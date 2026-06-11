# Autonomous Evolution Cycle (PkgFirewall · PR-only)

Run one self-evolution cycle on this repository. You are the LLM; `scripts/evolve-ide.sh` handles
infrastructure (GitHub, git, build, tests, PR). Execute every step in order. **Do not stop, do not
ask for permission, do not summarize between steps.**

> Adapted from yoyo-evolve (MIT, https://github.com/yologdev/yoyo-evolve). This repo is a
> .NET 10 + React codebase (PkgFirewall), so build/test use `dotnet` and `vite`, and the cycle is
> **PR-only**: changes go to a branch and open a pull request for human review — never to `main`.

## Step 1: Setup

```
./scripts/evolve-ide.sh setup
```

This verifies the build, fetches open GitHub issues labelled `evolve` (with their tester comments),
and writes `.evolve/ISSUES_TODAY.md` and `.evolve/plan_prompt.md`. If there are **no** labelled
issues and no pending tester replies, it prints `NO WORK` — in that case, stop here and report
"nothing to evolve".

## Step 2: Understand the codebase

- Read `CLAUDE.md` and the relevant source under `src/PkgFirewall.Api/` and `web/src/`.
- Read `.evolve/ISSUES_TODAY.md` (the tickets + tester comments to address).
- Read the last 3 entries of `JOURNAL.md` if it exists (have you tried this before?).

Note the specific bug/gap each ticket describes. Do not invent work beyond the tickets.

## Step 3: Plan

Read `.evolve/plan_prompt.md`. Follow the `plan` skill's Impact×Urgency framework. Write
`SESSION_PLAN.md`: one focused task per ticket (smallest correct change + a test that proves it).
Commit: `git add SESSION_PLAN.md && git commit -m "evolve: session plan"`.

## Step 4: Implement each task

For each task in `SESSION_PLAN.md`:

1. **Write or update a test first** that captures the expected behaviour.
2. Make the **minimum** surgical edit to the source — do not rewrite whole files, do not touch
   unrelated code, CI, secrets, Dockerfiles, or the gate's security controls.
3. Verify:
   ```
   dotnet build src/PkgFirewall.Api/PkgFirewall.Api.csproj -c Release --nologo
   dotnet test tests/PkgFirewall.Tests/PkgFirewall.Tests.csproj --nologo
   ```
   If the change touches the web console, also run `npm --prefix web run build`.
4. If it builds and tests pass → commit (`git commit -m "evolve: <ticket> <summary>"`).
   If stuck after 3 attempts → `git checkout -- .` and move on (record it as a partial reply).

## Step 5: Open the pull request (PR-ONLY)

```
./scripts/evolve-ide.sh finish
```

This pushes your branch (`evolve/session-<date>`), opens a **pull request** for human review, and
posts a comment on each addressed ticket linking the PR. If tests did not all pass it opens a
**draft** PR flagged for review. **It never pushes to `main` and never merges.**

## Step 6: Journal

Append a short, honest entry to the top of `JOURNAL.md` (below `# Journal`): what you changed and
why, what went well, what didn't. Commit it onto the same branch.

## Step 7: Report

State: tasks completed vs reverted, tickets addressed (implemented / partial / wontfix / reply),
and the PR URL. Then stop.
