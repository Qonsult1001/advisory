# SDK generation recipe

The reproducible anatomy of a production SDK client. **Language-agnostic** — the shape below holds in
C#, Kotlin, Swift, or TypeScript; the snippets are the proven C#/.NET reference (FutureBank), which you
translate to the target. The cardinal rule from the method applies: generate from the **real scanned
surface**, never from memory.

## The shape: one package, seven parts

Every generated SDK has these seven parts. Miss one and it's raw codegen, not an SDK.

| # | Part | Role | Generic / generated? |
|---|------|------|----------------------|
| 1 | **Typed client** | one typed method per API operation | **generated** from the spec (NSwag/OpenAPI generator) where possible |
| 2 | **One-call registration** | consumer wires the whole SDK in a single call | hand-written, per target's DI idiom |
| 3 | **Session / auth resolver** | consumer supplies token + identity once | interface the consumer implements |
| 4 | **Cross-cutting handler** | correlation-ID, retry/resilience, logging on **every** call | uniform, threaded through the client |
| 5 | **Mappers** | wire DTO → the consumer's domain model | static, explicit, no reflection magic |
| 6 | **Package** | install/distribute (NuGet / Maven / CocoaPods / npm) | per target |
| 7 | **Tests** | prove the public surface against a mocked endpoint | per the test pattern below |

## 1. Typed client — generate from the spec

