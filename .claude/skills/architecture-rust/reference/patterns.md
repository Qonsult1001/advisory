# Enterprise patterns — recurring structures, generically

The battle-tested structures for the recurring problems an enterprise Rust system hits: audit,
dedup, versioning, plugin dispatch, multi-surface. Each is a **generic shape** — reach for it by
name when the problem appears; spot its anti-pattern in review. These are language/domain-neutral:
the shape applies to a brand-new MCP-only tool, an importer service, or a large workspace alike.

Proof-points cite where a real system (`said-build`) uses the pattern — **evidence it works, not a
spec to copy.** Never lift a proof-point's *internals* (byte layouts, field orders, crypto schemes,
math) into code or into this skill; lift the *shape*.

Each entry: **the shape · when to use · the leak to avoid · proof-point.**

---

## 1. Append-only hash-chained log

**Shape.** Entries are never mutated. Each carries a hash over the previous hash plus its own fields;
verification walks the chain and reports the first break.

```rust
struct Entry { seq: u64, /* fields… */, hash: [u8; 32] }
// hash_n = H(hash_{n-1} ‖ seq_n ‖ …fields_n);  hash_0 over a zero seed.
fn verify(entries: &[Entry]) -> Result<(), u64> { /* recompute; first mismatch = break point */ }
```

**When.** Audit trails, event logs, anything that must be tamper-evident without a key server.
**Leak to avoid.** Mutable audit rows; a `deleted` boolean instead of an append; recomputing the whole
hash instead of chaining (no tamper localisation).
**Proof-point.** `said-build`'s `AUDT` audit section + `said-vault`'s event chain.

---

## 2. Content-addressed store

**Shape.** The key *is* the content hash. `put` is idempotent — a colliding key is a no-op (the bytes
are already there).

```rust
fn put(&mut self, bytes: &[u8]) -> Key {
    let key = hash(bytes);
    if self.has(&key) { return key; }   // dedup for free
    self.write(&key, bytes); key
}
```

**When.** Dedup of blobs/frames/assets; "have I already ingested this file?" checks; immutable storage.
**Leak to avoid.** Dedup logic copy-pasted into every caller instead of living at the store boundary;
a separate "exists?" round-trip racing the write.
**Proof-point.** `said-vault`'s `put_object` (BLAKE3 key, idempotent on collision); frame dedup in
`sca-core`.

---

## 3. Section table / offset registry

**Shape.** A small header at offset 0 holds offsets to each section. Readers jump via the table and
**skip sections they don't recognise** — so new sections are added without breaking old readers.

```rust
struct Header { magic: [u8;4], version: u16, flags: u16, offsets: [u64; N] /* one per section */ }
// open() = read header, index by offset; an offset of 0 = "section absent", degrade gracefully.
```

**When.** Any file/wire format that must evolve; plugin manifests; anything versioned that old code
must still load.
**Leak to avoid.** Hard-coded sequential layout that a version bump breaks; no "absent section"
sentinel; readers that fail instead of degrading on an unknown section.
**Proof-point.** `said-build`'s v7_1 header (offset registry) + magic-scanned late sections (MODE,
AUDT) added with no header change.

---

## 4. Version chain + two-phase delete

**Shape.** An update never overwrites: it links the old record to its replacement and marks the old
one superseded. Deletion is two-phase — soft (recoverable) then physical (reclaimed) — gated by holds.

```rust
enum Status { Active, Tombstone, Deleted }
struct Rec { /* … */, superseded_by: Option<Id>, status: Status }
// update: old.status = Tombstone; old.superseded_by = Some(new.id); new.status = Active.
// reclaim: drop Tombstones → Deleted → compact, UNLESS a hold tag blocks it.
```

**When.** Anything needing point-in-time restore, lineage, or compliance retention.
**Leak to avoid.** Destructive in-place update (no restore path); single-phase delete that reclaims
immediately (no recovery window); a reclaim sweep that ignores legal/retention holds.
**Proof-point.** `said-build`'s `superseded_by` lineage + Active→Tombstone→Deleted + `legal_hold:*`
tag blocking reclaim; byte-exact checkout.

---

## 5. Single-source-of-truth registry (prompts / config / strings)

**Shape.** One module owns the canonical strings/values. Every surface (CLI, MCP, WASM, API) *imports*
them — none inlines its own copy. The crate version moves when the content changes, so a bisect
fingers the change.

