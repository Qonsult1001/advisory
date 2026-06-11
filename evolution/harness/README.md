# PkgFirewall Evolution Harness

A **ticket-driven, PR-only** self-evolution loop for the PkgFirewall codebase, adapted from
[yoyo-evolve](https://github.com/yologdev/yoyo-evolve) (MIT). When a tester files a GitHub issue
labelled **`evolve`** (or comments on one), the loop runs the `/evolve` cycle through **Claude Code**
— it plans, implements a focused change, writes a test, builds + tests, and **opens a pull request
for human review**. It never pushes to `main` and never merges.

## How it works

```
issue labelled `evolve`  ──►  GitHub Action  ──►  Claude Code runs /evolve
                                                       │
              scripts/evolve-ide.sh setup  ◄───────────┤  (fetch tickets+comments, branch)
              implement + test (dotnet/vite) ◄──────────┤  (smallest correct change + test)
              scripts/evolve-ide.sh finish ◄────────────┘  (push branch, open PR, reply on ticket)
```

- **Brain:** Claude Code CLI (your existing login) executing `.claude/commands/evolve.md`.
- **Infrastructure:** `scripts/evolve-ide.sh` (GitHub + git + build/test + PR). PR-only is enforced
  here — it refuses to operate on the default branch.
- **Stack-aware:** builds/tests with `dotnet` and `vite` (this is a .NET 10 + React repo).

## Run it — the timer (no API key, no secret)

The primary mechanism is a **local loop that uses your existing Claude Code login**. There is
**nothing to configure for auth** beyond being logged into the `claude` and `gh` CLIs.

```bash
gh auth login                          # once — GitHub access for `gh`
./scripts/evolve-claude.sh --loop 30m  # tick every 30 min
```

Each tick runs `claude -p "/evolve"`. An **internal timer** inside `scripts/evolve-ide.sh` decides
which ticks actually connect to GitHub and do work:

```bash
EVOLVE_HOURS=0,4,8,12,16,20   # default: act only at these hours; other ticks print SKIPPED
EVOLVE_HOURS='*'              # act every tick
FORCE_RUN=true               # bypass the schedule, act now
```

So you keep the loop running cheaply; it only opens PRs on schedule, and only when an open issue is
labelled `evolve`.

## Remote trigger (GitHub Actions) — runs the SAME scripts

`.github/workflows/evolve.yml` lets a ticket trigger evolution remotely. When an issue is labelled
`evolve` (or a tester comments on a labelled one), the workflow installs the Claude CLI and runs
**`scripts/evolve-claude.sh`** — the exact entrypoint the local timer uses. So CI and local share one
code path; the workflow is just a remote trigger for the scripts.

CI has no interactive Claude login, so this path (and only this path) needs **one secret**:
`CLAUDE_CODE_OAUTH_TOKEN` (from `claude setup-token`) or `ANTHROPIC_API_KEY`. GitHub access uses the
built-in `GITHUB_TOKEN` — no extra secret. The local timer above needs **no secret at all**.

## Safety (enforced, not optional)

1. **PR-only.** `evolve-ide.sh` pushes a session branch and opens a PR. It `die`s if asked to act on
   the default branch. A human reviews and merges.
2. **Tests gate the PR.** Green build+tests → normal PR. Red → **draft** PR flagged for review.
3. **Label-gated.** Only issues a human labels `evolve` are acted on.
4. **Scope-limited.** The `/evolve` command instructs minimal, surgical edits and forbids touching
   CI, secrets, Dockerfiles, or the gate's security controls.

## Watching it

PkgFirewall's **Evolution** dashboard (sidebar) reads this repo's tickets, runs, and PRs. Point it
at the repo via `EVOLUTION_REPO=owner/name` and `EVOLUTION_ENABLED=true` on the API.

## Attribution

Adapted from **yoyo-evolve** by yologdev — https://github.com/yologdev/yoyo-evolve — under the MIT
License (see `NOTICE`). The `/evolve` pipeline shape and the `plan`/`debug`/`test`/`self-assess`
skills derive from that project; the build/test steps and PR-only safety model are re-authored for
this .NET+React repository.
