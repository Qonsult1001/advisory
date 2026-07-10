---
name: verify-mcp
description: Drive the real built MCP server end-to-end and verify production behaviour — start it, list tools, call them with real arguments, assert on JSON-RPC responses and side effects. Compiles and tests EACH shipped build variant (feature bundle / target / profile) against its own tool list, because each variant advertises a different tool surface. Use when the user wants to test the MCP server for real, do production/E2E MCP testing, verify tool dispatch/schemas, or smoke-test MCP tools (not unit/integration tests).
---

# Verify MCP

Drive the **real built MCP server** end-to-end and judge it by what a client would see: the JSON-RPC
tool result, errors, and the state the call changed. The **gate is the real server's tool responses**,
nothing else. It drives the **MCP protocol surface** — `tools/list` then `tools/call` over the
transport — so it catches what `/tdd` (in-process handlers) and `/verify-cli` (argv/stdout) can't: tool
**schema** correctness, **dispatch** routing, error shape, and state held **across calls** on one
server session.

## The build-variant axis — an MCP server is often not one binary

Many projects ship the MCP server in **several build variants** — feature bundles
(`--no-default-features` + a feature set), build targets, or profiles — and **each variant advertises
a different tool surface.** A compile-gated tool (e.g. `#[cfg(feature = …)]`, build tags) **is not in
`tools/list`** for a variant lacking its gate; a runtime-gated tool **appears but no-ops / errors**
without its data/feature. Testing one server can't verify the product: the fullest variant hides "tool
wrongly advertised in the minimal variant" bugs; the minimal one can't exercise the heavy tools. So
this skill tests **each shipped variant separately, against its own tool list** — and a project that
ships exactly **one** server is just the trivial case (one variant).

**Discover the variant set — don't assume it.** Re-derive the variants and each one's exact build
command **from the project every run** (the repo drifts; never trust memory or an example table). Look,
in order, for whatever the project actually uses:

1. **CI release workflow** — the build matrix is the authoritative variant list + build command
   (`.github/workflows/*.yml`, `.gitlab-ci.yml`, `azure-pipelines.yml`, a `Jenkinsfile`).
2. **Build manifest** — the feature/target definitions (`Cargo.toml [features]`, `package.json`
   scripts, `go.mod` + build tags, a `pyproject.toml`).
3. **Packaging / release config** — `Dockerfile`(s), `goreleaser.yml`, a `Makefile`/`justfile` release
   target, install scripts.
4. **Gating in source** — what's compile-gated (`grep` the gate idiom: `#[cfg(feature`, build tags,
   env-switched tool registration) → which tools are variant-specific.

**If none of these exist or they don't define multiple variants, ASK the user**: "How is this MCP
server shipped — one binary, or several variants (feature bundles / targets / profiles)? What's the
build command for each?" Do not invent variants, and do not silently fall back to a default build.
Treat a confirmed single-server project as one variant and proceed.

For each discovered variant, record **what tools it uniquely advertises** — the ones you must exercise
*because the lighter variants can't* — and **what must NOT be in its `tools/list`** (tools behind gates
this variant doesn't enable; that absence, plus a clean error on `tools/call`, is itself a scenario).

> *Illustration only (a real project, `said-mcp`): four feature bundles mirroring its CLI — `brain`
> (text-memory tools) → `coding` (+ source-indexing tools) → `coding-plus` (+ LSP tools advertised) →
> `full` (+ PDF/DOCX/OCR ingest), each `--no-default-features`. An example of the shape, not a set to
> assume — discover the real one.*
>
> ⚠️ A plain default build (e.g. `cargo build` / `npm run build` with no variant flags) is usually
> **not** a shipped artifact. Verifying it means verifying something no user runs — build the real
> variants the release pipeline ships.

## Workflow

### 0. Pick the variants to verify

