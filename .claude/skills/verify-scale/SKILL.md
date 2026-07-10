---
name: verify-scale
description: Load the real built product (CLI + MCP) with hundreds-to-thousands of memories per shipped build variant and prove it holds up as a portable memory at scale — measure recall quality (NDCG@10 / R@10, broken down @1/@5/@10) and throughput/latency, then gate against the numbers the project's own docs claim. Use when the user wants load testing, scale/stress testing, recall-quality-at-scale, deep-nuance retrieval testing, "does it still work with 1000s of memories", or to prove a build meets its documented benchmark targets.
---

# Verify Scale

Where `/verify-cli` and `/verify-mcp` prove the product is **correct** on a handful of scenarios, this
proves it stays **good at scale**: load the real shipped binary with many memories and measure whether
recall quality and speed still hold. The **gate is the project's own documented numbers** — a load run
that doesn't reproduce the claimed targets is the finding.

## What "scale" means here

Two measurement axes, both run per variant:

- **Quality at scale** — load N memories, fire a query set, score retrieval. The **gate is @10**
  (NDCG@10 for document retrieval, R@10 for conversational/recall), because that is the depth the
  project's docs actually report and block releases on. **Also report @1 and @5** as the nuance
  breakdown (where does the right answer actually land?) — but @10 is the pass/fail line, @1/@5 are
  diagnostics, never the gate.
- **Throughput / latency at scale** — ingest rate, query latency percentiles (p50/p99), and memory
  footprint as N grows. Numbers, trended against N — not pass/fail unless the docs state a latency claim.

Two engines feed these, and you run **both** (see [reference/harnesses.md](reference/harnesses.md)):

1. **The gold-standard harnesses** (`crates/sca-core/examples/` — `mteb_rust`, `locomo_baseline`,
   `realworld_recall_probe`, `baseline_recall`) produce the canonical quality numbers the docs are
   written from. Use them to establish the reference score.
2. **The real shipped product** (the CLI binary and MCP server, driven exactly as `/verify-cli` and
   `/verify-mcp` drive them) loaded at volume — to prove the *thing users actually run* reproduces those
   numbers, per variant. A harness passing while the shipped product regresses is exactly the bug class
   this skill exists to catch (e.g. an encoder that loads in the library but not on the CLI read path).

## The build-variant axis — reuse it, don't re-derive its meaning

A project ships the tool in **several compiled variants**, each with a different surface and a different
set of features compiled in — so a capability measured in one variant is **absent** in another, and a
scale run for a variant the user doesn't have is a dead end. This is the same variant axis `/verify-cli`
and `/verify-mcp` define; **discover the set the same way** (CI release matrix → build manifest →
packaging → source gates) and **never trust memory or an example list**. Then **ask the user which
variant(s) to load-test** — present what each uniquely exercises at scale (e.g. only a code-indexing
variant can be scale-tested on symbol recall over thousands of functions). Assume no default.

> ⚠️ A plain default build is usually **not** a shipped artifact — load-test the real variants the
> release pipeline ships, built with their real flags, or you are measuring something no user runs.

## The documentation is the oracle

The pass/fail line is whatever the project's own benchmark docs claim — read them this run; never assume
the threshold. The claim→gate mapping (which doc sentence becomes which numeric assertion, at @10) lives
in [reference/claims.md](reference/claims.md). Re-derive it from the live docs each run: docs drift, and
a stale threshold turns a real regression green. If a capability you loaded has **no** documented claim,
say so and report the measured number as a baseline — do not invent a passing bar.

## Workflow

### 0. Settle scope: which variants, which capabilities, what N

Discover the shipped variants from the project, then **ask the user**: which variant(s) to load-test,
which capability area(s) at scale (memory/brain recall, code-symbol recall, …), and the **load size N**
(default to a meaningful scale — hundreds-to-thousands — and the query-set size). **Completion criterion:
variant set, capability area(s), and N are all confirmed with the user this run; for each chosen variant
× capability you hold its real build command and the matching documented claim from
[reference/claims.md](reference/claims.md) (or an explicit note that none exists).**

