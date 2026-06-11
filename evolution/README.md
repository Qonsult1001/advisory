# PkgFirewall Evolution

Self-evolving code maintenance for PkgFirewall, driven by GitHub tickets and tester comments.

There are two layers:

## 1. The harness — `evolution/harness/`  ← the engine

A ticket-driven, **PR-only** evolution loop adapted from
[yoyo-evolve](https://github.com/yologdev/yoyo-evolve) (MIT). The "brain" is **Claude Code** running
the `/evolve` command — no separate Rust binary, no API-key juggling; it uses your existing Claude
login. When a tester labels an issue `evolve` (or comments on one), it plans → implements a focused
change + test → builds/tests (`dotnet`/`vite`) → **opens a PR for human review**. Never merges.

This is the folder you push to the private GitHub repo. See `harness/README.md` for setup, the
GitHub Actions trigger, and the safety model.

## 2. The dashboard — `src/PkgFirewall.Api/Evolution/` + the **Evolution** sidebar tab

A read/trigger view inside PkgFirewall: it lists the repo's `evolve` tickets, shows evolution runs
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
| Upstream | yoyo-evolve — https://github.com/yologdev/yoyo-evolve (MIT) |
| Your fork | `G:\development\SAID-ECHO\EVOLVE` (GitHub: `Qonsult1001/SAID-ECHO`) |
| Brain | Claude Code CLI (`/evolve` command) — your existing licence |
| Run mode | GitHub Actions on `evolve`-labelled issue / tester comment, PR-only |

> Note: the earlier Rust-binary bridge approach has been superseded by the Claude-CLI harness above,
> which is simpler, key-free, and the canonical upstream mechanism.
