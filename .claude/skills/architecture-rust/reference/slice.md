# Vertical slice — anatomy

A use case is a **vertical slice through one capability crate**: domain → service → adapter →
binding. Issues are sliced this way (a runnable thread through all layers), never as a horizontal
"do all the domain first" layer. The rules below are the placement decisions, not Rust syntax (the
model writes idiomatic `thiserror`/`async_trait`/builders by default — that isn't the point here).

Example: an `llm` capability crate exposing a `complete` use case over swappable providers.

| Layer | File | What lands here | The rule |
|-------|------|-----------------|----------|
| **Domain** | `error.rs` | `LlmError` (thiserror) + `LlmResult<T>`; a `FailureClass` enum if callers branch on failure | typed error, not `String` |
| **Domain** | `ports.rs` | `trait LlmProvider: Send + Sync` (+ `#[async_trait]`) | **port lives in the owning crate's domain, never in the adapter** |
| **Domain** | `model/` | `CompletionRequest` etc. — private fields, behaviour methods, invariants validated in-aggregate | no `pub` field that bypasses an invariant |
| **Service** | `complete/` | `CompletionService` orchestrating the port | depends on `&dyn LlmProvider`, **not** a concrete adapter (testable, target-agnostic) |
| **Adapter** | `providers/anthropic.rs` | `AnthropicProvider` + `impl LlmProvider` | the **only** layer that touches HTTP / a runtime; helpers `pub(crate)` |
| **Binding** | `lib.rs` | re-export the public API; a `provider_from_config(cfg) -> Box<dyn LlmProvider>` factory | a library-only project stops here; a surface adds a thin entry |

## The load-bearing rules (not no-ops)

- **Port in domain, not adapter.** The trait belongs to the crate that owns the *concept*; adapters
  `impl` it. Defining it beside the adapter inverts the dependency.
- **Service depends on `dyn` the port.** Not a concrete provider — that's what makes it unit-testable
  with a stub and free of the adapter's deps.
- **Factory maps config → `Box<dyn Port>`** in `lib.rs`; the surface calls the factory and never names
  a concrete adapter.

## Testing the slice

Exercise the **service through a stub port** — assert observable behaviour, not internals. Ship the
stub behind a `stub-<x>` feature (also on under `cfg(test)`) so the separately-compiled `tests/`
integration crate can use it too.

## The slice as an issue

One issue = port (if new) + service + one adapter + binding + tests, all in **one crate**. Never "add
all the ports" or "do the whole adapters layer" — those are horizontal and can't be demonstrated
end-to-end.
