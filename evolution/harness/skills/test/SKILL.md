> **Stack note (Advisory):** this is a .NET 10 + React repo, not Rust. Map the cargo examples below to:
> `cargo build` → `dotnet build src/Advisory.Api/Advisory.Api.csproj -c Release --nologo`;
> `cargo test` → `dotnet test tests/Advisory.Tests/Advisory.Tests.csproj --nologo`;
> `cargo clippy` → (n/a); web build → `npm --prefix web run build`. The principles still apply.

---
name: test
description: Validate changes through unit tests, integration tests, benchmarks, and regression checks.
tools: [bash, read_file, write_file, edit_file]
---

# Test

You are validating that your code works correctly. Testing is not optional —
it's how you know your changes are safe before committing.

## Testing workflow

1. **Run the full suite first** to establish a baseline:
   ```bash
   cargo test 2>&1
   ```
2. **Identify what changed** and what tests cover those changes.
3. **Write new tests** for any untested behavior.
4. **Run targeted tests** for faster iteration:
   ```bash
   cargo test test_name
   cargo test module_name::
   ```
5. **Run the full suite again** to catch regressions.

## What to test

- **Happy path** — Does the feature work with normal input?
- **Edge cases** — Empty input, very long input, Unicode, special characters.
- **Error paths** — Does the code fail gracefully? Are error messages useful?
- **Regressions** — If this fixes a bug, write a test that reproduces the bug first.

## Writing good tests

```rust
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_descriptive_name_of_behavior() {
        // Arrange
        let input = ...;

        // Act
        let result = function_under_test(input);

        // Assert
        assert_eq!(result, expected);
    }
}
```

- Test names should describe the behavior, not the implementation.
- One assertion per test when possible.
- Use `#[should_panic]` for expected panics.
- Use `assert!(result.is_err())` for expected errors.

## Full verification cycle

After writing tests, run the complete CI check:
```bash
cargo fmt
cargo clippy --all-targets -- -D warnings
cargo build
cargo test
```

All four must pass before committing.

## Test coverage gaps

To find untested code:
1. Read through `src/` and check which functions have corresponding tests.
2. Look for `unwrap()` calls — these are untested failure paths.
3. Check complex match arms — each variant should have a test.
4. Look for conditionals — test both branches.

## Rules

- **Never delete existing tests.** Tests are your safety net.
- **Write the test before the fix.** Prove the bug exists, then fix it.
- **Tests must be deterministic.** No reliance on timing, network, or random state.
- **Keep tests fast.** If a test needs external resources, mock them or skip in CI.
- **Failed tests are information.** Read the output carefully before changing anything.
- If you find a test that's flaky, fix the flakiness — don't delete the test.

## When to test

- Before every commit (run the full suite).
- After any code change, no matter how small.
- When self-assess identifies untested code paths.
- When debugging — write a test that reproduces the bug first.
