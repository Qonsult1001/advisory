---
name: sdk-factory
description: Scan a system and generate a world-class, fully-documented SDK for it — a typed client (any target language; application, web, or MCP/agent consumers), line-by-line documentation a non-technical person can follow, an architecture record, and an agent-native knowledge wiki (OKF). Use when the user wants an SDK, a client library, a web or MCP/agent client, an integration package, partner/consumer docs, or an agent-readable knowledge wiki for an API or system.
---

# SDK Factory

Scan a system and produce four things, inseparable: a **typed SDK client** that a consumer wires in
one call, **line-by-line documentation so complete a person who has never touched a computer could
follow it to a working integration**, an **architecture record** that shows how the client is shaped
and *why* (diagrams + a deep-module depth audit + a decision map), and an **agent-native knowledge
wiki** an LLM reads natively (Open Knowledge Format — markdown + YAML frontmatter, cross-linked into a
graph). The client alone is a black box; the docs are the differentiator for humans; the architecture
record lets a maintainer change the SDK without breaking its shape; the wiki gives an agent a
navigable graph of the surface with no SDK or translation layer. The bar is **radical clarity through
specificity**: exact commands, exact parameter names, exact "you should see…" after every step, exact
contact when stuck.

The consumer may be an **application** (a portal/service), a **web frontend** (npm package or
script-tag widget), or an **LLM agent** (an MCP server + client). The recipe's shape holds across all
three; only the packaging and transport change.

The SDK generation is a **recipe** (proven on a real multi-platform banking SDK — C#/Android/iOS/web,
a BFF service-composition layer, and an MCP agent ingress); the documentation and the architecture are
**standards** (every section, every callout, every diagram, codified). All live in reference files;
this page is the ordered method.

## The four deliverables, never fewer

- **Generate** — a typed, dependency-registerable, correlation/auth/resilience-aware client, for an
  application, web, or MCP/agent consumer. Language-agnostic method; reference:
  [reference/sdk-recipe.md](./reference/sdk-recipe.md).
- **Document** — the consumer guide + developer guide to the radical-clarity standard, including the
  Developer Guide's build/publish/versioning section and per-platform doc shape. **This is the
  differentiator for humans.** Reference: [reference/doc-standard.md](./reference/doc-standard.md).
- **Architect** — the module-dependency diagram, the request sequence diagram, the depth audit
  (deletion test per module), and the decision map. Reference:
  [reference/architecture-standard.md](./reference/architecture-standard.md).
- **Publish a wiki** — an agent-native OKF knowledge bundle of the operations and concepts, the same
  files a human reads and an agent parses. **This is the differentiator for agents.** Reference:
  [reference/wiki-standard.md](./reference/wiki-standard.md).

A submission that generates code but documents it for experts only — or ships no architecture record,
or no agent-native wiki — has **failed this skill.**

When the system is bigger than one SDK — **many apps and backends that must connect through one point**
— the SDK is organized around a **single orchestration layer**: one contract every consumer goes
through, with interchangeable backends behind it (the FutureBank `IBankingAdaptor` pattern). Apply this
the moment a *second* backend or *second* consumer is real. Reference:
[reference/orchestration-standard.md](./reference/orchestration-standard.md).

## Workflow

### 1. Scan the system — establish the real surface

