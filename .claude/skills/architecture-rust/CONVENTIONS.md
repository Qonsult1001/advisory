# Conventions — the Rust rulebook

The world-class target for an enterprise Rust workspace that ships multiple delivery surfaces
(CLI · MCP · orchestrator · global WASM · HTTP) over a shared capability core. This file is the
**source of truth**; it is ideal-first — it states the bar to migrate toward, not the floor any
single repo sits at today. Real patterns from `said-build` are cited as proof-points where they
already meet the bar.

## Three roles (capability = crate) — sized to the project

```text
workspace/
├── Cargo.toml                      # [workspace] + [workspace.dependencies] + [workspace.package]
└── crates/
    ├── core/                       # KERNEL (exactly one) — domain types, ports, engine. WASM-safe.
    ├── <capability>/  …            # CAPABILITY (zero or more) — one crate per capability (vault,
    │                               #   llm, importer, search…). Inward on core; siblings via ports.
    └── <surface>/     …            # SURFACE (zero or more) — ONE outer interface each, of whatever
                                    #   kind the project needs (cli, mcp, service, api, wasm).
                                    #   Wire capabilities; hold NO business logic.
```

**These are roles, not a required crate list.** Fill the roles the project needs; leave the rest
empty. A **pure-MCP** project = one surface (MCP) and nothing else. An **importer service** = one
surface of kind "service". A **library** = kernel only, zero surfaces. CLI/MCP/orchestrator/WASM are
*examples* of surface kinds — never a checklist. See [reference/workspace.md](./reference/workspace.md)
§Project shapes.

- **Capability = crate**, not layer. Split top-level by *what it does* (`vault`, `llm`, `importer`),
  never by `domain`/`infra` as sibling crates. Layering lives **inside** a crate (see §Layers).
- A crate is the unit of compilation, of the dependency graph, and of target selection. Prefer more
  small capability crates over one large one — Cargo parallelizes across crates, not within.
- The kernel is target-agnostic. **If the project has a WASM/constrained target**, the kernel must
  compile to it, and anything that can't (native I/O, threads, sockets, process spawn) belongs in a
  capability/surface crate that target never builds. If the project is native-only, this is moot.

## Dependency direction (inward only)

| From | May depend on | Must NOT depend on |
|------|---------------|--------------------|
| `core` (Ring 1) | `[workspace.dependencies]` only; pure crates | any capability crate; any surface; any native-only crate |
| `<capability>` (Ring 2) | `core`; a **sibling capability only through its port trait**; shared workspace deps | another capability's internals; any surface (Ring 3); a binary crate |
| `<surface>` (Ring 3) | the capability crates it exposes; `core` | another surface; reaching into a capability's internals |

Cross-capability collaboration = a **trait (port) defined in the lower/`core` crate**, `impl`'d by
the higher crate. Never a sideways internal reference. **A dependency cycle is a hard error.**
Minimise dependency hubs — a crate with many reverse-deps wrecks incremental compile.

## Layers inside a crate (the vertical slice)

Within one capability crate, organise by module, not by sibling crate:

- **Domain** — `model/{Aggregate}.rs` (types, invariants, private fields, behaviour methods),
  `ports.rs` (the trait ports this crate defines), `error.rs` (the typed error enum). No `async`
  runtime, no I/O, no framework types.
- **Application / service** — `<feature>/` module: the `Service` orchestrating ports, request/response
  types, validation. One module per use case (vertical slice).
- **Infrastructure / adapter** — `adapters/`, `providers/`, `persistence/`: concrete `impl`s of the
  port traits (HTTP, DB, file, model backends). Feature-gate the heavy ones.
- **Binding** — `lib.rs` re-exports the crate's public API; a Ring-3 crate's `main.rs` / `bin/` / WASM
  entry does wiring only.

Full snippets: [reference/slice.md](./reference/slice.md).

## The three extensibility mechanisms (do not interchange)

