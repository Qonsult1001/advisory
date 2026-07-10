---
name: architecture-csharp
description: Decide where new C#/.NET code lands in the DDD bounded-context structure — which context, which of the four layers, which folder. Use when adding a feature, slicing issues, placing a module, reviewing a diff for layer leaks, or when another skill needs DDD placement decisions.
---

# Architecture

Decide **where code physically lands** in the bounded-context structure, and keep it there.
This is the placement companion to two other design skills:

| Skill | Answers | Owns |
|-------|---------|------|
| [domain-modeling](../domain-modeling/SKILL.md) | *What do we call things?* | the glossary (`CONTEXT.md`), ADRs |
| [codebase-design](../codebase-design/SKILL.md) | *How is the module shaped?* | seams, depth, leverage |
| **architecture-csharp** (this) | ***Where does it live?*** | bounded context · layer · folder |

The Rust peer is [architecture-rust](../architecture-rust/SKILL.md) — same spine, crates instead of projects.

`codebase-design` deliberately avoids the word "boundary" because it's overloaded with DDD's
bounded context. **That overloaded meaning is this skill's job.** A seam is *where an interface
sits*; a bounded context is *which business area owns a model*. Use **seam** for module shape,
**context** for ownership — don't blur them.

The rulebook is [CONVENTIONS.md](./CONVENTIONS.md); the gold-standard reference is a generic
`OrderManagement` context, shown inline in [reference/slice.md](./reference/slice.md).

## When to use this

Pull this skill in whenever placement is at stake — it's a companion, like `domain-modeling`:

- **Adding a feature** (during `/grill-with-docs`): force the placement questions *before* code.
- **Slicing work** (during `/to-prd`, `/to-issues`): each issue is a vertical slice through **one**
  context (Domain → Application → Infrastructure → Web), not a horizontal layer slice.
- **Restructuring** (during `/improve-codebase-architecture`): check deepening against the layer rules.
- **Writing or reviewing `.cs`**: enforce dependency direction, allowed folders, and the naming
  conventions. Flag any leak.

## During the session — the placement questions

Resolve each before code exists. **Completion criterion for the whole pass: every new file is
assigned a context, a layer, and an allowed folder; every cross-context edge goes through an event or
HTTP port; every leak in the [CONVENTIONS.md](./CONVENTIONS.md) violation table is checked against the
diff.**

### 0. Research the legacy first (for conversions)

When the work is converting a **legacy SQL-heavy system** (dacpac, stored procs) to clean
architecture, you **cannot place anything until a research overview exists.** This is the mandatory
first move — see [reference/research.md](./reference/research.md).

- **Analysis docs already exist?** Read them fully; they are the map — typically a *behavioural
  analysis* (entry point, dispatch, invariants) plus an *operational inventory* (tables, procs, views).
- **No docs — only a dacpac / raw SQL?** Reverse-engineer one first: find the entry point, map the
  dispatch to use cases, inventory the data model + table roles, capture invariants, trace downstream
  objects. Write it down. *Then* place.

Only once you understand the legacy behaviour and data model do the questions below have answers.

### 1. Which bounded context owns this?

New behaviour belongs to exactly one context. Decide: **extend an existing context** or **create a
new one?** Default to extending. Create a new context only when the new model has its own language,
its own invariants, and would otherwise pull unrelated concepts into an existing context. Record the
rationale (see "Recording decisions" below).

If a `CONTEXT-MAP.md` exists, place the decision against it. If this is a brand-new context, it gets
its own four projects — see [reference/placement.md](./reference/placement.md). *Done when the owning
context is named and the new-vs-extend decision is recorded.*

### 2. Does it cross a context boundary?

If the use case needs data or behaviour from **another** context, you must not reference that
context's projects. Choose the integration style:

- **React to a change** → `IEventHandler<T>` in your Application + event contract in `Common.Domain`.
- **Read at request time** → an interface in your Application (`Contracts/`) + a typed HTTP client in
  your Infrastructure (`HttpServices/`).

This is a real, hard-to-reverse decision — offer an ADR (criteria in `domain-modeling`). *Done when
every cross-context edge is an event or an HTTP port, never a project reference.*

### 3. Which layer owns each piece?

Walk the vertical slice and place every artifact in its layer. The fast rule:

- **Domain** — the model, invariants, factory, repository *interfaces*. No frameworks.
- **Application** — the use case: command/query, `*Service`, validator, response DTO, event handlers.
- **Infrastructure** — DbContext, EF configurations, repository *implementations*, HTTP clients.
- **Web** — the controller action only. No business or data logic.

Full anatomy with real snippets: [reference/slice.md](./reference/slice.md). *Done when every artifact
sits in exactly one layer.*

### 4. Which folder, exactly?

Folders are not free-form — `DDD-STRUCTURE.md` lists the **allowed** folder per layer. Use
[CONVENTIONS.md](./CONVENTIONS.md) to place each file. If a file doesn't fit an allowed folder, it's
usually in the wrong layer. *Done when every file is in an allowed folder for its layer.*

## Enforce

When writing or reviewing `.cs`, flag every leak in the [CONVENTIONS.md](./CONVENTIONS.md) violation
table (namespace declarations, layer leaks, cross-context project refs, missing `Result`/`ApiController`,
domain entities reaching `Web`).

## Recording decisions

Placement decisions live in the **same** decision log `domain-modeling` maintains — don't start a
parallel one:

- Which context owns a capability, and *why a new context was created*, when reversibility is costly →
  an **ADR** in `docs/adr/` (or the context's `docs/adr/`). Use `domain-modeling`'s ADR format.
- A new context also belongs on `CONTEXT-MAP.md`.

Only write an ADR when the decision is hard to reverse, surprising without context, and the result of
a real trade-off — same bar `domain-modeling` sets.

## Reference

- [CONVENTIONS.md](./CONVENTIONS.md) — the distilled rulebook: layers, dependency direction, allowed
  folders, naming, the violation table.
- [reference/placement.md](./reference/placement.md) — the new-vs-extend decision and the full
  four-project skeleton to create for a new context.
- [reference/slice.md](./reference/slice.md) — anatomy of one vertical slice (command + query) with
  real `OrderManagement` snippets.
