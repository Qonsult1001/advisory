# Reference — documented claims → scale gates

The pass/fail bar is whatever `docs/said-structure/10-benchmarks/` claims. **Re-read those docs each
run** and rebuild this mapping — the numbers below are the snapshot at authoring time and the docs drift.
A stale threshold turns a real regression green.

## The gate is @10

Everything the docs *report and block on* is at depth 10: **NDCG@10** for document retrieval, **R@10**
for conversational/recall. top-1 appears as the achieved precision ("all 500 queries top-1"); top-50 is
an internal candidate-retrieval stage, **never a reported result**. So: gate at @10, report @1/@5 as
nuance, ignore top-50 except as an engine-internal detail.

## Claim → gate (snapshot — verify against live docs)

| Documented claim (source) | Capability | Gate (this run, re-read it) |
|---------------------------|-----------|------------------------------|
| WikimQA NDCG@10 = 1.00000, "all 500 queries top-1" (`mteb.md`) | MTEB doc retrieval | NDCG@10 ≥ ~0.94 (harness `TaskSpec.target` 0.93983); doc headline 1.0 |
| SummScreenFD NDCG@10 = 0.97974, "329/336 top-1" (`mteb.md`) | MTEB doc retrieval | NDCG@10 ≥ 0.96586 (harness target) |
| Needle NDCG@10 = 1.00000 (`mteb.md`) | NIAH needle/passkey | NDCG@10 = 1.0 (must stay perfect) |
| "MTEB LongEmbed must stay at 1.0 / 0.98+ / 0.97+ on Needle / WikimQA / SummScreenFD" (`README.md`) | release gate | Needle 1.0, WikimQA 0.98+, SummScreenFD 0.97+ |
| LoCoMo "Overall R@10 = 0.554", "must stay at 0.554 R@10 or improve" (`locomo.md`, `README.md`) | conversational recall | R@10 ≥ 0.554 |
| BEIR "within ±0.01 NDCG@10 of the prior measurement" (`beir-short-doc.md`) | short-doc non-regression | NDCG@10 within ±0.01 of prior per task |
| Chamber pass-rate "must stay at 30/30 or improve" (`README.md`, `chambers.md`) | deterministic property fixtures | 30/30 chambers pass |
| Competitor matrix logs R@10 + F1 + p50 + p99 (`competitor-matrix.md`) | perf + quality vs competitors | report p50/p99; R@10 per above |

## Reading the gate correctly

- **Two numbers per MTEB task**: the prose headline (e.g. WikimQA 1.0 = top-1 hit rate) and the harness
  `TaskSpec.target` (NDCG@10, e.g. 0.93983). The **NDCG@10 target is the gate**; the headline is the
  nuance. When they differ, gate on NDCG@10.
- **Tolerance is part of the claim.** BEIR states ±0.01; LoCoMo states "or improve" (one-sided). Apply
  the doc's own tolerance — don't impose a tighter or looser one.
- **"Must stay at X or improve"** = one-sided gate: below X fails, above X passes.
- **No documented claim for a capability you loaded** → report the measured @10 as a *baseline*, label
  it "no documented target", and do not mark pass/fail. Inventing a bar is the failure this file exists
  to prevent.

## Latency / throughput

The docs log p50/p99 latency (competitor matrix) but state few hard latency *gates*. Treat latency and
ingest throughput as **reported baselines trended against N**, and gate only where the docs give an
explicit number. If a load run shows latency degrading super-linearly with N, that is a finding worth
filing even without a documented gate — flag it as a scaling regression, not a pass/fail miss.
