# Workflow map — which skill to run, when

A guide to the engineering skills in this repo: which **`[USER]`** skill you type to start each kind
of work, and which **`[auto]`** skills fire themselves underneath. **You only ever run `[USER]`
skills.** The `[auto]` ones — `architecture-rust` / `architecture-csharp`, `domain-modeling`,
`codebase-design`, `diagnosing-bugs`, `review`, `tdd` — are recruited automatically when placement,
language, module-shape, a bug, or a diff comes up.

> First-time setup: run **`/setup-matt-pocock-skills`** once to configure the issue tracker, triage
> labels, and domain-doc layout. Not sure which skill fits? **`/ask-matt`** routes you.

---

## The conversion workflow (SQL-heavy → clean architecture)

Run these as slash commands in order. `architecture-*`, `domain-modeling`, and `codebase-design`
auto-activate during them when placement / language / module-shape comes up.

```
/grill-with-docs      # interview + build glossary/ADRs from the legacy area
/to-prd               # synthesise the discussion into a PRD
/to-issues            # slice the PRD into per-bounded-context vertical slices
/implement            # build each slice
/tdd                  # red-green-refactor while implementing
```

In a **Rust** workspace `architecture-rust` auto-fires (crates/layers/WASM); in a **C#/.NET** one
`architecture-csharp` (bounded-context/four-projects). Same pipeline, the right architecture skill
underneath.

---

## 1. End-to-end production testing — CLI and MCP

The conversion pipeline's `/tdd` does unit/integration tests **in-process**; it does not run the
shipped artifact. These skills fill that gap — they drive the **real built binary / server** and
judge it on observable behaviour. `/verify-cli` and `/verify-mcp` prove it's **correct** on a handful
of scenarios; `/verify-scale` proves it stays **good at volume**.

| Run | Why |
|-----|-----|
| **`/verify-cli`** | Drive the **real built CLI binary** end-to-end. **Tests each shipped build variant separately** (feature bundle / target / profile) — it discovers the variant set from the project (CI matrix / build manifest), asks if it can't, then builds each, runs real commands per variant in an isolated dir, and asserts exit code + stdout + side effects. The dedicated production-CLI E2E entry point. |
| **`/verify-mcp`** | Drive the **real built MCP server** end-to-end, **per build variant**: discover variants, start each, `tools/list`, call tools with real args over the transport, assert JSON-RPC result + error shape + state-across-calls. The dedicated production-MCP E2E entry point. |
| **`/verify-scale`** | Load the **real product (CLI + MCP) at volume** — hundreds-to-thousands of memories **per build variant** — and prove it holds up as a portable memory: measures recall **quality** (NDCG@10 / R@10, broken down @1/@5/@10) and **throughput/latency**, then **gates against the numbers the project's own docs claim** (the docs are the oracle). The scale/quality axis of E2E — where `/verify-cli`/`/verify-mcp` ask "is it correct?", this asks "does it still work with 1000s of entries?". |
| `/prototype` | Build a throwaway runnable terminal app to exercise behaviour when there isn't a shipped binary yet. |
| `/qa` | Conversational pass: you run it, report what breaks, it explores the code and files issues. `/verify-cli`, `/verify-mcp`, and `/verify-scale` hand their failures here — and file to **`/qa`'s issue standard** (§5). |
| `/tdd` | The automated regression net (in-process integration tests), not manual E2E. |

> A system exposing the same capabilities over **both** CLI and MCP should be verified on **both** —
> they share a core but differ in dispatch, schema, and (MCP) session state.
>
> **Build variants matter.** A project that ships its binary in several variants (e.g. minimal vs
> full feature bundles) has a *different command/tool surface per variant* — `verify-cli`/`verify-mcp`
> test each one against its own scenario list (a command present in the wrong variant is a bug), and
> `verify-scale` load-tests each variant against the recall/perf claims for the features it ships. A
> single-binary project is just the trivial one-variant case.
>
> **Correct vs good-at-scale.** `verify-cli`/`verify-mcp` answer "does the command/tool do the right
> thing?"; `verify-scale` answers "does retrieval quality and speed still hold at 1000s of entries,
> per variant, against the documented targets?" Run scale after correctness — there's no point
> load-testing a surface that's wrong on one example.

## 2. Documentation & SDKs — produce world-class docs / client libraries

These *generate* documentation and SDKs (distinct from `/verify-docs` in §3, which *audits* them).

