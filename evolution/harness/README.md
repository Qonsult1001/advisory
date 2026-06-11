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

## Run it

**On a ticket (CI, recommended):** `.github/workflows/evolve.yml` fires when an issue is labelled
`evolve` or a tester comments on a labelled issue. Add a repo secret
`CLAUDE_CODE_OAUTH_TOKEN` (or `ANTHROPIC_API_KEY`).

**Locally (manual):**
```bash
gh auth login                       # once
./scripts/evolve-claude.sh          # one cycle
./scripts/evolve-claude.sh --loop 1h
```

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
