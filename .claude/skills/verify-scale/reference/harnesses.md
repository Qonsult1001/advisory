# Reference — scale measurement engines

The two engines `verify-scale` runs. Re-confirm the command surface from the project each run (examples
get renamed); this catalogue is the starting map, not the source of truth.

## 1. Gold-standard harnesses (reference quality numbers)

Library examples under `crates/sca-core/examples/`. Built `--features static-embed`, with the
`said-lam-static` encoder folder adjacent (or use a bundle that bakes it in). Each prints a grep-able
JSON/score line at the end.

| Capability | Command | Produces |
|------------|---------|----------|
| Document retrieval (MTEB LongEmbed) | `cargo run --release -p sca-core --example mteb_rust --features "static-embed" -- --tasks <TASK>` | NDCG@10 per task |
| All MTEB tasks at once | `… mteb_rust … -- --tasks all` | NDCG@10 for each |
| Conversational recall (LoCoMo) | `cargo run --release -p sca-core --example locomo_baseline --features "static-embed"` | R@10 |
| Real-world train-of-thought recall | `cargo run --release -p sca-core --example realworld_recall_probe --features "static-embed"` | top-K recall over probe queries |
| Short-doc non-regression (BEIR) | the BEIR slice harness (see `docs/said-structure/10-benchmarks/beir-short-doc.md`) | NDCG@10 per task |
| Plain recall sanity | `cargo run --release -p sca-core --example baseline_recall --features "static-embed"` | baseline recall |

MTEB `--tasks` values (from `mteb_rust.rs`): `LEMBWikimQARetrieval`, `LEMBSummScreenFDRetrieval`,
`LEMBNeedleRetrieval`, `LEMBQMSumRetrieval`, or `all`. Each task carries its own target in the source
(`TaskSpec.target`) — read it there, it is the authoritative reference figure.

> These measure the **library**. Passing here is necessary but not sufficient — step 2 must reproduce the
> number on the **shipped product**.

## 2. Real shipped product at scale (what users run)

Drive the actual CLI binary and MCP server, exactly as `/verify-cli` and `/verify-mcp` do (real argv /
real JSON-RPC over stdio), built per the variant's real release flags. Load N memories, then query.

- **Ingest at scale** — loop the product's add path: CLI `said --path <brain> add "<text>" --id <id>`
  (or `remember`), or MCP `tools/call remember`. Time the loop for throughput; check the brain's
  `stats` / `status` reached N.
- **Query at scale** — fire the query set through the product's retrieval surface: CLI `said ask "<q>"`
  / `said query "<q>" --top 10`, or MCP `tools/call search`. Parse the ranked doc_ids to score recall.
- **Isolation** — a fresh brain/state file per variant in a temp dir; never the live project, never
  shared between variants (per the verify-* isolation rule).
- **Scoring** — for each query you need a known-correct doc_id (the gold label). Compute hit-rank, then
  @1 (rank == 1), @5 (rank ≤ 5), @10 (rank ≤ 10). Aggregate R@10 = fraction of queries with the gold in
  top-10; NDCG@10 where the harness defines graded relevance.

## Scoring depth — the gate vs the nuance

- **Gate: @10** — NDCG@10 (document retrieval) and R@10 (conversational/recall). This is the depth the
  docs report and block on.
- **Nuance: @1 and @5** — report both. @1 is the de-facto precision the docs cite ("all 500 queries
  top-1"); @5 is a stricter-than-documented bonus view. Useful to localize a regression (still in top-10
  but fell out of top-1 = ranking drift), but they are **never** the pass/fail line — @10 is.

## Synthetic load data

If you generate N synthetic memories rather than use a labelled dataset, each must carry a known gold
query→doc_id mapping so recall is scorable, and the data shape must mirror the real capability (short
notes for brain memory; functions/symbols for code recall). **Say in the report that the data is
synthetic** and how it was generated — silent synthetic data reads as a real-corpus result.
