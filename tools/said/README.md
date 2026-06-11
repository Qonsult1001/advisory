# `said` — project-context brain for agents

`said.exe` is the **.said brain engine** (copied from the SAID-ECHO project). It gives mutation/
evolution agents Cursor-style full-codebase context: it indexes the whole repo (AST-aware, via
tree-sitter) into `Advisory.said`, and agents query it with `ask` / `sym` / `grep` / `query`.

## Why it isn't committed
`said.exe` is a ~44 MB compiled binary, so it's **gitignored**, not stored in the repo. Each machine
builds it once. The generated `Advisory.said` brain is also gitignored (the worker rebuilds it).

Two self-contained exes (encoder model baked in via `include_bytes!` — no DLLs, no side files):
- `said.exe` — the CLI used by the worker to build/query the project brain.
- `said-mcp.exe` — the stdio MCP server, so agents/IDEs can query the brain over MCP.

## Build them (one-time, needs Rust + the SAID-ECHO checkout)

```cmd
cargo build --release -p said-cli --features "code embed-model" --manifest-path G:\development\SAID-ECHO\Cargo.toml
cargo build --release -p said-mcp --features "code embed-model" --manifest-path G:\development\SAID-ECHO\Cargo.toml
copy /Y G:\development\SAID-ECHO\target\release\said.exe     tools\said\said.exe
copy /Y G:\development\SAID-ECHO\target\release\said-mcp.exe tools\said\said-mcp.exe
```

The **`code`** feature is essential — it bakes in the tree-sitter grammars (C#, TS/JS, Python, Go,
Java, Rust). Without it, `said init` won't chunk the source and the symbol table stays empty.

## How the worker uses it
`scripts/mutate-claude.sh` calls `build_context` at the start of a cycle: if `Advisory.said` is
missing it runs `said init` + `said add --dir src|web/src|tests` once. The `/mutate` skill then
queries the brain for context. Set `CONTEXT_FORMAT=md` to use a plain `PROJECT_CONTEXT.md` map
instead, or `FORCE_CONTEXT=true` to rebuild.

## Quick check it's the right build
```cmd
tools\said\said.exe init
tools\said\said.exe sym GateEngine   REM should return a class hit with a file:line
```
If `sym` returns 0 symbols, the binary was built without `code` — rebuild as above.
