# Extensibility — the three mechanisms

Three ways to make a Rust system extensible. They are **not interchangeable** — choosing wrong is the
most common Rust architecture mistake. Pick by *intent*; the canonical shapes follow. All three are
proven in `said-build`. (Step-by-step "add a format/language/provider" recipes: [extending.md](./extending.md).)

| You want to… | Mechanism | Selected at | Cost unused |
|--------------|-----------|-------------|-------------|
| add an optional **heavy capability** (parser, model, GPU) that changes binary size/deps | **Feature-gate** | compile time | zero (not compiled) |
| let a **peer be chosen at run time** (provider, source, adapter, plugin) | **Trait registry** | run time | one vtable hop |
| make a **whole subsystem** optional to a consumer | **Optional crate** | compile time | zero (crate not built) |

Discriminators: changes which bytes/deps ship → feature-gate or optional crate; chosen at run time
among interchangeable peers → registry; one file → feature-gate, whole crate → optional crate. A
`match` over swappable peers is a **missing registry**; an always-compiled heavy module is a
**missing feature-gate**.

## 1. Feature-gate (compile-time)

A feature pulls its own `optional = true` deps and gates a `pub mod` (`#[cfg(feature = "docs")] pub
mod document_ingest;`). Calling it without the feature is a compile error. Features compose
(`ocr = ["docs", …]`); **ship bundles are feature unions** (`full = ["code", "docs", "ocr", "lsp"]`).
> Proof-point: `sca-core` gates `document_ingest`/`ocr_ingest`/`whisper_ingest`; the CLI bundles are
> unions.

## 2. Trait registry (run-time) — the canonical shape

The **port trait lives in the owning crate's domain**; adapters `impl` it; a `Registry` holds
`Box<dyn Port>` and selects. Two registry styles — **select-one** (detection) and **broadcast**
(every plugin sees every event):

```rust
#[async_trait]
pub trait DirectiveSource: Send + Sync {
    fn name(&self) -> &'static str;
    fn detect(&self, input: &str) -> bool;
    async fn load(&self, input: &str) -> ForgeResult<DirectiveDoc>;
}

pub struct SourceRegistry { adapters: Vec<Box<dyn DirectiveSource>> }
impl SourceRegistry {
    pub fn register(&mut self, a: Box<dyn DirectiveSource>) { self.adapters.push(a); }
    pub fn detect(&self, input: &str) -> ForgeResult<&dyn DirectiveSource> {
        self.adapters.iter().map(|a| a.as_ref())
            .find(|a| a.detect(input))
            .ok_or_else(|| ForgeError::NoAdapter(input.into()))
    }
}
impl Default for SourceRegistry {           // default registration; #[cfg]-gate a heavy adapter's row
    fn default() -> Self {
        let mut r = Self { adapters: Vec::new() };
        r.register(Box::new(OpenApiSource));
        r
    }
}
```

A **broadcast** registry (`Vec<Box<dyn Plugin>>` with `on_event` hooks) fans events to all plugins;
its `register` may **reject** a plugin by manifest (e.g. `embeds_content` under enterprise mode) — the
registry enforces policy, not just storage.
> Proof-point: `said-build`'s `DirectiveSource`/`SourceRegistry`, `EditorAdapter`, and the
> `SaidPlugin`/`PluginRegistry` broadcast registry with an enterprise-mode gate.

## 3. Optional crate (compile-time, whole-subsystem)

An optional path-dep + a feature that enables it pulls a whole capability crate in/out:
`forge = { path = "../forge", optional = true }` behind `forge = ["dep:forge", "dep:tokio"]`.
> Proof-point: `said-cli` depends on `said-forge` as `optional = true`.

## Placing a new plugin

1. **Owner.** The port trait belongs in the **domain of the crate that owns the concept**, not the
   adapter.
2. **Mechanism.** Run-time peer → registry; heavy optional module → feature-gate; whole optional
   subsystem → optional crate. Compose when both apply (a heavy registry adapter is `#[cfg]`-gated
   *and* conditionally registered).
3. **Target.** A plugin doing native I/O cannot sit on the WASM compile path — feature-gate it off
   that build or isolate it behind a process boundary.
4. **Governance.** Refusals (enterprise mode, untrusted source) live in `register`, enforced once.
