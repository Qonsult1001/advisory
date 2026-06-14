# `said edit` — Fixes Needed (from live Advisory integration, v0.6.0)

Tested `said 0.6.0` (`said-full-linux-x64`) end-to-end in the Advisory container against a real C# repo.
**`said edit` got the autonomous Groq cycle all the way from ticket → plan → approve → edit.** Two issues
block it from completing on real C# files. #1 is a hard blocker (affects every C# class); #2 is a docs/UX
gap. Everything else worked (sym, ask, get, grep, add, `--explain`, syntax-verify, `append-into-symbol`
on a clean class).

---

## 🔴 FIX 1 (BLOCKER): `append-into-symbol` can't disambiguate a symbol name that matches multiple spans

### What happens
```
said edit --path Advisory.said --file tests/Advisory.Tests/HealthTests.cs \
  append-into-symbol --symbol HealthTests --content-file new_test.txt --json
→ {"ok":false,"error":"ambiguous: 2 symbols match in tests/Advisory.Tests/HealthTests.cs
   (HealthTests:10-124, HealthTests:13-13); disambiguate by line"}
```

The brain indexed **two** symbols named `HealthTests` in that file:
- `HealthTests:10-124` — the **class** (what we want to append into)
- `HealthTests:13-13`  — the **constructor** `public HealthTests(...)` (same name, by C# rules)

`said edit` correctly refuses to guess (good!) — but **there is no CLI way to disambiguate**, so the edit
can never succeed.

### Why it's a hard blocker for C#
**In C#, EVERY class shares its name with its constructor(s).** So `append-into-symbol --symbol <ClassName>`
is ambiguous for essentially every non-trivial C# class. The single most valuable mode (the "add a member
at class scope" fix for CS0106) is therefore **unusable on real C# classes** as it stands.

### The error says "disambiguate by line" — but there's no flag for it
`said edit --help` lists `--symbol`, `--anchor`, `--content`, `--content-file`, `--explain`, `--allow-large`.
**There is no `--line`, `--occurrence`, or `--span` option.** So the instruction in the error message can't
be followed.

### Requested fix (any one of these, in order of preference)
1. **Add `--line <N>`** (or `--at-line <N>`): when a `--symbol` matches multiple spans, pick the one whose
   `start_line == N`. The error already prints the candidate start lines, so the caller can pass one back.
   `append-into-symbol --symbol HealthTests --line 10`.
2. **Prefer the enclosing/largest span by default** for `append-into-symbol` specifically. A constructor at
   `13-13` is *inside* the class `10-124`; "append a member into `HealthTests`" almost always means the
   class body, not the 1-line constructor. Defaulting to the largest enclosing span (with a note) would make
   the common case work with no extra args. (Could combine with #1 as an override.)
3. **Add `--kind <class|method|interface|...>`**: `append-into-symbol --symbol HealthTests --kind class`
   resolves to the class span. Disambiguates by declaration kind, which the AST already knows.

### Also: make `--explain` return the line so the caller CAN disambiguate
Today `--explain --symbol HealthTests` returns:
```json
{"valid_anchors":[{"mode":"append-into-symbol","symbol":"HealthTests","note":"...class scope..."}]}
```
It should include the **line/kind** needed to disambiguate, e.g.:
```json
{"valid_anchors":[{"mode":"append-into-symbol","symbol":"HealthTests","line":10,"kind":"class",
                   "note":"add a sibling member at the end of class HealthTests (lines 10-124)"}]}
```
Then an autonomous caller reads `--explain`, sees `line:10 kind:class`, and issues
`append-into-symbol --symbol HealthTests --line 10` — first try, no failed edit. Right now `--explain` gives
a menu that, when used, still hits the ambiguity error — so the menu isn't actionable for this case.

### Acceptance test
On a normal C# test class (class name == a constructor name):
```
said edit --file tests/.../HealthTests.cs append-into-symbol --symbol HealthTests --line 10 --content-file m.txt --json
→ {"ok":true, ...}  and the method lands at class scope (before the class's closing brace), auto-indented.
```

---

## 🟡 FIX 2 (DOCS/UX): `said edit --help` Modes line is missing the new modes

`said edit --help` prints:
```
Modes: insert-after-symbol | insert-before-symbol | replace-symbol | delete-symbol
       | insert-after-text | insert-before-text | replace-text
```
But the binary actually supports (and the handoff doc documents) **`append-into-symbol`** and the three
**context** modes (`insert-after-context` / `insert-before-context` / `replace-context`). They work — they're
just not listed in `--help`. An integrator reading `--help` won't discover the most important mode
(`append-into-symbol`). Please add all 8 modes to the `--help` Modes line and the usage examples.

---

## ✅ What worked (no action needed — for confidence)

Verified live in the Advisory Linux container (`said 0.6.0`, brain = `Advisory.said`, 673 frames):
- `said stats` — 673 frames, 384 symbols, ~10.7× compression.
- `said sym GateEngine` — exact AST range `GateEngine.cs::...:24` lines 24-421.
- `said grep MapGet` / `said grep Fact` — real code content returned.
- `said ask "how does the firewall block a package"` — recalled `GateEngine` at **0.85 confidence** from a
  pure concept query (no symbol named). Semantic recall is excellent.
- `said get <doc_id>` — exact symbol body.
- `said add "<memory>"` — saves a memory; subsequent `ask` recalls it. (NOTE: the **CLI** verb is `add`;
  the **MCP** tool is `remember` — worth aligning or documenting, it cost us a silent no-op.)
- `said edit --explain --symbol HealthTests` — returns a `valid_anchors` menu (but see Fix 1: needs line/kind).
- `said edit append-into-symbol` on a **single-class** file (no name clash) — member landed at class scope,
  auto-indented, file still parses. The mode itself is correct; only the C# class/ctor name clash blocks it.
- Post-edit **syntax-verify** — confirmed it rejects edits that would break parsing.

---

## Context: why this matters

Advisory runs an autonomous mutation cycle (ticket → LLM plan → operator approval → LLM writes a SURGICAL
edit via `said edit` → in-container `dotnet build`+`test` gate → PR). `said edit` is the safety core: it makes
whole-file rewrites (which once gutted `Program.cs` 161→27 lines) impossible by construction. With Fix 1,
`append-into-symbol` becomes usable for C# test classes and the cycle completes autonomously on real C# code.
