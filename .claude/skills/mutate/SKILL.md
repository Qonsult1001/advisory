---
name: mutate
description: Run one mutation cycle — fix a bug or broken code from a `mutation`-labelled GitHub ticket, with a test, and open a PR for review. PR-only; never edits main. Use when asked to /mutate or to run the bug-fix loop.
allowed-tools: Read, Edit, Write, Bash, Glob, Grep
---

# Mutation Cycle (Advisory · PR-only)

Run one **mutation** cycle on this repository — the hands-on loop that fixes bugs and broken code.
You are the LLM; `scripts/mutate-ide.sh` handles infrastructure (GitHub, git, build, tests, PR).
Execute every step in order. **Do not stop, do not ask for permission, do not summarize between steps.**

> **Primary aim: fix bugs and broken code.** Mutation addresses the concrete defects and gaps that
> testers file as `mutation`-labelled tickets — not features, not research. (The forward-looking
> research that studies the security landscape and logs work to the backlog is a *separate* task:
> the `evolve` skill. Mutation changes code; evolution only updates the backlog.)
>
> Based on an MIT-licensed evolution harness (see NOTICE). This repo is a .NET 10 + React codebase
> (Advisory), so build/test use `dotnet` and `vite`, and the cycle is **PR-only**: changes go to a
> branch and open a pull request for human review — never to `main`.

## Step 1: Setup

```
./scripts/mutate-ide.sh setup
```

This verifies the build, fetches open GitHub issues labelled `mutation` (with their tester comments),
and writes `.evolve/ISSUES_TODAY.md` and `.evolve/plan_prompt.md`. If there are **no** labelled
issues and no pending tester replies, it prints `NO WORK` — in that case, stop here and report
"nothing to mutate".

## Step 2: Remember who you are, then understand the task

Continuity first — read these so you act with memory, not from scratch:
- `IDENTITY.md` — who you are and what you will/won't do. **Honor it.**
- `PERSONALITY.md` — your voice (you'll write a journal entry later).
- Last 3 entries of `JOURNAL.md` — have you tried this before? What did you learn?
- `memory/active_learnings.md` — distilled lessons. Don't repeat past mistakes.
- `RESEARCH.md` — your open questions about this codebase.

Then the task:
- Read `.evolve/ISSUES_TODAY.md` (the tickets + tester comments to address).
- Read `CLAUDE.md` and the relevant source under `src/Advisory.Api/` and `web/src/`.
- **Project memory (RECALL — saves tokens, gives full-codebase awareness):** the `said` MCP server
  is the project brain (`.mcp.json`). **Recall before reading blindly — pick the right tool:**
  - **Know the name?** Use `said.sym("<ClassOrMethod>")` for the exact symbol (returns the file:line
    range), then `said.get(<doc_id>)` for the **complete current code body** to edit. This is exact —
    use it for a named class/method/endpoint.
  - **Know an exact string?** Use `said.grep("<text>")` (e.g. an endpoint path `api/health`, a route,
    an error message). Exact substring search — best for pinpointing a specific site.
  - **Only a concept?** Use `said.ask("<what you need>")` — fused semantic recall + past learnings.
    Great for "where do we X"; for a *specific* named thing prefer sym/grep (semantic can be fuzzy).
  Always `said.get` the doc_id before editing so you have the real, current body (not a stale guess).
  Only open files the brain points you to. (CLI fallback: `./tools/said/said.exe sym|grep|ask|get …`.)
  If neither the MCP nor `Advisory.said` is available, read `PROJECT_CONTEXT.md` or the source directly.

Note the specific bug/gap each ticket describes. Do not invent work beyond the tickets, and never
weaken a security control to make a test pass (see IDENTITY.md).

## Step 2b: Agent routing (if configured)

If `.evolve/routing.json` exists, the operator has assigned specific AI agents to phases
(research / planning / execution / documentation) and a run **mode** (`sequential` or `parallel`).
Each routed agent is a Task-tool subagent. **Every delegated subagent prompt MUST include, in order:**

1. **Persona** — the routed agent's `persona` from `routing.json` (its personality + strict
   instructions), verbatim at the top. This is how each agent keeps its own character.
2. **Full project context (not a summary)** — tell the subagent to RECALL from the shared `.said`
   brain itself: `said ask "<what this phase needs>"`, `said sym <Name>`, `said get <doc_id>`. Every
   agent has the *same* full-codebase memory — no degraded hand-off, no telephone game.
3. **Prior-phase results** — the hand-off is via memory: when a phase finishes it `said.remember`s its
   key findings, so the next agent recalls them with `said ask` ALONGSIDE the codebase. Execution thus
   sees research's findings AND the real code, through its own persona — full context, every time.