### 1. Build the real artifact(s) and establish the reference score

Build each selected variant fresh with its real flags. Then run the **gold-standard harness** for each
chosen capability to record the canonical quality number (the score the docs were written from).
**Completion criterion: every selected variant built; for each capability the reference harness has run
and produced its quality number (e.g. `mteb_rust -- needle` → NDCG@10), captured verbatim.**

### 2. Load the real product at scale

Drive the **shipped CLI and MCP** (as `/verify-cli` / `/verify-mcp` do) in an **isolated brain/state per
variant**, ingesting N memories representative of the capability under test (synthetic is fine if it
mirrors the real shape; say so). Capture ingest throughput and the growing footprint. **Completion
criterion: N memories loaded into the real product per selected variant, on isolated state; ingest rate
and final footprint recorded; the load actually reached the target N (a truncated load is reported, not
silently shrunk).**

### 3. Measure recall quality at scale — @10 gate, @1/@5 nuance

Fire the query set at the loaded product and score retrieval. Compute **NDCG@10 / R@10** (the gate) and
**also @1 and @5** for the nuance breakdown. Do this against the **real product surface** (CLI `ask`/`query`,
MCP `search`) — not only the harness — so you measure what ships. **Completion criterion: for each
variant × capability, @1/@5/@10 are computed from the real product's responses over the full query set;
the @10 figure is stated next to its documented target.**

### 4. Measure throughput / latency at scale

Record query latency percentiles (p50/p99) and ingest throughput at N, and — where cheap — at a second
N to show the trend with scale. **Completion criterion: p50/p99 query latency and ingest throughput are
recorded at the tested N (and trended against a second N where feasible); any documented latency claim is
stated next to the measured value.**

### 5. Judge against the documented claim, and report

Compare measured **@10** to the documented target per capability: within the doc's stated tolerance =
pass; below = fail. Throughput/latency: pass only against an explicit documented claim, else report as
baseline. Produce **one report per variant** — a quality table (capability · N · @1 · @5 · **@10** ·
doc-target · pass/fail) and a perf table (capability · N · ingest/s · p50 · p99) — plus a cross-variant
summary and a harness-vs-product line (did the shipped product reproduce the harness number?). File each
**fail** to `/qa`'s issue standard (its template, single-vs-breakdown decision, domain language — that
standard lives in `/qa`, don't restate it), **naming the variant and N** in the title so it reproduces,
or hand off to `/qa`. **Completion criterion: a per-variant quality + perf report exists; every @10 fail
is filed (variant + N named) or handed off; any harness-passed-but-product-failed gap is called out
explicitly — never left only in chat.**

## Checklist per run

```
[ ] Variants + capabilities + N confirmed with the user; build flags from the project this run
[ ] Reference harness number captured per capability (the doc's source-of-truth score)
[ ] N memories loaded into the REAL CLI/MCP per variant, isolated state, target N actually reached
[ ] @10 computed from the real product (gate); @1/@5 reported as nuance; stated beside the doc target
[ ] p50/p99 + ingest throughput recorded; latency judged only against an explicit doc claim
[ ] Per-variant quality + perf tables; harness-vs-product gap called out; every @10 fail filed
```

## Boundaries

It **measures and reports at scale** — it does not fix (fails become issues; fixing is `/implement` or
`/tdd`). It is **not** `/verify-cli` or `/verify-mcp` (those prove a few scenarios are *correct*; this
proves quality and speed *hold at volume*), and **not** `/verify-docs` (that proves docs match code
prose; this proves the build meets the docs' *numbers*). A system exposing the same memory over CLI and
MCP should be scale-checked on **both** — they share a core but differ in dispatch and session state, so
one can regress at scale while the other holds.