Read the system's surface from its source of truth: an OpenAPI/Swagger spec if one exists; else the
real endpoints (controllers, routes, request/response models); else — for an agent-facing system — the
**MCP tool definitions** (each tool's input schema + the exact response it emits). For each operation
capture: method/path *or* tool name, parameters (name · type · required), request body/args, response
shape, auth requirement, idempotency key, error shapes. **Completion criterion: a complete operation
inventory exists — every endpoint or tool, every parameter, every response — traced to the real
surface; nothing assumed from a name. An operation you couldn't verify is flagged, not invented.**

### 2. Choose the target(s) and confirm the consumer

Decide which SDK target(s) the consumer needs — driven by *who consumes it*, not by the backend's
language. The consumer dictates the form:

- an **application** → C#/.NET (DI), Kotlin/Android, Swift/iOS, Python, TS service;
- a **web frontend** → TypeScript, shipped as an npm package **and/or** a `<script>`-tag widget;
- an **LLM agent** → an MCP server + a thin typed client over its tools.

Confirm with the user. For multi-platform SDKs, confirm *each* platform against a named consumer.
**Completion criterion: each target is chosen against a named consumer (the portal, the Android app,
the agent…), the platform's package idiom is identified, and the auth/session model is identified.**

### 3. Generate the SDK to the recipe

For each target, generate the client per [reference/sdk-recipe.md](./reference/sdk-recipe.md): the
**typed client** (generated from the spec where possible, e.g. NSwag/OpenAPI generators; or one typed
method per MCP tool), the **one-call registration**, the **session/auth resolver**, **correlation-ID +
resilience (retry) + logging** threaded through every call set up once, the **DTO→domain mappers**, and
a **package** a consumer installs (NuGet/Maven/Pods/npm/script-tag/MCP-server). Add an **adaptor seam
only if more than one implementation exists**, and ship a **mock/fake adaptor** for offline dev + CI.
Add either orchestration layer **only when it earns its place**: an **MCP agent ingress** when an LLM
consumes the ops (part 11), a **BFF service-composition** layer when one business operation is several
SDK calls — composing, never proxying (part 12).

**When the layer fronts two real codebases (e.g. an old SDK and its rewrite), STOP at the
reconciliation gate before generating.** Run the **grilling discipline** (the `grilling` skill) over the
scan to pin the human down on union-vs-intersection, new-only/old-only ops, divergent signatures, and
parity risk — producing a written operation map (every op tagged *both*/*old-only*/*new-only*/*divergent*
with a recorded decision) **before** any code. See
[reference/orchestration-standard.md](./reference/orchestration-standard.md#the-reconciliation-gate--grill-before-you-generate-mandatory).

**Completion criterion: the SDK exposes every inventoried operation as a typed method; a
consumer can install → register in one call → inject → call → get a typed result; cross-cutting applies
uniformly; a mock/fake lets a consumer run with no live backend; and when ≥2 codebases are fronted, the
reconciliation operation-map exists with the human's decisions recorded.**

### 4. Prove the SDK with tests

Generate tests that exercise the SDK's public surface against a mocked endpoint (per the recipe's test
pattern — WireMock for HTTP, an in-process mock server for MCP/stdio): registration resolves, the right
headers/token/correlation/args are sent, the response maps to the typed domain model. **Completion
criterion: every public method has a test asserting its observable behaviour (sent request + mapped
response); the test suite passes.**

### 5. Write the documentation to the standard

This is the deliverable that makes it world-class. For each target, write a **Consumer Guide** and a
**Developer Guide** to [reference/doc-standard.md](./reference/doc-standard.md): prerequisites as a
checklist · numbered single-action steps · a **"you should see…"** verification after *every* step ·
every config parameter documented (name · type · where to get it · required/optional · consequence) ·
the dependency/initialisation order spelled out · `Note:` callouts for gotchas · who to contact when
stuck (a **named human**) · copy-paste-ready snippets with per-line explanation. The Developer Guide
MUST include the **build/publish/versioning** section (CI jobs trigger→action→output, release
workflow, version policy) and honour the **per-platform doc shape**. Document auth/session flows
end-to-end when present. **Completion criterion: a reader with no prior knowledge could reach a working
integration from the doc alone — every step has a verification, every parameter is documented, no
jargon is undefined, the consumer-vs-developer split is honoured, and the Developer Guide says how to
ship a release.**

### 6. Produce the architecture record

Write the architecture record per
[reference/architecture-standard.md](./reference/architecture-standard.md): the **module-dependency
diagram** (with the single external seam marked), the **request sequence diagram** (showing
cross-cutting applied once), and the **depth audit + decision map** (deletion test per module; each
decision as Decision · Alternative rejected · Why; what varies across any adaptor seam). **Completion
criterion: from the record alone, a maintainer can draw the dependency graph and name the external
seam, trace one call and point to where correlation/retry/logging apply, and say for any module whether
it's deep or shallow — and answer "what would I change to add an operation / swap the backend / add a
platform".**

### 7. Publish the agent-native wiki

Write the knowledge wiki per [reference/wiki-standard.md](./reference/wiki-standard.md): an **OKF**
bundle (markdown + YAML frontmatter, cross-linked into a graph) with `operations/`, `concepts/`, and
`guides/` folders that point into the human docs. Every operation traces to the same scanned surface
as the client — invented, never assumed. **Completion criterion: the bundle OKF-conforms (every
non-reserved file has a non-empty `type`; only `index.md` and `log.md` are reserved), its cross-links
resolve into a connected graph, every inventoried operation has a document or index entry, and an agent
handed only the bundle can name an operation's parameters and follow a link to the concept it depends
on.**

### 8. Verify the whole thing end-to-end

Run a sample: follow your own Consumer Guide, paste its commands/snippets, and confirm each produces
the documented "you should see…". Where a real backend/binary exists, run against it, not just a mock.
Verify the wiki conforms and its links resolve. Fix any drift between the docs, the architecture
record, the wiki, and the real SDK. **Completion criterion: a representative path through the Consumer
Guide has been executed and matches the documentation; the SDK, its docs, its architecture record, and
its wiki agree — evidence, not assertion.**

## What this is not

- Not raw codegen — a bare generated client with no registration, no cross-cutting, no docs, no
  architecture record is a starting point, not an SDK.
- Not API reference docs alone — those describe endpoints; this teaches a human to integrate.
- Not diagrams alone — the architecture record is one of four deliverables, never the whole job.
- Not a wiki alone, nor docs without one — the human guides and the agent-native OKF wiki are
  separate deliverables for separate readers; ship both.
- Not a single-platform assumption — when the consumer spans application + web + agent, the SDK spans
  them too, and the docs say what differs per platform.
- Not the user-manual skill (`document-user-manual` documents a *running system's* CLI/MCP); this
  produces a *client library* and its integration docs for *developers and the apps that embed it*.
