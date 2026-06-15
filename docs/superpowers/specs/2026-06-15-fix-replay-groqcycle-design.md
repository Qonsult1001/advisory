# Operator-approved auto-merge + clean — Design

**Date:** 2026-06-15 · **Target:** `EvolutionService.cs`, `GroqCycle.cs`, `Controllers.cs` · **said:** 0.7.0 (stable, baked)

## Goal

Turn the manual end-of-cycle (`gh pr merge --squash --delete-branch` + close issue)
into **code**, triggered by an explicit operator decision — keeping release
operator-only (per the mutation-release-workflow memory) but removing the manual
shell step.

## The new checkpoint

The cycle already parks once for plan approval (`awaiting-approval`). This adds a
SECOND park at `pr-open` (reached only after build+test pass = "100% complete"):

```
pr-open  →  operator POSTs decision="merge"  →  CODE: gh pr merge --squash --delete-branch
                                                     → verify issue closed (Closes #N) else gh issue close
                                                     → status = "released"
         →  (no decision)                     →  stays pr-open (today's behaviour)
```

## Implementation (3 isolated pieces)

1. **`GroqCycle.MergeAndCleanAsync(prUrl, ticket, ct)`** — new method:
   - `gh pr merge <pr> --repo <Repo> --squash --delete-branch`
   - verify the issue auto-closed (squash-merge honours `Closes #N`); fallback
     `gh issue close <ticket>` if still open.
   - returns `(bool ok, string detail)`.

2. **`EvolutionService.Decide` gains `decision=="merge"`** — valid ONLY when
   `r.Status == "pr-open"`:
   - calls `MergeAndCleanAsync`; on success → `Status="released"`, append
     `[merge] APPROVED by operator → squash-merged, branch deleted, #N closed`.
   - on FAILURE → **stay at `pr-open`**, append the error (retry/manual-merge
     stays possible). A transient failure never marks a green PR failed.
   - `merge` on a non-`pr-open` run → no-op with a clear message.

3. **Endpoint: reuse existing `POST /run/{id}/decision`** — no new route. The
   controller already forwards the decision string; `Decide` routes `"merge"` to
   the merge path. Because `Decide` is sync and merge is async, add an async
   `DecideMergeAsync(id, ct)` that the controller calls when `decision=="merge"`.

## Safety properties (preserved)

- **Release stays operator-only** — the cycle NEVER auto-merges; a human must POST
  `decision="merge"`. We replace manual `gh` with code behind that decision.
- **Only green PRs mergeable** — `merge` rejected unless `Status=="pr-open"`,
  reached only after build+test pass.
- **Plan approval unchanged** — purely additive; first checkpoint untouched.
- **Failure-safe** — merge failure leaves the PR open, never marks it failed.

## Acceptance behaviour

- Green cycle → `pr-open`; operator POSTs `decision="merge"` → PR squash-merged,
  branch deleted, issue closed, status `released`.
- Merge fails (branch protection / conflict / checks pending) → stays `pr-open`,
  error logged, operator can retry or merge by hand.
- `merge` on an `awaiting-approval` or `failed` run → rejected, no effect.

---

# (Deferred) Fix-Replay wired into GroqCycle — Design

> **BLOCKED — do not implement.** Empirically tested on said 0.9.0: `record-fix`
> writes a case but `suggest-fix`/`grep`/`query` never retrieve incrementally-added
> frames (the write succeeds, `stats` counts it, `get` returns it by id, but search
> finds nothing — even exact text at `--min-similarity 0.0`). Reproduced on a fresh
> uncorrupted brain. Until the .said incremental-indexing bug is fixed, wiring this
> in would add dead code. Held for when a fixed binary lands. Original design below.

**Date:** 2026-06-15 · **Target:** `src/Advisory.Api/Agents/GroqCycle.cs` · **said:** 0.9.0 (baked, live)

## Goal

Eliminate the first-attempt build/test failure for *recurring* ticket shapes by
replaying a known-good change-set with **no LLM call**, using said 0.9.0's
`suggest-fix` / `record-fix`. For novel tickets, behaviour is unchanged.

## The two integration points (both in GroqCycle.cs)

### 1. `suggest-fix` — before Groq, inside `ProduceChangeAsync`

On the **first** attempt only (not repairs), before calling the LLM:

```
said suggest-fix --path <brain> --ticket "<title>\n\n<body>" --min-similarity 0.85 --json
  ├─ match.edits present → build a ChangeSet from the returned edits, return it (NO LLM call)
  └─ match: null         → existing path: Groq emits the change-set
```

- **Fingerprint key:** `title + "\n\n" + body` (full ticket). Matches what
  `record-fix` stores, so a paraphrased recurrence still matches.
- **Threshold:** 0.85 (the CLI default). Tune from real hit/miss outcomes later.
- **Repairs never use replay** — if attempt 1 was a replay and it failed the
  gate, attempts 2+ go to Groq. This prevents looping on a stale case.

### 2. `record-fix` — after the green gate, inside `ImplementAndPrAsync`

Immediately after `dotnet build` **and** `dotnet test` both pass (the existing
gate), before/around opening the PR:

```
said record-fix --path <brain> --ticket "<title>\n\n<body>" --edits-file <changeset.json> --label "#<pr>"
```

- Recorded **only** on green — the gate stays the sole ground truth.
- Best-effort: a record-fix failure never blocks the PR (wrap in try/catch, log).
- `--edits-file` carries the exact change-set that just built+passed (the
  `edits` array serialized to JSON).

## Load-bearing safety properties (unchanged behaviour)

1. **A replayed change-set still passes through the full build/test gate.**
   `suggest-fix` skips the *Groq call*, NOT the verification. Replayed edits flow
   into the same `said edit → dotnet build → dotnet test` path. A stale replay
   that no longer compiles **fails the gate and falls back to Groq** — it can
   never reach a PR. Worst case = one wasted ~30s build, i.e. today's
   first-attempt cost, never worse.
2. **Operator approval preserved.** The plan-approval checkpoint is untouched. On
   a replay, the plan/log notes "replaying known-good fix (similarity X) — no LLM".
3. **Replay-failed → Groq fallback, no loop on the stale case** (replay only on
   attempt 1).
4. The build/test gate, `said edit` surgical application, and the self-repair
   loop are all untouched.

## Plumbing details

- `ProduceChangeAsync` needs `title`+`body` to build the fingerprint — it already
  receives both. The fingerprint string is `$"{title}\n\n{body}"`.
- `ImplementAndPrAsync` currently takes `(ticket, title, change)`. It needs the
  `body` too, to record the same fingerprint key. Thread `body` through (it's
  available at the call site in `ImplementWithRepairAsync`).
- Add two small helpers mirroring `SaidRecall`: `SaidSuggestFix(ticketText)` →
  parsed `ChangeSet?`, and `SaidRecordFix(ticketText, edits, label)` →
  best-effort void.
- A `record-fix` change-set is serialized from the `ChangeSet.edits` to a temp
  file (same temp-file pattern as the per-edit content files), passed via
  `--edits-file`, then deleted.

## What to KEEP (these serve DISTINCT purposes — do not conflate)

- **KEEP `SaidRemember`** (lines 42-49) and its call sites. It is **NOT**
  superseded by `record-fix` — the two feed different paths:
  - `SaidRemember` → `said add` stores **free-text prose context** ("what we did
    and why"), which the **Groq planning path** recalls via `SaidRecall`/`ask`
    when handling a *novel* ticket replay can't match. It is searchable narrative
    memory.
  - `record-fix` stores a **structured, fingerprinted, replayable change-set**
    for the **replay path** (re-apply a known edit, no LLM).
  A replayable case is not a substitute for searchable prose: replay only fires
  on recurring shapes; prose context still helps Groq on everything else. Keep
  both.
- **Keep `RealEndpointAnchors`** — verified that said 0.9.0 still has no
  `anchors` command and `grep app.MapGet` is still noisy, so the source-read
  workaround is still required for the *Groq* path. (Replay path doesn't need it —
  it copies the stored edits verbatim.)
- **Keep `SaidRecall` / `SaidExplain`** — still used by the Groq fallback path.

## Acceptance behaviour

- **Cold brain (no cases):** first ticket → `suggest-fix` returns null → Groq path
  (today's behaviour) → green → `record-fix` stores the case. Identical to now,
  plus one recorded case.
- **Warm brain (matching case):** a similar ticket → `suggest-fix` returns the
  stored edits → ChangeSet built with **zero Groq calls** → gate passes →
  PR opens with no first-attempt failure.
- **Stale case:** a stored case whose anchor no longer resolves → replay fails the
  gate → falls back to Groq on attempt 2 → still converges. No broken PR.