When an OpenAPI/Swagger spec exists, **generate** the typed client rather than hand-writing it. The
FutureBank reference uses an NSwag config (`Nswag.nswag`): OpenAPI in → a typed `I<Name>Service`
interface + implementation out, with `generateClientInterfaces: true`, `injectHttpClient: true`,
`generateExceptionClasses: true`, `jsonLibrary: SystemTextJson`. Equivalent generators per target:
`openapi-generator` (Kotlin/Swift/TS), `swift-openapi-generator`, `NSwag`/`Kiota` (C#). The generated
client is then **wrapped by a hand-written facade** (parts 2–5) so the consumer never touches the raw
generated surface.

**Two branches — which path generates the client:**

- **Code-scan branch (default, no spec).** When there is *no* OpenAPI spec — only source (controllers,
  routes, models) or MCP tool definitions — the agent **scans the code** (workflow step 1) and
  hand-authors the client to this recipe. This is the higher-quality, hand-crafted path; it needs no
  spec and is how the C# `Said.Sdk` (scanned from `tools.rs`) and any stored-proc/legacy surface get an
  SDK. **A spec is never a prerequisite of this skill.**

- **Spec-driven branch (automated, when a spec exists or can be emitted).** When the system emits an
  OpenAPI spec — or can with one line (e.g. .NET's `AddOpenApi()`, FastAPI, Swashbuckle) — point a
  generator at the spec and emit real native clients for *every* target at once
  (`swift6`/`kotlin`/`python`/`csharp`/`typescript-fetch`/… — 80+). This is the **multi-platform
  shortcut**: one spec → native SDKs no toolchain on your side could otherwise produce. When a product
  *owns* this pipeline (a no-code/orchestration layer turning its own surface into SDKs on demand), make
  the generator a **self-hosted engine** (Docker `openapi-generator-cli`), never the public beta service
  (`api.openapi-generator.tech`, "no service-level guarantee") — so it's always up, always yours, and
  the engine stays an invisible, swappable seam behind your own `/sdk/generate` route. Brand the output
  (package id/namespace) so it's *your* SDK, not generic codegen. See the worked Factory pipeline in
  `docs/FACTORY-SDK-PIPELINE.md`.

  **Spec hygiene — two failures that abort the whole generation (fix in the spec, not the generator):**
  (1) **Bare free-form objects** — a `{ "type": "object" }` with no `properties`/`additionalProperties`
  (e.g. an untyped bag) → the generator can't name the model ("schema name: null") and rejects the spec;
  fix by setting `additionalProperties: true`. (2) **Spurious integer `pattern`** — .NET 10 emits a
  regex `pattern` on int32/int64 fields (often with only `format`, no `type`) that the Python/other
  generators reject ("Pattern must follow the Perl /pattern/ convention"); strip it (the `format`
  already constrains the value). Normalize the spec at the JSON level before it reaches the engine.

The two branches compose: a code-scanned, hand-crafted facade for the language you ship first; the
spec-driven engine for the long tail of native platforms. Both end at the same wrapped, registerable
client (parts 2–5).

## 2. One-call registration (the consumer's whole setup)

The consumer wires everything in one line. C#/.NET reference:

```csharp
public static void Add<Name>Sdk(this IServiceCollection services, string basePath)
{
    services.AddHttpClient(HttpClientNames.SdkClientName, c => {
            c.BaseAddress = new Uri(basePath);
            c.Timeout = TimeSpan.FromSeconds(15);
        })
        .AddPolicyHandler(GetRetryPolicy())            // part 4: resilience
        .AddHttpMessageHandler<AddCorrelationHeader>(); // part 4: correlation
    services.AddScoped<I<Name>Client, <Name>IntegrationService>(); // the facade
}
```

Consumer side, in full: `services.Add<Name>Sdk("https://api…");` — then inject `I<Name>Client`. The
target's idiom differs (Koin/Hilt module on Android, an initializer on iOS, a factory on TS) but the
**one-call** property is invariant.

**Ship a minimal front door too — the "world-class ease" lever.** DI registration is right for a host
app, but it is *ceremony* for a developer who just wants to call the thing. The best SDKs (e.g. the
Navixy JS SDK: `const API = new Navixy.Api({user:{hash}}); API.tracker.list()`) give a **one-line
constructor + fluent ops**. Provide a thin facade over the typed client so the 80% case is frictionless:

```csharp
var said = new SaidApi("willie.said");              // one line — no options object, no DI
await said.Remember("note", Pillar.Semantic);        // fluent, typed
var answer = await said.Ask("what did the user say?");
await said.Request("remember", new { content="n" }); // generic escape hatch (Navixy's API.request)
```

The front door **wraps** the same typed client (parts 1, 4, 5 reused — never a parallel implementation)
and adds three things: a **one-line constructor** (sensible defaults, no options ceremony), **fluent
shortcut methods** for the common ops, and a **generic `Request(tool, args)`** escape hatch with
optional response unwrapping — the JS SDK's `API.request(name, params, rootProperty)`. Keep the DI
registration for hosts; ship the front door for everyone else. A consumer should reach a working call
in **one line of construction**.

## 3. Session / auth resolver (consumer supplies identity once)

The SDK does not own credentials; the consumer implements a resolver the SDK reads on every call:

```csharp
public interface ISessionResolver {
    string Token { get; }          // bearer token
    string CorrelationId { get; }  // per-request trace id
    string UserProfileId { get; }  // identity context
}
```

## 4. Cross-cutting — uniform on EVERY call

The thing that separates an SDK from scattered HTTP calls: auth, correlation, retry, and logging apply
**identically to every operation**, set up once. In the reference, a request builder threads them:

```csharp
var response = await new HttpRequestBuilder()
    .Initialize(_httpClientFactory, _logger)
    .AddMethod(HttpMethod.Get).AddUrl("api/v1/accounts")
    .AddClientName(HttpClientNames.SdkClientName)
    .AddToken(_session.Token)                              // auth
    .AddXUserCorrelationIdHeader(_session.CorrelationId)   // tracing
    .Send<List<GetAccountsResponse>>(ct);                  // typed
return response.Map();                                     // part 5
```

Resilience is one shared policy (e.g. retry transient errors N times with backoff), applied via the
registration — never re-implemented per method. **Named HTTP client per SDK** (`"<domain>-http-client"`)
lets multiple SDKs coexist in one consumer without collision.

## 5. Mappers — DTO → domain, explicit

Map the API's response DTOs to the consumer's domain model with **plain, compile-checked** functions
(static extension methods in C#; pure functions elsewhere) — no reflection, so a reader sees exactly
what maps to what:

```csharp
public static Account Map(this GetAccountsResponse dto) => new() {
    AccountNumber = dto.AccountNumber, AvailableBalance = dto.AvailableBalance,
    Cards = dto.Cards.Map() };   // nested mapping, explicit
```

## 6. Package — install/distribute per target

Make it a real installable package, versioned: NuGet (`.csproj` with package metadata + `InternalsVisibleTo`
the test project) for C#; Maven/Artifactory for Kotlin; CocoaPods/SPM for Swift; npm for TS. One
version across a multi-module SDK simplifies consumer compatibility.

## 7. Tests — prove the public surface

Test the SDK the way a consumer uses it: register it, mock the endpoint, call the method, assert the
**sent request** (right URL, token, correlation header) and the **mapped response** (typed domain
model). Reference uses WireMock + the real DI container:

```csharp
services.Add<Name>Sdk(_wireMock.Url);                 // real registration
_wireMock.Given(Request.Create().WithPath("/api/v1/accounts").UsingGet())
         .RespondWith(Response.Create().WithBodyAsJson(stub));
var sut = await provider.GetService<I<Name>Client>().GetAccountsAsync();
sut.First().Cards.Count().Should().Be(1);             // mapping verified
```

## 8. The adaptor seam (only when something varies)

Reach for an adaptor interface **only when more than one implementation exists** — *one adapter is a
hypothetical seam; two is a real one.* The FutureBank SDK ships **three** behind one `IBankingAdaptor`
contract — `Gateway`, `DirectTransact`, `Mock` — switchable by which registration the consumer calls:

```csharp
services.AddAdaptorGatewaySdk(basePath);   // gateway adaptor  (15s timeout, x-adaptor-identifier header)
services.AddDirectTransactSdk(basePath);   // direct adaptor   (30s timeout, no routing header)
services.AddMockSdk(basePath);             // mock adaptor     (test/dev double, same contract)
```

What varies across the seam (timeout, routing header, backend) is the *only* thing the adaptor layer
owns; correlation, retry, mappers, and the request builder are shared. If the SDK has a single backend,
**do not** invent an adaptor interface — that's a shallow seam nothing crosses. The decision record
([architecture-standard.md](./architecture-standard.md)) is where what-varies gets written down.

**At system scale, this seam becomes the orchestration layer.** When the second backend is a whole
*system* and many apps must go through one contract, the adaptor seam is no longer inside one SDK — it
*is* the architecture: one composite contract, interchangeable backends, one-line selection,
cross-cutting threaded through the single point. See
[orchestration-standard.md](./orchestration-standard.md).

## 9. The mock adaptor / fake (ship one for local dev + CI)

Ship a **fake implementation of the same public contract** that returns predictable data without a
real backend, so consumers develop and run CI offline — part of the product, not the test suite.
FutureBank's mock adaptor implements every method against a test double on the *same execution path*
(it still goes through the request builder), so behaviour matches production. A stdio/MCP target ships
an in-process mock server instead.

## 10. Multi-platform — the target drives the doc AND the package shape

The recipe's shape is invariant across targets, but **packaging, idiom, and even doc structure differ
per platform.** Decide targets by *who consumes it*, then translate:

| Target | Registration idiom | Package / distribution | Generated client |
|--------|--------------------|------------------------|------------------|
| C#/.NET | `services.AddXSdk(basePath)` (DI extension) | NuGet (`.csproj` + `InternalsVisibleTo`) | NSwag / Kiota, or a hand-written request builder |
| Kotlin/Android | Koin/Hilt module, or `FutureBank.init(...)` in `Application.onCreate()` | Maven / Artifactory; one `semver.properties` across all modules | openapi-generator |
| Swift/iOS | an initializer; CocoaPods `pod install` → `.xcworkspace` | CocoaPods / SPM; per-module versioned | swift-openapi-generator |
| TypeScript/web | a factory `createXClient(options)` | npm; **or a `<script>` tag embed** for a browser widget | openapi-generator / openapi-typescript |
| MCP (agents) | the MCP server *is* the surface; the SDK spawns/connects it | the server binary + a thin client package | one typed method per MCP tool |

A browser/web target often needs **both** ingress modes — an npm package *and* a `<script>`-tag embed
with token-based init (backend-issued `accessToken`, optional locale). Both are first-class; neither
is the afterthought.

## 11. Agent ingress — the MCP layer (expose the SDK to agents)

There are **two** orchestration layers an SDK can grow, and they are different jobs: an **agent
ingress** (this part — exposing operations to an LLM) and a **service composition** layer (part 12 —
composing operations into one business operation). Don't conflate them.

When the consumer is an LLM agent, the integration surface is an **MCP server** (tools + resources +
prompts) over the SDK's operations — *the same operations, a different ingress.* The FutureBank MCP
layer maps each banking operation to an MCP **tool** with a JSON input schema, threads the same
correlation/auth/idempotency as the SDK, and adds per-tool authorization + audit:

```json
{ "name": "balance_enquiry",
  "description": "Retrieve account balance for a linked account",
  "inputSchema": { "type": "object",
    "properties": { "accountNumber": {"type":"string"},
                    "requestId": {"type":"string","description":"Idempotency key"} },
    "required": ["accountNumber","requestId"] } }
```

An MCP-target SDK is the mirror image: the **server** exposes the tools; the **client SDK** wraps it
with one typed method per tool, mapping each tool's response to a domain type. The same parts hold —
the transport is stdio JSON-RPC instead of HTTP, and the correlation header becomes the call's `_meta`
correlation id. Idempotency, per-tool auth, and audit are cross-cutting here too. When an SDK and an
MCP layer coexist, they share the session/auth resolver and the mappers.

## 12. Service composition — the BFF layer (compose ops into one business operation)

The other orchestration layer (distinct from part 11's agent ingress): a **Backend-for-Frontend**
that turns *one* business operation a consumer wants into the *several* SDK calls it takes — so iOS,
Android, and web hit a single composed endpoint instead of orchestrating the fan-out themselves. The
FutureBank BFF (`AccountsService`) is the reference: it threads the shared request builder, exchanges
the inbound token for a downstream one (`.AddToken(ExchangeToken)`), calls each operation through a
named downstream client, and maps the responses to the consumer's domain model at the boundary.

What a BFF layer owns that the raw SDK does not:

- **Composition** — one method = N SDK calls (e.g. open-account = create 3 accounts + link
  beneficiary + authorize), with **one correlation id** spanning the fan-out.
- **Token exchange** — swap the consumer's identity token for the downstream credential, once.
- **Idempotency across the fan-out** — a `requestId` that makes the *whole* composed operation
  replay-safe, not just each leg.
- **Boundary shaping** — map downstream DTOs to the consumer's domain at the edge, so the apps never
  see downstream contracts.

**The guard — compose, don't proxy.** A BFF earns its place only when it *composes*; a layer that
forwards one call to one downstream and back is a **pass-through proxy** — a shallow seam that adds an
operational hop for no leverage (the FutureBank write-off flagged exactly this: *"the stack on top
just passes the request down"*). Apply the deletion test: delete the BFF method — if a single SDK call
reappears unchanged, it was a proxy; if a multi-call composition reappears in every consumer, it
earned its keep. Record the verdict in the decision map
([architecture-standard.md](./architecture-standard.md)).

## 13. Code export / handoff (when a no-code or visual layer sits on the SDK)

If the SDK powers a **no-code / drag-and-drop / visual builder**, a non-developer assembles something
(a flow, a query, a config) and then needs to **take it into their own app** — on their website,
mobile app, or backend. The builder must offer a one-click **Export** that hands them runnable code,
the way Navixy's API lets you trigger a saved flow from any language. Three artifacts, ranked by ease:

- **API-call snippets in every consumer language.** The visual artifact runs against an endpoint; emit
  the call (the `/run`-equivalent POST) in each language the consumer might use — **C#, Python,
  JavaScript/web, Kotlin/Android, Swift/iOS, cURL.** Every platform can hit a REST endpoint, so this
  works even where no native SDK exists. Where a real **SDK** exists for a language (e.g. the C# one),
  that tab uses the SDK's front door (`new XApi(...)` → typed methods), not a raw HTTP call.
- **The MCP client config (agent ingress).** If the system is MCP-backed, include an **MCP** export:
  the `mcpServers` config block a user drops into Claude Desktop / Cursor to connect an AI agent
  straight to the operations — the same tools the artifact uses, now agent-callable (Navixy ships
  exactly this as its MCP). Never omit MCP for an MCP-native system; it's the most authentic export.
- **The portable definition (import/export).** Download the artifact as its native document (JSON) and
  re-import it elsewhere — versionable, shareable, movable between instances.
- **A standalone program** (optional, the premium tier): generate a small program that embeds the SDK
  and runs the artifact's operations directly, for true on-prem with no server.

The export is **generated from the real artifact** (parse its nodes/ops, emit the matching calls) —
never a static template. A no-code tool that can't give a developer the code has trapped the user's
work inside it.

## The recipe, condensed

```
scan spec/tools ─▶ 1 generate typed client — code-scan (no spec, hand-author) OR spec-driven
                     (openapi-generator, 80+ native targets from one spec; self-host the engine,
                     normalize the spec, brand the output); one method per MCP tool when agent-facing
                ─▶ 2 hand-write one-call registration (target DI idiom / factory / MCP connect)
                ─▶ 3 session/auth resolver interface (consumer implements)
                ─▶ 4 thread correlation + retry + logging through EVERY call, set up once
                ─▶ 5 explicit DTO→domain mappers
                ─▶ 6 package + version (NuGet/Maven/Pods/npm/script-tag/MCP-server)
                ─▶ 7 tests: register, mock endpoint (WireMock / mock server), assert sent-request + mapped-response
                ─▶ 8 adaptor seam ONLY if >1 implementation (record what varies)
                ─▶ 9 ship a mock/fake adaptor for offline dev + CI
                ─▶ 10 per-target packaging + idiom (incl. web script-tag, MCP)
                ─▶ 11 agent ingress: optional MCP layer to expose ops to agents
                ─▶ 12 service composition: optional BFF layer — compose ops, never proxy
                ─▶ 13 code export: if a no-code layer sits on top, one-click export to runnable
                                   code in every consumer language (C#/Python/JS/Kotlin/Swift/cURL) +
                                   the portable JSON — generated from the artifact, never a template
```

The invariant across every target and platform: **install → one-call register → inject → call typed
method → typed result**, with auth/correlation/retry/idempotency already handled. Translate the
snippets; keep the shape. Where multiple platforms or adaptors exist, the **architecture record**
(see [architecture-standard.md](./architecture-standard.md)) is what keeps them coherent.
