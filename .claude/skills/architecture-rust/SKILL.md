---
name: architecture-rust
description: Decide where new Rust code lands — which crate, which layer, which extensibility mechanism, and keep it WASM/offline-safe. Use when adding a feature, slicing issues, placing a module, adding a plugin/format/language/provider, reviewing a diff for layer or target leaks, or when another skill needs Rust placement decisions.
---

# Architecture (Rust)

Decide **where code physically lands** in an enterprise Rust workspace, and keep it there. The Rust
peer of the C#/.NET [architecture-csharp](../architecture-csharp/SKILL.md) skill — same spine, different unit:
where .NET gives **four `.csproj` projects per context**, Rust gives **crates in a workspace**, with
layering as **modules inside a crate** off a shared **core**.

**Ideal-first**: the rulebook is the world-class target to migrate *toward*. `said-build` is cited as
a **proof-point** (evidence the patterns work on real code), never as the ceiling.

A **seam** is *where a trait sits*; a **crate** is *which capability owns a model*; a **target**
(native / WASM) is *where the code may run*.

The rulebook is [CONVENTIONS.md](./CONVENTIONS.md). The shape is **three ring *roles*** — roles a
crate plays, **not a required crate list**; a project fills the roles it needs and leaves the rest
empty (full shapes in [reference/workspace.md](./reference/workspace.md) §Project shapes):

- **Kernel** — pure core: domain types, ports, engine. Target-agnostic (WASM-safe). Exactly one.
- **Capability** — a crate owning one bounded capability (a vault, an llm layer, an importer). Zero or
  more; a thin tool may fold its capability into the kernel.
- **Surface** — one outer interface that *exposes* capabilities (CLI, MCP, service, HTTP, WASM). Zero
  or more; holds no business logic.

A pure-MCP project is one surface and nothing else; an importer is a surface of kind "service"; a
library is kernel-only. The skill governs **how** a role is built, not **which** roles exist.

## When to use this

- **Adding a feature** (`/grill-with-docs`): force the placement questions before code.
- **Adding a plugin / format / language / provider**: pick the right extensibility mechanism —
  [reference/plugins.md](./reference/plugins.md); use the add-one recipes —
  [reference/extending.md](./reference/extending.md).
- **Slicing work** (`/to-prd`, `/to-issues`): each issue is one vertical slice through **one** crate
  (port → service → adapter → binding), never a horizontal layer.
- **Writing or reviewing `.rs`**: enforce the [CONVENTIONS.md](./CONVENTIONS.md) violation table.

## The placement questions

Resolve each before code exists. **Completion criterion for the whole pass: every new file is
assigned a crate, a role, and a layer; every cross-crate edge goes through a port; every leak in the
[CONVENTIONS.md](./CONVENTIONS.md) violation table is checked against the diff.**

**0. Research first (ports/conversions only).** Can't place a port until a research overview exists —
[reference/research.md](./reference/research.md). Greenfield skips to 0.5.

**0.5. Size this project.** Read the workspace; establish which **roles** exist before placing.
Don't assume a full fleet — a single library crate, or one capability + one surface, is a complete
valid shape. Questions for absent roles are no-ops. [reference/workspace.md](./reference/workspace.md)
§Project shapes.

**1. Which crate owns this?** Default to **extending**. Create a new crate only when it has its own
capability, would otherwise become a compile-wrecking dependency hub, or must be reused by ≥2 crates
or targeted independently. Split **by capability, not by layer**. New-vs-extend test +
skeleton: [reference/workspace.md](./reference/workspace.md). *Done when the owning crate is named and
the new-vs-extend decision is recorded.*

**2. How is it extended?** Pick one of the three mechanisms (feature-gate · trait registry · optional
crate) by intent — the decision table and canonical code are in
[reference/plugins.md](./reference/plugins.md). *Done when the mechanism is chosen and, if hard to
reverse, recorded as an ADR.*

**3. Does it cross a crate boundary?** Depend **inward only** (toward `core`); collaborate via a
**port** defined in the lower crate, never a sideways internal reference; a cycle is a hard error
(§Dependency direction). *Done when every cross-crate edge is a port or a `core` type.*

**4. Which layer owns each piece?** Place every artifact: **domain** (`model/`, `ports.rs`,
`error.rs`), **service** (`<feature>/`), **adapter** (`adapters/`, `providers/`), **binding** (the
surface entry — none for a library). Anatomy: [reference/slice.md](./reference/slice.md). *Done when
every artifact sits in exactly one layer.*

**5. Which target? (only if a constrained target exists.)** Skip for plain native projects. If WASM
or offline-first applies, hold the code to the constitution — [CONVENTIONS.md](./CONVENTIONS.md)
§Constitution. *Done when constrained-target code is constitution-clean, or the project has no such
target.*

## Recording decisions

Placement, mechanism, and new-crate decisions go in the **same** decision log `domain-modeling`
maintains. Write an **ADR** only at that skill's bar (hard to reverse · surprising · a real
trade-off). A new crate also belongs on `CONTEXT-MAP.md` if one exists.

## Reference

- [CONVENTIONS.md](./CONVENTIONS.md) — the rulebook: crate layout, dependency direction, the three
  mechanisms, error handling, the WASM/offline constitution, the violation table.
- [reference/workspace.md](./reference/workspace.md) — the three roles, project shapes, new-vs-extend
  test, crate skeletons.
- [reference/plugins.md](./reference/plugins.md) — the three extensibility mechanisms, with canonical
  trait + registry + feature-gate code.
- [reference/extending.md](./reference/extending.md) — the no-fuss add-a-format / add-a-language /
  add-a-provider recipes, and where `said-build` does/doesn't meet the bar.
- [reference/patterns.md](./reference/patterns.md) — the recurring enterprise patterns (audit log,
  content-address, version chain, registry dispatch, thin surface, sidecar) as generic shapes.
- [reference/slice.md](./reference/slice.md) — anatomy of one vertical slice with snippets.
- [reference/research.md](./reference/research.md) — the research step for ports/conversions.
