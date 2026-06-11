> **Stack note (Advisory):** this is a .NET 10 + React repo, not Rust. Map the cargo examples below to:
> `cargo build` → `dotnet build src/Advisory.Api/Advisory.Api.csproj -c Release --nologo`;
> `cargo test` → `dotnet test tests/Advisory.Tests/Advisory.Tests.csproj --nologo`;
> `cargo clippy` → (n/a); web build → `npm --prefix web run build`. The principles still apply.

---
name: debug
description: Diagnose failures by reading errors, tracing execution, and isolating root causes.
tools: [bash, read_file]
---

# Debug

You are diagnosing a failure. Something went wrong — a build error, a test
failure, unexpected behavior, or a crash. Your job is to find the root cause,
not just make the symptom go away.

## Debugging workflow

1. **Read the error message.** The whole thing. Rust's error messages are excellent — they usually tell you exactly what's wrong.
2. **Reproduce the failure.** Run the failing command again to confirm it's consistent:
   ```bash
   cargo build 2>&1
   cargo test 2>&1
   cargo test specific_test_name 2>&1
   ```
3. **Isolate the cause.** Narrow down where the failure originates:
   - Which file and line does the error point to?
   - What was the last change before it broke?
   - Does reverting the last change fix it?
4. **Understand why.** Don't just fix the symptom. Ask:
   - Why did this break?
   - Is this a one-off or a pattern?
   - Could similar bugs exist elsewhere?
5. **Fix and verify.** Apply the fix, run the full test suite.

## Reading Rust errors

### Compiler errors
```bash
cargo build 2>&1 | head -50
```
Look for: file path, line number, error code (E0xxx), and the suggestion line.

### Test failures
```bash
cargo test 2>&1
# For more detail on a specific test:
cargo test test_name -- --nocapture 2>&1
```
Look for: `thread 'test_name' panicked at`, the assertion that failed, and the left/right values.

### Clippy warnings
```bash
cargo clippy --all-targets -- -D warnings 2>&1
```
Clippy warnings treated as errors in CI. Read the suggestion — clippy usually tells you exactly how to fix it.

### Runtime panics
Look for the backtrace:
```bash
RUST_BACKTRACE=1 cargo run 2>&1
```

## Common failure patterns

- **"cannot find value/type"** — Missing import, wrong module path, or typo.
- **"borrow checker"** — Read the lifetimes. Usually means you need to clone, use a reference, or restructure ownership.
- **"trait not implemented"** — Check if you need a derive macro or manual impl.
- **"test panicked"** — Check the assertion values. The expected vs actual tells the story.
- **"unresolved import"** — Module not declared in parent, or crate not in Cargo.toml.

## When you're stuck

1. **Check git diff** — What changed since it last worked?
   ```bash
   git diff HEAD~1
   git log --oneline -5
   ```
2. **Check JOURNAL.md** — Have you hit this before?
3. **Simplify** — Remove complexity until the error goes away, then add back piece by piece.
4. **Research** — If the error is unfamiliar, use the research skill to look it up.
5. **Try a different approach** — If the same fix isn't working, step back and reconsider the design. Sometimes the right fix is architectural, not syntactic.
6. **Revert as a last resort** — Only when you've genuinely exhausted your understanding:
   ```bash
   git checkout -- src/ Cargo.toml Cargo.lock
   ```

   But journal *why* you reverted — that's a learning for next session.

## Rules

- **Read the full error.** Don't skim. Rust errors contain the answer.
- **Reproduce before fixing.** Confirm the failure is real and consistent.
- **Fix the root cause, not the symptom.** Suppressing warnings or ignoring errors creates debt.
- **Don't guess-and-check randomly.** Form a hypothesis, test it, iterate.
- **Write a test for every bug you fix.** Prevent regressions.
- **If a fix feels wrong, it probably is.** Step back and reconsider.

## When to debug

- Build fails after a change.
- Tests fail unexpectedly.
- The agent crashes or produces wrong output.
- Clippy or fmt checks fail in CI.
- You observe behavior that doesn't match your expectations.
