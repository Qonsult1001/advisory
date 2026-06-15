# `said edit` — Integration Feedback & Fixes (for the `.said` maintainers)

**From:** Advisory autonomous mutation cycle (a real .NET 10 + React repo).
**Tested against:** `said 0.6.0` (`said-full-linux-x64`), live in the Advisory Linux container.
**Status:** `said edit` got the cycle all the way from **ticket → plan → operator approval → surgical edit**.
The one hard blocker we hit (C# class/constructor name clash) is **already fixed in the v0.7.0 handoff** —
this doc confirms it resolves our case, adds **one new finding** about `--explain`, and lists what we have
**not yet been able to verify end-to-end** so you know the boundary of our testing.

---

## ✅ Already addressed in v0.7.0 (confirmed against our exact failure)

Our blocker on 0.6.0 was:
```
said edit --file tests/Advisory.Tests/HealthTests.cs append-into-symbol --symbol HealthTests --content-file m.txt --json
→ {"ok":false,"error":"ambiguous: 2 symbols match (HealthTests:10-124, HealthTests:13-13); disambiguate by line"}
```
`HealthTests:10-124` = the class (what we want); `HealthTests:13-13` = its constructor (same name, by C# rules).
This affects **every C# class** (class name always == constructor name), so `append-into-symbol --symbol <Class>`
was unusable on real C# classes.

The **v0.7.0 handoff** addresses this exactly as we needed — all three requested fixes are present:
- **`--line <N>`** to select the span by start line ✔
- **`append-into-symbol` defaults to the largest/enclosing span** (picks the class, not the 1-line ctor),
  so it works with **no extra args** ✔
- **`--explain` returns `line` + `kind`** so the menu is actionable ✔
- **`--help` lists all 11 modes** ✔

**This is the right fix.** Note our consumer sends `append-into-symbol --symbol HealthTests` with **no `--line`**,
so the **largest-span default is what will unblock us with zero code change on our side** — please keep that
default behavior. (We'll also wire explicit `--line` from `--explain` as a belt-and-suspenders.)

> ⚠️ We have **not** been able to run this yet: the v0.7.0 binary isn't in `dist-binaries/` (only `v0.6.0/`).
> The moment `dist-binaries/v0.7.0/said-full-linux-x64/` exists, we bake it in and run the full cycle. The
> confirmation above is from the **handoff doc**, not a live run.

---

## 🟡 NEW FINDING: `--explain` (and any source-reading op) must run where the SOURCE lives, not where the brain lives

`said edit --explain` reads the **on-disk source file** to compute `valid_anchors`. In our deployment the
**brain and the source are in different places**:
- The `.said` brain is baked into the image at `/app/Advisory.said`.
- `/app` contains **compiled DLLs, not the source tree.**
- The source only exists in the **throwaway git clone** the cycle makes (`/tmp/advisory-groq-<id>/`).

So calling `--explain` against the baked brain failed:
```
said edit --path /app/Advisory.said --file tests/Advisory.Tests/HealthTests.cs --explain --symbol HealthTests --json
→ {"ok":false,"error":"read tests/Advisory.Tests/HealthTests.cs: No such file or directory (os error 2)"}
```

**This is arguably correct behavior** (it needs the file to inspect), but two things would help integrators:
1. **Document clearly** that `--explain` / symbol-mode edits resolve `--file` **relative to the current working
   directory** (where the source is), independent of where `--path` (the brain) points. We worked it out, but
   it cost a debugging cycle and a temporarily-disabled `--explain` call.
2. **Optional, nice-to-have:** a brain-only `--explain` that returns the symbol spans/kinds **from the index
   alone** (the brain already stores `start_line`/`end_line`/`kind`), without touching disk. That would let a
   caller pre-validate using just the baked brain, before it has a working copy checked out. Not required —
   our fix is to run `--explain` inside the clone — but it would make the brain more self-sufficient.

---

## 🟢 Minor: CLI verb vs MCP tool name mismatch for saving memory

- **CLI:** `said add "<text>"` saves a memory.
- **MCP:** the tool is `remember`.

We initially called `said remember …` from the CLI (matching the MCP name) and it **silently no-opped**
(`error: unrecognized subcommand 'remember'`) so memories were never saved. Please either alias `remember`
→ `add` in the CLI, or note the difference prominently. (We've switched to `add`.)

---

## ✅ What we verified working live (so you know what's solid)

In the Advisory container, `said 0.6.0`, brain `Advisory.said` (rebuilt from current source, 673 frames):

| Capability | Result |
|---|---|
| `said stats` | 673 frames, 384 symbols, ~10.7× compression |
| `said sym GateEngine` | exact AST span `GateEngine.cs::…:24` lines 24–421 |
| `said grep MapGet` / `grep Fact` | real code content returned |
| `said ask "how does the firewall block a package"` | recalled `GateEngine` at **0.85 confidence** from a pure concept query (no symbol named) — semantic recall is excellent |
| `said get <doc_id>` | exact symbol body |
| `said add "<memory>"` + `ask` | memory saved and recalled |
| `said edit --explain --symbol HealthTests` (in a dir WITH source) | returns a `valid_anchors` menu ✔ |
| `said edit append-into-symbol` on a **single-class** file (no name clash) | member lands at class scope, auto-indented, file still parses ✔ |
| post-edit **syntax-verify** | rejects edits that would break parsing ✔ |
| ambiguity guard | refuses on >1 symbol match instead of guessing ✔ (this is the behavior v0.7.0 now lets us resolve) |

---

## ❓ What we have NOT yet verified end-to-end (the honest boundary)

The cycle has **never completed past the `said edit` step** (the ambiguity blocked it on 0.6.0). So these
paths are **unexercised with a real, multi-edit, Groq-generated change set** and may surface follow-ups:

1. **Two edits applied together** — our change set is `[endpoint insert-after-text in Program.cs, test
   append-into-symbol in HealthTests.cs]`. We've only seen the *test* edit fail (ambiguity); we haven't
   confirmed **both** apply cleanly in one run, nor the **transactional all-or-nothing** behavior across files
   (we apply per-edit via CLI and abort on first failure; `edit_batch` is MCP-only and we're on the CLI).
2. **Anchor-drift detection in practice** — we copy the baked `Advisory.said` into the clone, then `said edit`
   there. If the clone's source differs from the baked brain, v0.4.0 drift-detection may refuse with "run
   `said reindex`". We haven't hit or handled that path live.
3. **Post-edit syntax-verify on a real Groq-written method** — could reject if the model's content has a
   subtle structural issue; unexercised in a full run.

None of these are known bugs — they're just **untested** because we never got past the first wall. We'll learn
the truth on the first clean run with v0.7.0. Flagging them so "v0.7.0 unblocks us" is understood as **"v0.7.0
unblocks the *known* blocker"** — there may be a second wall behind it, which we'll report if so.

---

## Context (why this matters)

Advisory runs an autonomous mutation cycle: ticket → LLM plans → **operator approves** → LLM emits a SURGICAL
edit applied via `said edit` → in-container `dotnet build`+`test` gate → PR (PR-only, human merges). `said edit`
is the safety core: it makes whole-file rewrites — which once **gutted `Program.cs` 161→27 lines and broke
`main`** — impossible by construction. With v0.7.0's class/ctor disambiguation, `append-into-symbol` becomes
usable for C# test classes and the cycle should complete autonomously on real C# code. Thank you for the fast
turnaround on each round of feedback.
