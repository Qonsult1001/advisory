# First-Attempt Build Failure — Root Cause & Fix

**Date:** 2026-06-15 · **Area:** `GroqCycle.ProduceChangeAsync` · **said:** 0.7.0 (stable)

## Symptom

Every mutation cycle failed its **first** build/test attempt, then recovered on
the self-repair retry (or, for hard cases, exhausted retries and the gate
correctly blocked the PR). The cycle still produced correct PRs, but always paid
a wasted ~30s first build, and harder tickets failed outright.

## Root cause (pinpointed from real errors)

The first attempt is Groq's only **blind** guess: it had the plan and a real
endpoint anchor, but **not** the real test fixture or the exact .NET API surface.
So its first change-set referenced members/fields that **don't exist in this
codebase** — which parse fine (so `said edit`'s syntax-gate passes them) but do
not **compile**. Only the in-clone `dotnet build`/`test` catches them — by
definition, on the first attempt.

Captured live (the diagnostic logging from PR #119 made these visible per-run):

| Run | First-attempt error | What Groq guessed wrong |
|-----|---------------------|--------------------------|
| #118 | `error CS0103: The name '_factory' does not exist in the current context` | Guessed the test fixture field name (real one is `_client`) |
| #118 | `error CS1061: 'GCMemoryInfo' does not contain a definition for 'TotalAllocatedBytes'` | Called a .NET API member that doesn't exist |
| #112 | `error CS1739: ...GetTotalAllocatedBytes... no parameter named 'forceFullCollection'` | Invented an API parameter |

Reading the real fixture (`tests/Advisory.Tests/HealthTests.cs`) confirmed it: the
class uses `IClassFixture<WebApplicationFactory<Program>>` with a `_client` field
(`factory.CreateClient()`), and tests call `_client.GetAsync(...)` +
`JsonDocument.Parse(...)`. Some generated tests even spun their **own**
`new WebApplicationFactory<Program>()` because they couldn't see the existing one.

**It is not a `.said` bug** — `said edit` is structural; type/semantic errors are
the compiler's job, and the gate does catch them. The gap was the **prompt**: it
said "use the existing fixture pattern" without ever **showing** it.

## The fix

`RealTestFixture()` reads the real `HealthTests.cs` from the source mount and
injects, into Groq's execution prompt:

- the existing `using`s + class declaration + constructor (so the model sees the
  `_client` field and how it's obtained), and
- one canonical sample `[Fact]` (so it copies the real call shape).

The system instruction now explicitly says: use the existing `_client` field; do
**not** declare a new field/constructor or `new WebApplicationFactory`; do not
re-add usings already present; only call .NET APIs you are certain exist.

This mirrors the existing `RealEndpointAnchors()` helper (which reads real
`app.MapGet(...).AllowAnonymous();` lines so the model copies a real anchor
instead of hallucinating one). Same principle: **feed the model the real source
it would otherwise guess.**

## Evidence the fix works

Before the fix: **every** run failed attempt 1. After the fix:

| Run | Endpoint | Result | Repairs |
|-----|----------|--------|---------|
| #120 → PR #121 | `/api/logical` | first-attempt GREEN | 0 |
| #122 → PR #123 | `/api/is64bit` | first-attempt GREEN | 0 |

PR #121's generated test used `_client.GetAsync("/api/logical")` — the real
fixture field, exactly as intended (no `_factory`, no self-spun factory).

## Honest boundary

This removes the **fixture-guessing / unknown-field** class of first-attempt
failures (`CS0103` on `_client`). It does **not** guarantee zero first-attempt
failures: a ticket that needs a genuinely unfamiliar .NET API (e.g. the GC
`TotalAllocatedBytes` surface) can still trip `CS1061`/`CS1739` on the first try —
the model can't be fed an API it doesn't know. For those, the self-repair loop and
the build/test gate remain the backstop (and a hard API stays blocked from a PR,
which is correct). The fix targets the **common, recurring** shape (endpoint +
fixture test), which is the bulk of the cycle's tickets.

## Related

- Diagnostic logging that surfaced the real errors: PR #119 (`FirstErrorLine`).
- The analogous anchor fix: `RealEndpointAnchors` (see `said-edit-handoff.md`).
- Deferred fix-replay (would skip the LLM entirely for recurring shapes, blocked
  on a said indexing bug): `docs/superpowers/specs/2026-06-15-fix-replay-groqcycle-design.md`.
