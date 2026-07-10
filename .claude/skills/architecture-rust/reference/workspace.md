# Workspace shape — three ring *roles* off a shared core

The enterprise Rust solution organises crates by **three roles**: a kernel, capabilities, surfaces.
**These are roles, not a required crate list.** A project fills the roles it needs and leaves the
rest empty — the names below are illustrative (`said-build` proof-points in parentheses), never a
checklist. Read [§Project shapes](#project-shapes) first: a single-surface tool, an importer service,
or a lone library are all valid and must pass the rules unchanged.

A *maximal* layout (a multi-surface fleet) looks like this — but most projects fill only part of it;
see Project shapes for the single-surface, service, and library-only variants:

```text
workspace/
├── Cargo.toml                       # [workspace] members + [workspace.dependencies] + [workspace.package]
└── crates/
    │
    ├── core/                        # ── KERNEL role: EXACTLY ONE (may be the only crate) ──
    │   └── src/{lib.rs, model/, ports.rs, error.rs, engine.rs}
    │                                # domain types, port traits, the engine. WASM-safe. No I/O.
    │                                # (said-build: sca-core)
    │
    ├── <capability>/  …             # ── CAPABILITY role: ZERO OR MORE ─────────────────────
    │                                #    each owns ONE bounded capability; depends inward on
    │                                #    core; talks to siblings only through ports. A thin
    │                                #    tool may have none (logic lives in the kernel).
    │                                #    (said-build: said-vault, said-llm, said-forge,
    │                                #     said-prompts, + future Slack/Linear/GitHub packs)
    │
    └── <surface>/  …                # ── SURFACE role: ZERO OR MORE ────────────────────────
                                     #    each is ONE outer interface of whatever kind the
                                     #    project needs — cli, mcp, an importer service, an
                                     #    http api, a wasm module. Wire capabilities; NO
                                     #    business logic. A project may have one, many, or none.
                                     #    (said-build: said-cli, said-mcp, said-orchestration)
```

## The roles, precisely

| Role | Holds | Depends on | Target | Count in a project |
|------|-------|------------|--------|--------------------|
| **Kernel** | domain types, port traits, engine, pure algorithms | workspace deps only | every target incl. `wasm32` | exactly one (may be the *only* crate) |
| **Capability** | one bounded capability each (vault, llm, importer, search, …) | kernel; siblings *via ports only* | native; WASM if pure | **zero or more** |
| **Surface** | a single outer interface (CLI, MCP, service, HTTP API, WASM UI, …) | the capabilities it exposes; kernel | one target each | **zero or more** |

A surface is a thin presenter; the same capability is reachable from every surface **without
reimplementation**. Business logic in a surface is in the wrong role.

## Project shapes

Size to the project: count the **distinct outer interfaces** to expose (your surfaces — could be one
or zero) and the **distinct bounded capabilities** (could be zero, all in the kernel). Add a role
because the *project* needs it, never because the skill names it. All of these are valid:

| Shape | Kernel | Capabilities | Surfaces | Notes |
|-------|--------|--------------|----------|-------|
| **Pure library** | 1 (everything) | 0 | 0 | "cross-crate" rules become "cross-module" |
| **Single-surface tool** | 1 | 0–1 | **1** (e.g. MCP only) | don't invent CLI/WASM that aren't there |
| **Service** | 1 | 1+ | **1** (kind = importer/daemon/HTTP) | surface kind is "service"; same rules |
| **Multi-surface fleet** | 1 | many | many | the full three-role shape |

## New crate vs. extend — the test

Default to **extending** an existing crate. Create a **new crate** only when ≥1 holds:

1. **Distinct capability + language.** It models its own concept with its own vocabulary (a new Ring-2
   capability), or it's a new delivery surface (Ring-3).
2. **Reuse by ≥2 crates.** Two+ crates need it → extract to `core` or its own crate so neither owns the
   other.
3. **Independent target / ship boundary.** It must compile to a *different* target (a WASM-only or
   native-only boundary) or ship as its own bundle.
4. **Compile-hub relief.** It has grown into a dependency hub whose every change recompiles many
   reverse-deps; splitting restores incremental build.

If none hold, it's a **module inside an existing crate**, not a new crate. Premature crate-splitting
costs build-graph complexity; over-large crates cost compile time — the test above is where the line
sits. Record a real new-crate decision as an ADR (it's hard to reverse).

## Crate skeleton (Ring 2 — a capability)

```
crates/<capability>/
├── Cargo.toml          # [dependencies] core = { path = "../core" }; shared deps via workspace = true
└── src/
    ├── lib.rs          # public API: pub use of the service + ports + error; nothing else pub
    ├── error.rs        # <Cap>Error (thiserror) + <Cap>Result<T>
    ├── model/          # domain aggregates (private fields, behaviour methods)
    ├── ports.rs        # the trait ports THIS crate defines (for siblings/surfaces to depend on)
    ├── <feature>/      # one module per use case: the Service + request/response
    ├── adapters/       # impls of ports owned elsewhere; providers/ for runtime-swappable peers
    └── (feature-gated) # #[cfg(feature = "heavy")] pub mod heavy;
tests/                  # integration tests + fixtures/
examples/               # runnable public-API usage
```

## Crate skeleton (Ring 3 — a surface)

```
crates/<surface>/
├── Cargo.toml          # depends on the capability crates it exposes; anyhow (not thiserror)
└── src/
    ├── main.rs         # or bin/<name>.rs — parse input, build the registries/services, dispatch
    └── <wiring>.rs     # surface-specific glue only (arg parsing, MCP tool defs, WASM bindings)
```

A WASM surface's `Cargo.toml` must exclude every native-only dependency; it composes `core` (pure)
plus any WASM-safe capability crates, and reaches native-only capabilities through a boundary
(message passing / a host import), never by compiling them in.

## Workspace manifest (the hygiene baseline)

```toml
[workspace]
members = ["crates/*"]
resolver = "2"

[workspace.package]
edition = "2021"
license = "MIT"
version = "0.1.0"

[workspace.dependencies]            # declare every shared dep ONCE
serde      = { version = "1", features = ["derive"] }
tokio      = { version = "1", features = ["rt", "rt-multi-thread", "macros"] }
thiserror  = "1"
async-trait = "0.1"
# … member crates use:  serde = { workspace = true }
```

This `[workspace.dependencies]` baseline is a **migration target** for repos that still declare
shared deps per-crate (`said-build` does — it carries "already in workspace via X" comments instead);
the world-class bar is one declaration, no drift.