| Run | Why |
|-----|-----|
| **`/document-user-manual`** | The **reference** (Diátaxis): a **production-grade user manual** for a CLI and/or MCP server — separate manuals, **lifecycle-first** (create → import → … → harden), **every** command/tool/flag/config catalogued, enterprise sections (encryption, audit, retention, deploy), troubleshooting + quick-reference. You **look things up** in it. |
| **`/document-walkthrough`** | The **tutorial + how-to** (Diátaxis): **task-first**, not a catalogue — a hand-held **tutorial** (zero → first success, choice-free, "a cleaner could follow it") plus **how-to guides** (one per real goal: "how to import a folder", "how to connect MCP to my editor"). You **follow it** to get something done. Links to the manual for the long tail. |
| **`/sdk-factory`** | Scan a system and generate a **world-class, fully-documented SDK** — a typed client (any target language: C#/Kotlin/Swift/TS) with one-call registration + auth/correlation/retry threaded uniformly, **plus line-by-line documentation a non-technical person can follow** (the non-negotiable half). Produces a Consumer Guide *and* a Developer Guide, each at the right reader level. |

> **Manual vs walkthrough** — the most common doc mistake is conflating them. The **manual** documents
> *the system* (exhaustive reference, jump-to); the **walkthrough** documents *the user's task*
> (selective journey, follow-along). Run both for the full picture — they complement, the walkthrough
> links into the manual.
>
> All three discover the **real surface first**. The two doc skills also **scope on two axes — they ask
> you**: which **build variant** (when a system ships several compiled bundles/targets), *and* whether
> to cover the **whole system or just one capability area** (e.g. "only the brain/memory side, not
> code-indexing"). So you can document just a section, not only a whole binary. They produce docs/SDKs;
> `/verify-docs` (next) checks docs are *true*.

## 3. Verify documentation is correct, and fix bugs end-to-end

| Run | Why |
|-----|-----|
| **`/verify-docs`** | Audit a project's docs against its **real code**: inventory every documented claim, **trace** each to source (cheap structural check) and **execute** the testable ones (recruits `/verify-cli`, `/verify-mcp`, `/tdd`) — a claim is correct only with **green evidence**. On any mismatch it does **not** guess: it hands off to **`/grill-with-docs`** to settle true intent, then **fixes the code end-to-end** to production quality (or corrects the doc if the doc was wrong), and re-verifies. |

**Fixing bugs.** There is no separate "fix a bug" entry skill — bug-fixing is **built into the flow**:

- The **`diagnosing-bugs`** skill is `[auto]` — it fires itself the moment you say "diagnose / debug
  this" or report something broken/throwing/failing/slow. You don't run it; it runs the diagnosis loop
  underneath whatever you're doing.
- `/verify-cli`, `/verify-mcp`, `/verify-docs` **find** bugs and **file them to `/qa`'s issue
  standard** (durable, behaviour-not-code, no file paths, domain language). Fixing the filed bug is
  then `/implement` or `/tdd` (with `diagnosing-bugs` auto-firing if the fault is hard).
- **`/qa`** is the conversational way in: describe the bug, it explores the code and files the issue.
  Its template + single-issue-vs-breakdown rules **are** the project's issue-filing standard that the
  verify-* skills file to — one home, no drift.

So the bug loop is: **find** (a verify skill / qa / you notice) → **file** (qa's standard) →
**diagnose** (diagnosing-bugs, auto) → **fix** (`/implement` + `/tdd`, red→green) → **re-verify**.

## 4. Investigate new features

| Run | Why |
|-----|-----|
| **`/decision-mapping`** | "Turn a loose idea into a sequenced map of investigation tickets, then drive them one at a time." The feature-investigation entry point — for a fuzzy new idea. |
| `/grill-me` | Once you have a rough plan, relentlessly stress-test it before building. |
| then `/to-prd` → `/to-issues` → `/implement` | Once investigation converges, the same pipeline carries it to code. |

`architecture-*` + `domain-modeling` + `codebase-design` auto-fire throughout.

## 5. Refactor

| Run | Why |
|-----|-----|
| **`/request-refactor-plan`** | "Create a detailed refactor plan with tiny commits via user interview, then file it as an issue." The refactor entry point — turns a refactor into safe incremental issues. |
| `/improve-codebase-architecture` | Broader: scan for deepening opportunities, present an HTML report, grill whichever you pick. Use when you want it to **find** what to refactor. |
| `/review` | After refactoring, check the diff against standards + spec before merging. |

## 6. Assistance on design

| Run | Why |
|-----|-----|
| **`/design-an-interface`** | Generate multiple radically different interface designs for a module via parallel sub-agents. When designing an API/module shape. |
| `/improve-codebase-architecture` | When the question is "how do I make **this existing module** deeper/better." |
| `/grill-me` | Stress-test any design before committing. |
| `/prototype` | When the design needs to be **felt** — build throwaway variants to compare. |

`codebase-design` (deep-module vocabulary) auto-fires across all of these.

---

## The mental model — pick the entry point

| I want to… | Type |
|------------|------|
| convert AB → clean arch | the 5-skill pipeline (`/grill-with-docs` …) |
| **test the CLI end-to-end** (per build variant) | **`/verify-cli`** (+ `/qa` for the conversational pass) |
| **test the MCP server end-to-end** (per build variant) | **`/verify-mcp`** (+ `/qa`) |
| **load-test at scale** (1000s of entries, recall quality + perf vs documented targets, per variant) | **`/verify-scale`** (+ `/qa`) |
| **write a production reference manual** (every command/tool) | **`/document-user-manual`** |
| **write step-by-step how-to-use docs** (tutorial + how-tos, easy for anyone) | **`/document-walkthrough`** |
| **generate a documented SDK** for a system | **`/sdk-factory`** |
| **check the docs are actually true** + fix the code | **`/verify-docs`** (grills, then fixes end-to-end) |
| **report / fix a bug** | **`/qa`** to file it → `/implement` + `/tdd` to fix (`diagnosing-bugs` auto-fires) |
| investigate a new feature | `/decision-mapping` → (`/grill-me`) → `/to-prd` → `/to-issues` → `/implement` |
| refactor | `/request-refactor-plan` (or `/improve-codebase-architecture` to find targets) |
| get design help | `/design-an-interface`, `/improve-codebase-architecture`, `/grill-me`, `/prototype` |
| not sure which | **`/ask-matt`** |

Also handy as you go: **`/handoff`** (compact a long session for another agent), **`/triage`** (move
filed issues through their states), **`/review`** (before any merge), **`/setup-matt-pocock-skills`**
(one-time repo setup).
