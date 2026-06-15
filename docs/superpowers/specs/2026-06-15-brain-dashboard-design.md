# Advisory Brain Dashboard (WASM-powered) — Design

**Date:** 2026-06-15 · **Target:** `web/src/` (new `BrainDashboard.jsx`, replaces `MemoryPanel`)
**Engine:** `said-wasm` (from `G:/development/SAID-ECHO/crates/said-wasm`) — built, in `web/pkg/`.

## Problem

The current "Project memory" panel (`MemoryPanel`) shows 5 stat tiles + marketing
copy and a "brain isn't built yet" warning — you cannot see, search, or read a
single actual memory. For a product positioned as "the brain for self-healing,
looping, growing, bug fixes," that is all tell, no show.

## Key architectural decision — run the brain in the browser

The dashboard is a React panel that loads **`said-wasm`** and reads the `.said`
file **client-side** via `new SaidBrain(...)`. This means:

- **No new API endpoints.** The brain reads itself in the browser.
- **No dependency on the broken CLI binary.** The WASM runs the real `sca-core`
  retrieval engine against the file directly, so `list_memories` / `ask` /
  `search` / `stats` all work LIVE — sidestepping the said-0.9.0 incremental-index
  bug entirely (that bug is in the CLI write path; the WASM reads a finished file).

## WASM API (verified against `web/pkg/said_wasm.d.ts`)

```
new SaidBrain(bytes: Uint8Array, filename, encoderTokenizer, encoderSafetensors, encoderConfig)
  .stats() -> { active_frames, symbol_count, ... }
  .count_memories() -> number
  .list_memories(offset, limit) -> rows[]
  .get_memory(doc_id) -> detail
  .search(query, k) -> hits[]        // lexical/SCA
  .ask(query, k) / .ask_fused(query, top, deep) -> hits[]   // semantic (needs encoder)
  .sym_list(prefix, max) / .symbol_count() / .history(limit)
```

The constructor needs the encoder bytes (tokenizer/safetensors/config) for
semantic `ask`. Those ship under `web/encoder/` in the said-wasm web dir.

## Layout — Option C (hero + drill-down)

```
🧠 ADVISORY BRAIN    <headline: % tokens saved · grows daily>   [Download .said]
 ╔ memories ╗ ╔ recalls ╗ ╔ symbols ╗ ╔ consolidations ╗   (live stat tiles)
 [ Explore memories → ]  [ Recall → ]  [ Loop → ]
   each opens a focused panel below the hero
```

- **Explore** — paginated `list_memories` table (kind / source / salience), click →
  `get_memory` detail pane (full content + lineage).
- **Recall** — a search box → `ask_fused(query, 8, false)` → ranked results with
  scores. LIVE semantic recall against the real engine.
- **Loop** — per-cycle activity from the EXISTING `/api/evolution/runs` + PR data
  (recalled → fixed → learned). No WASM needed; reuses run records.

## Data sources (all real)

| Panel        | Source                                   |
|--------------|------------------------------------------|
| Stat tiles   | `brain.stats()` (WASM)                   |
| Explore      | `brain.list_memories()` / `get_memory()` |
| Recall       | `brain.ask_fused()` / `search()`         |
| Symbols      | `brain.sym_list()` / `symbol_count()`    |
| Loop         | `/api/evolution/runs` (existing)         |

## Brain source — auto-serve

On load, the dashboard fetches the project brain from the existing
`GET /admin/context/download` (serves `Advisory.said`), converts to `Uint8Array`,
and constructs `SaidBrain`. Brain appears automatically — no click. If the fetch
404s (brain not built yet), show the existing "build on next cycle" callout.

## Build steps

1. Copy `said-wasm/web/pkg/` (the `.wasm` + `said_wasm.js` glue + `.d.ts`) and
   `said-wasm/web/encoder/` into Advisory `web/public/said/`.
2. New `web/src/BrainDashboard.jsx`: `import init, { SaidBrain } from "/said/pkg/said_wasm.js"`,
   `await init()`, fetch brain + encoder bytes, `new SaidBrain(...)`, render hero +
   the three drill-down panels (Explore / Recall / Loop).
3. In `App.jsx`, replace the `<MemoryPanel .../>` mount under the
   "Project memory" SubHead with `<BrainDashboard .../>`. Keep the engine selector
   + download button (move them into the dashboard header).

## Honest boundaries

- One-time ~4.7 MB WASM + ~4.8 MB encoder fetch (browser-cached after first load).
  `ask` is disabled until the encoder finishes; `search`/`list`/`stats` work without it.
- The `.said` is loaded client-side — nothing leaves the browser; it's the same
  file `context/download` already serves.
- Phase 1 = hero + Explore + Recall + Loop, all live. Salience-trend / tokens-saved
  HISTORY needs snapshots over time (not stored yet) — show current value now, add
  the trend line in a later phase once we persist periodic `stats()` snapshots.

## Acceptance

- Open the Memory section → brain auto-loads → stat tiles show real counts.
- Explore → a real, paginated list of actual memories; click one → its content.
- Recall "where do endpoints register" → ranked real hits with scores.
- Loop → the last few mutation cycles with recalled/fixed/learned.
- Brain not built → graceful "build on next cycle" callout (no crash).
