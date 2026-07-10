---
name: verify-docs
description: Audit a project's documentation against its real code — prove every documented claim is true by tracing it to source and executing the testable ones, then on any mismatch grill to establish true intent and fix the code end-to-end to production-ready. Use when the user wants to verify docs match code, check documentation is correct, find stale/wrong docs, or make docs and code agree.
---

# Verify Docs

Treat every documented claim as an **unproven assertion** until evidence says otherwise. Documentation
makes promises — "call this, get that"; "this flag does X"; "the response looks like this" — and over
time code drifts from those promises. This skill hunts every claim, **proves or refutes it against the
real code**, and on a mismatch does not silently patch: it establishes the **true intent** by grilling,
then **fixes the code end-to-end** to that intent (correcting the doc only if the doc was the wrong
side). The output is documentation and code that **agree, with evidence** — enterprise/production-ready.

**Neither side is assumed right.** A mismatch is not "the doc is stale" *or* "the code is buggy" until
the grill decides. Jumping to either conclusion is the failure this skill exists to prevent.

## Workflow

### 1. Inventory every documented claim

Sweep all documentation in scope — READMEs, doc comments (`///`, docstrings, JSDoc), API docs, guides,
the user manual, inline `// docs say…` notes — and extract every **checkable claim**: a behaviour, a
signature, a flag/parameter, a sample, a stated output, an endpoint, a config default. Each claim gets:
its source location, what it asserts, and whether it's **statically traceable**, **executable**, or
both. Kick off a **background Explore agent** while you sweep, to map the codebase's domain language
(check `UBIQUITOUS_LANGUAGE.md`) and behaviour boundaries — context that sharpens both the trace and
any issue you file. **Completion criterion: a claim inventory exists covering every doc in scope; each
claim has a source, an assertion, and a verification kind. A doc you skipped is named, not silently
dropped.**

### 2. Trace every claim to source (the cheap check)

For each claim, find the code that implements it and confirm they agree **statically**: signature,
types, parameter names/defaults, control flow, the shape of a documented response. This catches
structural drift (a renamed flag, a changed default, a removed method) without running anything.
**Completion criterion: every claim is marked traced-consistent or traced-DRIFT against real source,
with the implementing location cited; a claim with no implementing code is flagged as orphaned.**

### 3. Execute every testable claim (the proof)

For each claim that *can* be run, **run it** and assert the real result against the documented one:
execute doc code samples; invoke documented commands and check stated output (recruit `/verify-cli`);
call documented endpoints/tools and check the response (recruit `/verify-mcp`); exercise documented
behaviours as tests (recruit `/tdd`). A claim is **correct only with green evidence**, never with a
read. **Completion criterion: every executable claim has been run with a captured actual result and
marked pass/fail against its documented claim; nothing testable is marked "correct" without execution.**

### 4. Triage findings — verified vs mismatch

Produce the verdict table: each claim · source · trace result · execution result · **verified / drift /
broken / orphaned / undocumented-behaviour**. A claim is **verified** only if trace *and* (where
applicable) execution agree. Everything else is a **mismatch** for stage 5. **Completion criterion:
every claim has a final verdict; the mismatch list is complete and each entry says which axis failed
(structural, behavioural, or missing).**

### 5. For each mismatch — grill to true intent, then fix code end-to-end

Do **not** assume the doc or the code is right. For each mismatch (or a coherent cluster), **hand off
to the user to run `/grill-with-docs`** — a relentless interview that establishes what the behaviour
*should* be and records it (ADR/glossary). Pause here; this is a real decision the user owns. With the
intent settled:

- if the **code** was wrong → fix it end-to-end to the verified intent (recruit `/tdd` red→green;
  `/diagnosing-bugs` if the fault is hard), to **production/enterprise quality** — not a patch.
- if the **doc** was wrong → correct the doc to the now-true behaviour.
- re-run stages 2–3 on the touched claim until it is **verified with green evidence**.

**Completion criterion: every mismatch has a recorded intent decision (from the grill), a fix applied
to the correct side, and a re-verification that now passes; no mismatch is closed by assertion or by
editing the doc to hide a real code defect.**

### 6. Final sweep — agreement, with evidence

Re-run the inventory pass: confirm every claim is now **verified**, the code changes hold under the
existing test suite (no regression — recruit `/review`), and the doc↔code set agrees end to end. Any
mismatch deliberately **deferred** (out of scope, needs a bigger fix) is filed to **`/qa`'s issue
standard**, not dropped. **Completion criterion: the verdict table is all-verified or has every
exception filed to that standard; the full test suite passes; a summary reports what was drift vs
broken vs orphaned and what was fixed — evidence, not claims.**

## The grill is a hand-off, not a side-call

`/grill-with-docs` is **user-invoked** — this skill cannot fire it. At stage 5 you **stop and ask the
user to run it**, then resume with the decision. Never resolve a mismatch by guessing the intent to
avoid the hand-off; the whole point is that intent is decided, not assumed.

## What this is not

- Not a doc *linter* (spelling, links, formatting) — it checks **truth**, not style.
- Not doc generation (`document-user-manual` / `sdk-factory` *write* docs) — this **audits existing
  docs against code** and fixes the divergence.
- Not "make the docs match the code" — that's only the resolution when the grill rules the doc wrong;
  when the code is wrong, the **code** is fixed to the documented/intended behaviour.
