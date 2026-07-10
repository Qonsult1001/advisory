# Orchestration-layer standard — one point everything connects through

When many apps and systems must integrate, the worst outcome is **point-to-point**: every app wired to
every backend, cross-cutting concerns re-implemented per integration, a backend swap touching dozens of
call sites. The cure is a **single orchestration layer** — one contract every consumer goes through,
with interchangeable backends behind it. This standard codifies that pattern, proven on the FutureBank
banking platform (`IBankingAdaptor` + Gateway/DirectTransact backends) and reproducible for any domain.

This is the **adaptor seam (recipe part 8) elevated to the system level**: not a seam *inside* one
SDK, but the seam *the whole system* is organized around.

## What the layer adds on top of the recipe

The orchestration layer **is recipe part 8 (the adaptor seam) at system scale** — so its core mechanics
are the recipe's, unchanged, applied to whole *systems* rather than one SDK's internals:

- the **≥2-interchangeable-backends gate** (part 8): no layer until a second backend is real;
- **one-line backend selection** at the composition root (`services.Add<X>Sdk(...)`): swapping is one
  line, no domain code moves;
- **cross-cutting threaded once** at that registration (part 4): correlation/retry/logging/session ride
  every call through the single point automatically.

Two things are genuinely new at system scale — they are the substance of this standard:

1. **One composite contract.** A *single* interface exposing **every** operation the domain needs
   (FutureBank's `IBankingAdaptor` combines accounts + beneficiaries + users + approvals +
   notifications). This interface **is** the layer. Assemble it from role sub-interfaces
   (`IAccountsAdaptor`, …) for readability, but consumers see one composite. Every domain service takes
   that composite and **never references a backend** — backend-agnostic, mockable through the one seam.
2. **Two axes** (see below) — backends *behind* the contract and consumer platforms *in front* of it,
   which the per-SDK adaptor seam never had to think about.

```
Apps / domain services  ─┐  each depends on the ONE contract, never a backend
                         ▼
        ╔══════════════════════════════════════╗
        ║   THE SINGLE POINT: I<Domain>Adaptor  ║   ← the orchestration layer
        ╚══════════════════════════════════════╝
                 │ selected ONCE at the composition root
        ┌────────┴───────────┬──────────────────┐
        ▼                    ▼                  ▼
   Backend A            Backend B            Mock/Fake
   (e.g. gateway)       (e.g. direct)        (offline/CI)
```

## The guard — a layer, not a dressed-up client

It is a real orchestration layer **only when ≥2 backends exist AND ≥2 consumers go through it**. With
one backend and one consumer it's a dressed-up client — apply the deletion test (from
[architecture-standard.md](./architecture-standard.md)): delete the abstraction; if a single concrete
call reappears unchanged, it earned nothing. Build the layer the moment the second backend or second
consumer is real, not before.

## Two axes: backends behind the point, consumers in front of it

Do not conflate them — a complete layer scales on both.

- **Backends (behind the contract)** — the interchangeable *systems* the one contract fronts. The
  classic case is **old-code SDK vs new-code SDK for the same domain**: e.g. a bank with a legacy
  integration (`AB`) and a rewritten one (`AfricanBank`). Both implement the *same* `IBankAdaptor`;
  an app picks one with `AddBankOrchestration(backend: AB)` vs `(backend: AfricanBank)` — **one line**,
  identical app code. Adding the new SDK alongside the old, or cutting over, never touches a consumer.
  (This is the part-8 backend gate plus one-line selection, above.)
- **Consumers (in front of the contract)** — the *client platforms* that call the one point:
  a .NET service, a web app, an Android app, an iOS app, an LLM agent. They are NOT all the same kind
  of caller, and this is the part teams get wrong:

### Reaching the single point from any platform

The orchestration layer (the contract + backends) is **one process's** composition — typically a .NET
service. A consumer in the *same* process/runtime (another .NET service) injects the contract directly.
A consumer on a *different* platform (Android/Kotlin, iOS/Swift, a browser, Python) **cannot link the
.NET layer** — it reaches the single point **over a network ingress** the layer exposes:

