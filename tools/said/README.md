# `said` 0.11.1 — project brain + closed-loop orchestrator for the mutation cycle

Three self-contained binaries (encoder model baked in via `include_bytes!` — no DLLs, no side files),
shipped per-platform: **`.exe` for the Windows host/IDE (outside Docker)** and **`-linux` for WSL /
the container**.

| Binary | Role |
|--------|------|
| `said` / `said-linux` | The `.said` CLI — builds + queries the project brain (`sym`/`grep`/`ask`/`get`). |
| `said-mcp` / `said-mcp-linux` | The stdio MCP server, so IDEs/agents query the brain over MCP. |
| `said-orchestrate` / `said-orchestrate-linux` | **The closed-loop driver.** Runs plan → design → code → test → repair → learn against ANY external LLM, gate-verified. Replaced the hand-driven `claude -p /mutate` cycle. |

## Why the binaries aren't committed
They're large compiled binaries (~60 MB each), so they're **gitignored** (only this README is tracked).
The generated `Advisory.said` brain is also gitignored — the worker rebuilds it. Drop the v0.11.1
binaries from `said-build/dist-binaries/v0.11.1/said-full-linux-x64/` (Linux) and
`said-build/target/release/*.exe` (Windows) into this folder.

## How the worker uses them (`scripts/mutate-claude.sh`)
- **`build_context`** rebuilds the `.said` brain when missing/forced. It indexes **`src` + `tests` only**
  (the said-build recipe): indexing the whole repo blew said 0.11.1's embedding pass to ~10 GB RAM and
  OOM-killed `init`; `src`+`tests` is ~750 frames and peaks ~100 MB. `init` takes a single dir, so the
  worker copies `src`+`tests` into a scratch dir, inits that, and moves the brain into place.
- **`run_cycle_orchestrate`** (engine = `orchestrate`, the default) drives each ticket:
  `setup` (branch) → `said-orchestrate --brain Advisory.said --repo . --task <ticket> --build <gate>
  --test <gate> --max-attempts 3` → on green, `finish` (open PR). PR-only; the operator `release`s.
- **Provider = the MAF selection.** The worker reads `/admin/routing/mutation` (the dashboard model
  dropdown) and exports the matching env: Groq → `GROQ_API_KEY`+`GROQ_MODEL`; any OpenAI-compatible
  endpoint (OpenRouter, on-prem) → `OPENAI_API_KEY`+`SAID_LLM_BASE_URL`+`SAID_LLM_MODEL`. Falls back to
  `MUTATE_LLM=groq|openrouter` if the API isn't reachable. **The gate is the sole ground truth** — the
  orchestrator never merges red; on exhausted attempts it stops with the tree clean.
- Set `MUTATE_ENGINE=claude` to fall back to the legacy `claude -p /mutate` path.

## Quick check it's the right build
```bash
tools/said/said-linux --version                              # must print: said 0.11.1
tools/said/said-linux sym GateEngine --path Advisory.said    # → src/Advisory.Api/Gate/GateEngine.cs:24
```
If `sym` returns 0 symbols, the brain was built wrong (or with an old binary) — rebuild via
`FORCE_CONTEXT=true` (which rebuilds scoped to `src`+`tests`).

See `docs/said-orchestrator-design.md` for the closed-loop design.
