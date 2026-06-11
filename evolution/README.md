# Advisory Evolution

Self-evolving code maintenance for Advisory, driven by GitHub tickets and tester comments.

There are two layers:

## 1. The harness — `evolution/harness/`  ← the engine

A ticket-driven, **PR-only** evolution loop. The "brain" is running
the `/mutate` cycle. When a tester labels an issue `mutation` (or comments on one), it plans → implements a focused
change + test → builds/tests (`dotnet`/`vite`) → **opens a PR for human review**. Never merges.

This is the folder you push to the private GitHub repo. See `harness/README.md` for setup, the
GitHub Actions trigger, and the safety model.

## 2. The dashboard — `src/Advisory.Api/Evolution/` + the **Evolution** sidebar tab

A read/trigger view inside Advisory: it lists the repo's `mutation` tickets, shows evolution runs
and their PRs, and lets an admin trigger a run. It talks to GitHub via the `gh` CLI and is **disabled
by default**. Enable with:

```
EVOLUTION_ENABLED=true
EVOLUTION_REPO=owner/your-private-repo
EVOLUTION_LABEL=evolve          # default
```

## Engine provenance

| | |
|---|---|
| Brain | Claude Code CLI (`/mutate` command) — your existing licence |
| Run mode | GitHub Actions on `evolve`-labelled issue / tester comment, PR-only |