Enumerate the current shipped variants from the sources above, then **ask the user which to verify this
run** — present each with the tools it uniquely exercises, and let them pick one, several, or all.
Assume no default. **Completion criterion: the user has chosen an explicit set of variants, and for each
you hold its exact build command discovered from the project this run.**

### 1. Build and start the real server — once per selected variant

Build each selected variant with its discovered build command (release profile), fresh this run, and start it on its
transport. Record `{variant → running server}`. **Completion criterion: every selected variant built and
responds to an initialize/`tools/list` handshake; if any build or handshake fails, stop and report that
variant's error — never fall back to a different, default, or stale server.**

### 2. Enumerate each variant's tool surface and build its list

Call `tools/list` **on each variant's server** and read the **actual** advertised tools + input schemas.
Reconcile against what the docs claim (a tool claimed but absent, or present but undocumented, is a
finding). Each variant gets its **own self-contained scenario list**, which MUST include:

- **Positive scenarios** for the tools the variant is meant to deliver — especially the ones it uniquely
  adds (the tools the lighter variants can't exercise).
- **Negative-gating scenarios** — a tool that should be **absent** from this variant's `tools/list` is
  confirmed absent, and a `tools/call` for it errors cleanly (the tools behind gates this variant doesn't enable).
- **Error paths** — bad arguments rejected with a clean error envelope, not a panic.

Confirm each list with the user; prioritise critical tools. **Completion criterion: each selected variant
has its own list where every scenario names a tool, concrete arguments, and an expected observable
(result substring / clean error / state change), includes its negative-gating scenarios, and the live
tool list is reconciled against the documented one.**

### 3. Call each variant's tools on ITS server

Issue real `tools/call` requests over the transport against each variant's server, using an **isolated
brain/state file per variant** (temp copy, never live data, never state shared between variants).
Capture the **JSON-RPC result or error + any state change**. Where state-across-calls matters, run the
ordered sequence on **one server session** (e.g. `open` → `remember` → `ask`) and verify the later call
sees the earlier effect. **Completion criterion: every scenario has a captured actual response;
multi-call sequences run on a single session; none skipped silently — one you couldn't run is reported
and tagged with its variant.**

### 4. Judge against the expected observable

Compare each actual response to its expected observable — **the server's response is the sole judge**,
not your reading of the handler. Check three things per call: the **result content**, the **error
behaviour** (bad args fail cleanly, not panic), and the **side effect** on state. A tool absent because
the variant doesn't ship its feature is **not a fail** — it fails only if the variant is *supposed* to
advertise it. **Completion criterion: every scenario marked pass/fail against its observable, actual
JSON quoted for any fail, each result attributed to its variant.**

### 5. Report per variant, and capture failures as issues

Produce **one results table per variant** (tool · args · expected · actual · pass/fail), plus a
cross-variant summary — which variants fully passed, and any tool that regressed in one variant but not
another. File the **fails** to **`/qa`'s issue standard** (its template, the single-issue-vs-breakdown
decision, no file paths/line numbers, project domain language — that standard lives in `/qa`, don't
restate it here), **naming the variant in the title** so the build reproduces, or hand the list to `/qa`.
**Completion criterion: a per-variant table exists and every fail is filed to that standard (variant
named) or explicitly handed off — never left only in chat.**

## Checklist per run

```
[ ] Variant set + build flags re-derived from the project (CI / manifest / user) this run, not memory
[ ] Each selected variant built + handshake OK; tools/list reconciled against the docs
[ ] Each variant ran its OWN list (incl. negative-gating) against ITS server, isolated state
[ ] State-across-calls verified on one session; absence ≠ fail unless the variant should advertise it
[ ] One results table per variant; every fail quoted, attributed, and filed
```

## Boundaries

It **verifies and reports** — it does not fix (failures become issues; fixing is `/implement` or
`/tdd` after). A system exposing the same capabilities over **both** CLI and MCP should be verified on
**both** — they share a core but differ in dispatch, schema, and session state. This skill verifies the **MCP
server**; use `/verify-cli` for the project's CLI (discover its variants the same way).
