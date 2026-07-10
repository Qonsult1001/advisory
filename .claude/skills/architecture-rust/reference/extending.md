# Extending — add a format / language / provider with zero fuss

The enterprise bar is **no-fuss extensibility**: a new instance is *one entry you drop in* — a
feature, a module, or a registry row — picked up automatically, **no edit to core dispatch**. The
test: *"to add the next one, how many existing functions do I edit?"* World-class = **zero**. If the
answer is "edit the `match`/the dispatcher", the seam is missing — convert it to a registry first,
*then* add the instance. This is the single most important rule on this page.

`said-build`'s `grammars.rs` header records the migration that achieves it: *"Adding a language used
to mean editing `match ext {…}` arms in 5 files. Now it's one line here."* That registry is the bar.

> ### Reality check — `said-build` today (verify your state before trusting a recipe)
>
> The target pattern exists but isn't propagated:
>
> | Extension point | State | Cost to add one |
> |-----------------|-------|-----------------|
> | language registry (`grammars.rs::register_languages`) | ✅ clean `Vec<LanguageSpec>` | one row |
> | language lookup (`code_search.rs::language_for_ext`) | ⚠️ a **second `match`** duplicating the registry (C# in 2 places) | edit the match **too** |
> | doc/pdf formats (`document_ingest.rs`) | ⚠️ a `DocFormat` **enum + 3 `match`es**, all-or-nothing under `docs` | edit **3 arms** |
>
> So "just add a feature" is **not** one-line until the dispatcher is registry-driven. Check which
> state you're in first.

## Recipe A — add a format (pdf, docx, …): feature-gated module

1. **Declare a feature** that pulls only its own `optional = true` deps; features compose
   (`ocr = ["docs", …]`). The pure part and the native part are **separate features** so the WASM
   build can take the pure one (`docx` = zip+xml is wasm-safe; `docs` adds native `pdfium`).
2. **Add the module gated** — `#[cfg(feature = "docs")] pub mod document_ingest;` is the *only* line
   in core that changes.
3. **Implement the entry fn** behind the same gate.
4. **Make routing data-driven** — replace the `DocFormat` enum + 3 matches with a registry row per
   format, so adding ODT/EPUB is one row + one dep, not three match edits:

```rust
struct FormatSpec { name: &'static str, extensions: &'static [&'static str],
                    ingest_tag: &'static str, ingest: fn(&Path) -> IngestResult<Vec<DocSegment>> }
fn register_formats() -> Vec<FormatSpec> { vec![ /* one row per format */ ] }
```

## Recipe B — add a language (C#, Java, …): registry row

A language is **one row** — `name + extensions + grammar fn` — in the `Vec<LanguageSpec>` registry.
Extension lookup and AST chunking read the `Vec` and never name a language, so they don't change.

To make a language **its own feature** (so a build ships only the languages it wants), gate the
grammar dep (`lang-csharp = ["dep:tree-sitter-c-sharp"]`) and the row (`#[cfg(feature = "lang-csharp")]
v.push(LanguageSpec{…})`); keep `code` as the "everything" union.

**Two migrations `said-build` needs first:** (1) make `language_for_ext` *read the registry* instead
of being a duplicate match — so a language lives in one place; (2) split the coarse `code` feature
into per-`lang-*` features. Until both land, "add C# as a feature" is a row **plus** a match arm.

## Recipe C — add a provider/source/adapter: registry entry

A run-time peer is a **trait registry** entry, not a feature. Add-point = `impl` the existing port in
a new adapter module, then one `registry.register(Box::new(NewProvider::from_config(cfg)?))`.
Selection (`detect`/by-name/broadcast) iterates the registry and never names the provider, so adding
one changes nothing downstream. Governance (refuse a peer under enterprise mode) lives in `register`,
enforced once. Heavy deps → gate the registration with `#[cfg]`.

Mechanism details and full registry shapes: [plugins.md](./plugins.md).