```rust
// one crate owns it:
pub fn system_prompt(role: Role, ctx: &Ctx) -> String { /* the only definition */ }
// every surface calls it; WASM exports the SAME fn to JS, MCP serves it, CLI embeds it.
```

**When.** Prompts, policy text, config defaults, schemas — anything that must stay identical across
surfaces and be auditable in one place.
**Leak to avoid.** The same prompt/config copy-pasted into CLI **and** MCP **and** WASM (they drift);
strings scattered inline where no one can audit or version them.
**Proof-point.** `said-prompts` — one crate, exported to the WASM browser agent, the MCP server, and
direct Rust callers from the same source.

---

## 6. Thin surface over shared core

**Shape.** A delivery surface (Ring 3) does **parse → call a capability → format**. No business logic.
Multiple surfaces wire the *same* capability with zero reimplementation.

```rust
// surface handler, in full:
let cmd = parse(input)?;
let out = capability.do_it(cmd).await?;   // the ONLY substantive line
Ok(format(out))
```

**When.** Every CLI command, MCP tool, HTTP handler, WASM binding.
**Leak to avoid.** A handler that grows hundreds of lines of domain logic (it belongs in the
capability crate); two surfaces re-implementing the same operation; injecting policy/governance at the
surface edge instead of in the capability.
**Proof-point.** `said-build`'s CLI and MCP are both thin relays over `sca-core` (verified zero
duplication) — *except* `init`/`snapshot` (≈1000 LOC of capability logic in the CLI) and a
`BOUNDARY.md`-constitution injection at the MCP edge: the skill cites these as the **leak to flag**,
not to imitate.

---

## 7. Registry-not-match dispatch

**Shape.** Swappable things live in a list, not a growing `match`. Compile-time peers (languages,
formats) → a static `Vec<Spec>` (data). Run-time peers (providers, sources) → a `Vec<Box<dyn Port>>`.
Adding the Nth one is **one row**, never an edit to a dispatcher.

```rust
struct Spec { name: &'static str, extensions: &'static [&'static str], make: fn() -> T }
fn registry() -> Vec<Spec> { vec![ /* one row per peer; lookups read this Vec */ ] }
```

**When.** Anything you'll add more of: languages, ingestion formats, providers, tools, adapters.
**Leak to avoid.** A `match ext { … }` (or a tool-name `match`) that every new peer must edit; the
*same* peer listed in two places (a registry **and** a duplicate match) so adding one means editing
both. See [extending.md](./extending.md) for the full add-a-language / add-a-format recipes.
**Proof-point.** `said-build`'s `grammars.rs` registry (the good shape) — and the duplicate
`language_for_ext` match + match-driven format/tool dispatch the skill flags as the migration target.

---

## 8. Separate-process sidecar for the heavy/online dependency

**Shape.** A dependency that's heavy, online, or licence-divergent does **not** link into the shipping
(or WASM) binary. It runs as its own process behind a port — installable separately, absent by default.

```rust
// the core binary speaks to it over a boundary (IPC / a port trait), never `dep`s it:
trait ThinkPort { async fn distill(&self, frames: &[Frame]) -> Result<Summary>; }
// the sidecar is a separate crate/binary that implements the client side.
```

**When.** LLM/network calls, GPU work, anything that would compromise an offline/WASM/audit guarantee.
**Leak to avoid.** An LLM or network call compiled into a binary that's meant to be offline or
WASM-safe; a "plugin system" where the right answer is one named sidecar process.
**Proof-point.** `said-build`'s rule — the shipping binary never calls an LLM; `said-think` is a
separate MCP-client process. This *is* the WASM/offline constitution (see
[../CONVENTIONS.md](../CONVENTIONS.md) §Constitution).

---

## Using these

- **Reviewing a diff:** match the code against the relevant pattern; the "leak to avoid" line is the
  finding. Several of these feed the violation table in [CONVENTIONS.md](./CONVENTIONS.md).
- **Designing something new** ("I need an audit log / a dedup store / a plugin dispatch"): take the
  shape, not a blank page. Adapt the shape; never copy a proof-point's internals.
- **The patterns compose.** A compliance store is often #1 (audit) + #2 (content-address) + #4
  (version chain) at once — `said-vault` is exactly that stack.