| Mechanism | When | Shape |
|-----------|------|-------|
| **Feature-gate** `#[cfg(feature = "x")]` | optional **heavy capability**; changes binary size/deps; ship-bundle control | gated `pub mod`; deps `optional = true`, pulled by the feature |
| **Trait registry** `Vec<Box<dyn Port>>` | **runtime-swappable peers**: providers, sources, adapters, dynamic plugins | port trait in `core`/domain + a `Registry` with `register` / `detect` / dispatch |
| **Optional crate** `dep = { optional = true }` | a **whole subsystem** is optional to a consumer | optional path-dep + a feature that enables it |

Picking wrong is the most common Rust architecture mistake (e.g. a `match` over providers that should
be a registry; an always-on parser that should be feature-gated). Full code: [reference/plugins.md](./reference/plugins.md).

## Ports & polymorphism

- A **port** is a trait, defined in the **domain** module of the crate that *owns the concept*, named
  for the role (`LlmProvider`, `DirectiveSource`, `EditorAdapter`, `Repository`, `Store`). `: Send + Sync`;
  `#[async_trait]` when async.
- Prefer **`dyn` dispatch** (`Box<dyn Port>`) for swappable peers and registries — less code, less
  binary bloat than generic monomorphisation. Reserve generics/associated-types for hot paths or
  where the concrete type must be known at compile time.
- A **factory** maps config → boxed port: `fn provider_from_config(cfg) -> Result<Box<dyn LlmProvider>>`.

## Error handling

- **Library / capability / core crates → `thiserror`.** One typed enum per crate: `#[derive(Error)]`,
  named `<Domain>Error`, with a `pub type <Domain>Result<T> = Result<T, <Domain>Error>;` alias. Add a
  classification enum (`<Domain>FailureClass`) when callers need retry/circuit-breaker decisions.
- **Binary / surface crates → `anyhow`** for contextual happy-path wiring. `anyhow` in a library is a
  violation.
- Never `Result<T, String>` across a public boundary; never `unwrap()`/`panic!` on a recoverable path.

## WASM / offline constitution (conditional — applies only if the project has these targets)

**This whole section is gated on the project actually having a WASM/constrained target and/or an
offline-first product constraint.** A native-only service or a plain library is exempt from rules
1–3; rule 4 (explicit state) is good practice everywhere. Apply what the project's targets demand —
nothing more.

1. **Kernel compiles to `wasm32`** *(if a WASM target exists)*. CI proves it
   (`cargo check --target wasm32-unknown-unknown -p core`).
2. **No native-only I/O on a constrained-target path** *(if such a target exists)* — no `std::fs`,
   `std::net`, `std::thread`, `tokio::net`, `std::process::Command`, or native `tokio` runtime in the
   kernel or any crate that target compiles. Push these into adapter crates the constrained build
   excludes.
3. **The two rules** *(if offline-first is a product constraint)*, stated precisely:
   - **Offline-first.** An integration must produce value with **no network call required**. Online is
     the *exception*, permitted only when the source of truth is **inherently online** and no local
     snapshot exists (prefer a user-driven local export first; a `*-live` variant follows). Each new
     capability must answer which side of this line it's on.
   - **The heavy/online dependency lives outside the binary.** The shipping (and WASM) binary **never**
     calls an LLM — not for ingest, retrieval, or consolidation. LLM/online work is a **separate named
     process** that is an MCP/IPC *client* of the core, installable separately and **absent by
     default** (so `ls` answers "does this binary call an LLM?" definitively). It is **one named
     sidecar, not a discovered plugin system**. See [patterns.md](./reference/patterns.md) #8.
4. **State is explicit** *(always)*. Pass clocks, loggers, metrics, config as parameters — **no
   thread-local globals** (they break WASM, tests, and cause multi-version linking bugs).

## Workspace hygiene (enterprise bar)

