# Research overview — the first step for ports/conversions

A conversion into the workspace **always starts from a research overview** of the source system. You
cannot place code into crates/roles until you understand what the source does and what data/contracts
it touches. **Greenfield work skips this** — go straight to sizing the project (SKILL.md §0.5).

Two starting points:

1. **Analysis docs already exist** → read them, don't re-derive. Treat them as the map.
2. **No docs — only source / a binary protocol / a schema** → reverse-engineer one first.

## When analysis docs exist (preferred)

If the source already has a research write-up, that *is* the research step. Read it fully and extract
the placement inputs. From any such doc set, pull out:

| Extract | Used for |
|---------|----------|
| Entry point(s) and how requests are dispatched | identifying use cases (one per handler/command) |
| The data model / contracts + each item's role | which crate/role owns which data |
| Business rules / invariants | the aggregate's behaviour, in the kernel |
| Cross-cutting concerns (auth, audit, config) | kernel vs a dedicated capability crate |
| Downstream / external reach | which surface exposes it; which calls cross a process boundary |
| **Target/runtime constraints** | does any path need WASM / offline / no-std? (drives role placement early) |

## When there are NO docs — reverse-engineer first

If all you have is source, a wire protocol, or a schema, produce a research overview *before* placing
anything:

1. **Find the entry point(s).** What external callers invoke — a `main`, an RPC surface, a queue
   consumer, a C ABI. Note how it routes (a command enum, a big `match`, a handler table).
2. **Map the dispatch.** List each branch/handler and the operation it performs. Each is a candidate
   **use case** (→ a vertical slice later).
3. **Inventory the data model / contracts.** What each type/table/message *means* (identity? ledger?
   lookup? config?). Group by role — raw material for capability crates and aggregates.
4. **Capture invariants & nuances.** The rules that must survive the port — they become aggregate
   behaviour in the kernel, not scattered `if`s.
5. **Trace downstream + external reach.** Network calls, files, subprocesses, native libs. Each is a
   port + adapter, and a **target-safety flag** (anything native can't sit on a WASM path).
6. **Write it down** as a behavioural-analysis doc (+ an inventory if the surface is large), in the
   source area's `docs/`. This doc is the deliverable of the research step.

Keep the research doc free of *target* design — it describes the **source as-is**, not the clean
plan. Placement decisions come next, recorded separately (ADRs / `CONTEXT.md`).

## From research → placement

Once the overview exists, hand off to the rest of this skill and the workflow:

1. **domain-modeling** — turn the source's term/type soup into a clean glossary (`CONTEXT.md`). The
   legacy names are *not* the ubiquitous language; name the concepts.
2. **architecture-rust / placement** — size the project (SKILL.md §0.5), then group the use cases into
   **capability crates**, decide extend-vs-new, and assign each source item to a crate/role. See
   [workspace.md](./workspace.md).
3. **to-issues** — slice each capability as a vertical slice (port → service → adapter → binding) in
   one crate. See [slice.md](./slice.md).
4. **implement / tdd** — port behaviour out of the source into the aggregate + service, proving parity
   against the source where data parity matters.

## Parity note

Where the source is the system of record, the port must be provable against real source data, not
just synthetic. Build the slice so a parity harness can compare its output to the source's at the
record level. Record any deliberate divergence as an ADR.