```
.NET service / agent ─(in-process: inject ISaidGateway/IBankAdaptor)─┐
Android · iOS · web · Python ─(over the wire: REST / gRPC / MCP)──────┤
                                                                       ▼
                                              ╔════════════════════════════╗
                                              ║  network ingress (the API) ║
                                              ║        ↓                    ║
                                              ║  THE ONE CONTRACT           ║
                                              ║        ↓                    ║
                                              ║  AB | AfricanBank | …       ║
                                              ╚════════════════════════════╝
```

So the layer earns a **network ingress** (a REST/gRPC API, and an **MCP** server for agents) the moment
a non-host-runtime consumer exists. The **native SDKs (Swift/Kotlin) and the web SDK are thin typed
clients to that ingress** — they are recipe-part-1 typed clients, NOT re-implementations of the layer.
Every platform thus connects to the *same one source*; the backend swap (AB↔AfricanBank) is invisible
to all of them because it happens behind the ingress. **A layer that only a .NET caller can reach is
half-built** — give it the ingress, then generate a thin client per consumer platform (the multi-platform
export / sdk-recipe parts 1, 10, 13).

## The reconciliation gate — grill before you generate (MANDATORY)

When the backends are **two real codebases** (e.g. an old SDK and a rewrite), they rarely expose the
*same* operations. The contract design is then a series of make-or-break decisions that the skill must
**not** make silently. Before generating a single line of the layer, **run the grilling discipline**
(the `grilling` skill: relentless, one question at a time, walk every branch, recommend an answer for
each, explore the code rather than asking what code can answer) over the scan, to pin the human down on:

- **Union vs intersection.** Is the contract every operation either backend offers (union — some ops are
  backend-specific and the other backend returns "not supported"), or only what *both* support
  (intersection — the lowest common denominator)? This single choice shapes the whole contract.
- **New-only operations.** For each operation only the new backend has (e.g. a clean ledger engine the
  legacy SDK lacks): in the contract as backend-specific, or kept *out* as a separate service the
  orchestrator calls directly? Grill each one; don't batch-assume.
- **Old-only operations.** Same question in reverse — legacy ops the new code dropped. Keep, deprecate,
  or exclude?
- **Divergent signatures.** Ops both have but with different shapes (typed DTO vs loose XML; one path vs
  a feature-flagged dual path). Which shape is the contract's truth, and where does the mapping live?
- **Parity risk.** For money-moving / irreversible ops, which backend is authoritative, and is a
  parity-test harness required before the swap is allowed?

The gate's exit criterion: **a written operation map** — every operation tagged *both* / *old-only* /
*new-only* / *divergent*, each with the human's decision recorded — before code generation begins. A
soft "look okay?" is not this gate; the grilling is. Skipping it bakes silent assumptions into every
operation of the contract.

## Same shape, many domains (why it's reusable)

The pattern is domain-agnostic. `.said` instantiates it with `ISaidGateway` (search/ask/remember/…) over
{MCP backend, HTTP backend, fake}; banking instantiates it with `IBankAdaptor` over {legacy `AB` SDK,
new `AfricanBank` SDK, sandbox}. **Different codebases, different contracts, different backends — the
same composite contract over the same two axes.** That sameness is the point: generate each from this
one standard rather than re-architecting per domain. (A **BFF composition layer**, recipe part 12, may
sit *in front of* the orchestration layer — composing several of its calls into one business op — and
consumes it like any other consumer.)

## Completion criterion

The orchestration layer passes when: there is **one composite contract** every consumer depends on;
**≥2 interchangeable backends** implement it; an app **selects a backend in one registration line** and
swapping it touches no domain code; **cross-cutting is wired once** at that registration and provably
threads every call; and **no consumer references a backend type**. A maintainer can answer "what changes
to swap the backend?" with "one registration line," and "where do I add a cross-cutting concern?" with
"the layer's registration, once."