4. **The phase + the ticket text** — what to do and the issue.

Dispatch: `parallel` → independent phases (research + planning) in one message with multiple Task
calls; `sequential` → one after another, each remembering before the next begins. A phase with no
agent (or no routing file) runs inline. Keep PR-only, test-first discipline regardless of which agent
did the work.

## Step 3: Plan

Read `.evolve/plan_prompt.md`. Follow an Impact×Urgency framework. Write
`SESSION_PLAN.md`: one focused task per ticket (smallest correct change + a test that proves it).
Commit: `git add SESSION_PLAN.md && git commit -m "mutate: session plan"`.

## Step 3b: Approval checkpoint (interactive run control)

If `ADVISORY_APPROVAL` is `required` (env), the operator reviews the plan before you implement:

1. **Post the plan** to the dashboard:
   `curl -s -X POST "$ADVISORY_API/evolution/run/$ADVISORY_RUN/plan" -H "Content-Type: application/json" -d "{\"plan\": <SESSION_PLAN.md as a JSON string>}"`
2. **Poll for the decision** (every ~5s, up to ~10 min):
   `curl -s "$ADVISORY_API/evolution/run/$ADVISORY_RUN/decision"` → JSON `{"approval":"pending|approved|rejected","subIssue":"..."}`.
   - `approved` → proceed to Step 4. If `subIssue` is non-empty, **incorporate that correction** into the plan first.
   - `rejected` → **stop here**: do not implement, do not open a PR. Report that the operator rejected the plan (with their note). Run finished.
   - `pending` → keep polling.

If `ADVISORY_APPROVAL` is unset/`auto`, skip this step and implement directly.

## Step 4: Implement each task

For each task in `SESSION_PLAN.md`:

1. **Write or update a test first** that captures the expected behaviour.
2. Make the **minimum** surgical edit to the source — do not rewrite whole files, do not touch
   unrelated code, CI, secrets, Dockerfiles, or the gate's security controls.
3. Verify:
   ```
   dotnet build src/Advisory.Api/Advisory.Api.csproj -c Release --nologo
   dotnet test tests/Advisory.Tests/Advisory.Tests.csproj --nologo
   ```
   If the change touches the web console, also run `npm --prefix web run build`.
4. If it builds and tests pass → commit (`git commit -m "mutate: <ticket> <summary>"`).
   If stuck after 3 attempts → `git checkout -- .` and move on (record it as a partial reply).

## Step 5: Open the pull request (PR-ONLY)

```
./scripts/mutate-ide.sh finish
```

This pushes your branch (`mutation/session-<date>`), opens a **pull request** for human review, and
posts a comment on each addressed ticket linking the PR. If tests did not all pass it opens a
**draft** PR flagged for review. **It never pushes to `main` and never merges.**

## Step 6: Journal

Bump `DAY_COUNT` (read it, add 1, write it back). Append a short, honest entry to the top of
`JOURNAL.md` (below `# Journal`), in the voice from `PERSONALITY.md`: what you changed and why, what
went well, what didn't, what surprised you. Not a commit list — a real entry. Commit it onto the
session branch.

## Step 7: Memory & research (this is how you grow)

1. **Learning** (only if genuinely novel): append one JSON line to `memory/learnings.jsonl` —
   `{"day":N,"ts":"<iso>","title":"...","context":"...","takeaway":"..."}` — and, if it's a lesson
   you'll want every session, add a bullet to `memory/active_learnings.md`. Don't log trivia.
2. **Research gap** (always consider): if this session revealed something you didn't understand or
   couldn't handle well, append a `### [ ]` entry to `RESEARCH.md` with a Goal. If you closed a gap,
   check its box. Commit `RESEARCH.md` and any memory changes onto the session branch.
3. **Project memory (REMEMBER — make the brain smarter for next time):** write back what you learned
   to the `said` brain so future cycles recall it instead of rediscovering it:
   - `said.remember("<concise learning>: <where + why>")` — store the fix/gotcha/decision (salience +
     surprise are scored automatically). One or two high-value memories, not a transcript.
   - `said.session_end("<1-2 line summary of this cycle>")` — flush an episodic summary of the run.
   This is the compounding-memory loop: recall at Step 2, remember here. (CLI fallback:
   `./tools/said/said.exe add --title "<t>" "<learning>"`.)

## Step 8: Report

State: tasks completed vs reverted, tickets addressed (implemented / partial / wontfix / reply),
the journal entry title, any learning recorded, and the PR URL. Then stop.
