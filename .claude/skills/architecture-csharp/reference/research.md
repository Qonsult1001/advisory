# Research overview — the mandatory first step

A clean-architecture conversion **always starts from a research overview** of the legacy system.
You cannot place code into bounded contexts until you understand what the legacy does and what data
it touches. This step produces (or consumes) that understanding.

Two starting points:

1. **Analysis docs already exist** → read them, don't re-derive. Treat them as the map.
2. **No docs — only a dacpac or raw SQL** → reverse-engineer one yourself before placing anything.

## When analysis docs exist (preferred)

If the legacy area already has a research write-up, that *is* the research step. Read it fully and
extract the placement inputs from it. Could be one doc or several.

The strong shape is **two docs** (one named `*_Processing_Analysis.md` + `Required_for_Operations.md`
in the African Bank port, for instance):

- a **behavioural analysis** — executive summary, entry point, data model, table roles, the
  dispatch/handler map, business nuances.
- an **operational inventory** — every table, proc, view, function, trigger required, grouped by
  operational role.

Together they give you everything placement needs. From any such doc set, pull out:

| Extract | Used for |
|---------|----------|
| Entry point(s) and how requests are dispatched | identifying use cases (one per handler/request type) |
| The data model + each table's role | which aggregate/context owns which data |
| Business nuances / invariants | the aggregate's behaviour and rules |
| Cross-cutting concerns (fees, audit, access control) | shared kernel vs separate context |
| Downstream object graph | cross-context integration (events vs HTTP ports) |

## When there are NO docs — reverse-engineer from the dacpac / raw SQL

If all you have is a `.sqlproj` / dacpac / loose `.sql`, produce a research overview *first*. Mirror
the two-doc shape above. Steps:

1. **Find the entry point(s).** The proc(s) external callers invoke (a dispatcher, an API-facing
   proc, a queue trigger). Note how it routes (a `TypeOfRequest`-style key, a big `IF/CASE`, a
   handler table).
2. **Map the dispatch.** List each branch/handler and the business operation it performs. Each one is
   a candidate **use case**.
3. **Inventory the data model.** List the tables each path reads/writes and what each table *means*
   (identity? account master? ledger? lookup? audit?). Group tables by role — this is the raw
   material for bounded contexts and aggregates.
4. **Capture invariants & nuances.** Triple-account-on-open, fee splits, access checks, idempotency —
   the rules that must survive the port. These become aggregate behaviour, not SQL.
5. **Trace downstream objects.** Functions, views, child procs, triggers each path depends on. Cross-
   system reach → cross-context integration.
6. **Write it down** as a behavioural-analysis doc (+ an operational inventory if the surface is
   large), in the legacy area's `docs/` or alongside the SQL. This doc is the deliverable of the
   research step and the input to everything after.

Keep the research doc free of *target* design — it describes the **legacy as-is**, not the clean-arch
plan. Placement decisions come next, recorded separately (ADRs / `CONTEXT.md`).

## From research → placement

Once the overview exists, hand off to the rest of this skill and the workflow:

1. **domain-modeling** — turn the legacy table/term soup into a clean glossary (`CONTEXT.md`). The
   legacy names (`cpf_`, `ana_`, `erl_`) are *not* the ubiquitous language; name the concepts.
2. **architecture / placement** — group the use cases into bounded contexts; for each, decide
   extend-vs-new and which legacy tables map to which aggregate. See
   [placement.md](./placement.md).
3. **to-issues** — slice each context as a vertical slice (Domain→Application→Infrastructure→Web).
   See [slice.md](./slice.md).
4. **implement / tdd** — port behaviour out of SQL into the aggregate + services, proving parity
   against the legacy where data parity matters.

## Parity note

Where the legacy is the system of record (banking, ledgers), the port must be provable against real
legacy data, not just synthetic. Build the clean-arch slice so a parity harness can compare its
output to the legacy proc's output at the cent/row level. Record any deliberate divergence (e.g.
fixing a legacy double-bill) as an ADR.