- **`[workspace.dependencies]`** declares every shared dep once; member crates use
  `dep = { workspace = true }`. Per-crate version literals for a shared dep are a violation (drift +
  duplicate linked versions). *(said-build does not do this yet — it's a migration target, not the
  standard.)*
- **`[workspace.package]`** holds shared version / license / edition / authors.
- **CI gate (all must pass):** `cargo fmt --check` · `cargo clippy --all-targets -- -D warnings` ·
  `cargo test --workspace` · *(only if a WASM target exists)*
  `cargo check --target wasm32-unknown-unknown -p core` · *(only if ship bundles exist)* the
  feature matrix. Smoke-test-only CI is below the bar.

## Module & naming conventions

- Crates: kebab-case, capability-named (`said-vault`, `said-llm`); a short core name (`sca-core`) is
  fine. Surfaces named for the surface (`said-cli`, `said-mcp`).
- Modules: `snake_case`; a directory module is `name/mod.rs` + submodules.
- Types/traits: `PascalCase`. Errors `<Domain>Error`; result alias `<Domain>Result<T>`; port traits
  named for the role.
- Visibility: `pub(crate)` by default; `pub` only the deliberate public API, re-exported from `lib.rs`.
  No public field that bypasses an aggregate invariant.

## Testing

- **Unit tests in a separate `#[cfg(test)] mod tests`** (own file where the module is large) — keeps
  the build cache clean.
- **Integration tests in `tests/`**, one concern per file; shared data in `tests/fixtures/`.
- **`examples/`** for runnable, documented usage of a crate's public API.
- A `stub` adapter behind a `stub-<x>` feature (also auto-on under `cfg(test)`) lets integration
  tests exercise a port without the real backend.

## Violation table — flag on sight

| Violation | Rule |
|-----------|------|
| Top-level crate named `domain` / `infrastructure` | Split by capability, not layer (§Three rings) |
| Capability crate depends sideways on another's internals | Inward-only; collaborate via a port (§Dependency direction) |
| Any crate depends on a binary crate; a dependency cycle | Hard error (§Dependency direction) |
| `anyhow` in a library crate | `thiserror` in libs, `anyhow` in binaries (§Error handling) |
| `Result<T, String>` across a public boundary | Typed `<Domain>Error` (§Error handling) |
| Port trait defined in an adapter/`providers/` module | Ports live in the domain module (§Ports) |
| `match` over swappable peers instead of a registry | Use a trait registry (§Three mechanisms) |
| Always-on heavy module / dep | Feature-gate it (§Three mechanisms) |
| `std::fs`/`net`/`thread`/`Command`/native `tokio` on a constrained-target path | Constitution rule 2 *(only if that target exists)* |
| LLM/network call inside a shipping or WASM binary | Constitution rule 3 *(only if offline-first)* |
| Thread-local / global mutable state | Constitution rule 4 — pass state explicitly *(always)* |
| Per-crate version literal for a shared dep | Use `[workspace.dependencies]` (§Workspace hygiene) |
| `pub` field exposing an aggregate invariant | `pub(crate)` + behaviour method (§Naming) |
| A delivery surface holding domain logic (a 100+-line handler) | Thin-surface-over-core ([patterns.md](./reference/patterns.md) #6) |
| Mutable audit rows / a `deleted` flag instead of an append | Append-only hash-chained log ([patterns.md](./reference/patterns.md) #1) |
| Destructive in-place update with no restore path | Version chain + two-phase delete ([patterns.md](./reference/patterns.md) #4) |
| Dedup logic copy-pasted into callers instead of the store boundary | Content-addressed store ([patterns.md](./reference/patterns.md) #2) |
| The same peer in a registry **and** a duplicate `match` | Registry-not-match dispatch ([patterns.md](./reference/patterns.md) #7) |
| The same prompt/config/string inlined in CLI + MCP + WASM | Single-source-of-truth registry ([patterns.md](./reference/patterns.md) #5) |
