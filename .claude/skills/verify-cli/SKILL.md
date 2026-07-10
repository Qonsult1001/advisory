---
name: verify-cli
description: Drive the real built CLI binary end-to-end and verify production behaviour — build, run actual commands, assert on real output/exit codes/side effects. Compiles and tests EACH shipped build variant (feature bundle / target / profile) against its own scenario list, because each variant has a different command surface. Use when the user wants to test the CLI for real, do production/E2E CLI testing, verify a release binary, or smoke-test a built command (not unit/integration tests).
---

# Verify CLI

Drive the **real built binary** end-to-end and judge it by what a user would see: stdout, exit code,
files written, state changed. The **gate is the real binary's observable behaviour**, nothing else.
Where `/tdd` proves the *logic* in-process, `verify-cli` proves the *product* — compiled binary, real
argv, real filesystem, real exit codes. For production smoke tests, release verification, and "does
the actual command actually work end-to-end."

## The build-variant axis — a CLI is often not one binary

Many projects ship the CLI in **several build variants** — feature bundles (`--no-default-features` +
a feature set), build targets (platform / arch / `wasm`), or profiles — and **each variant has a
different command surface.** A compile-gated command (e.g. `#[cfg(feature = …)]`, build tags) **does
not exist** in a variant lacking its gate; a runtime-gated command **exists but no-ops** without its
data/feature. Testing one binary can't verify the product: the fullest variant hides "command wrongly
present in the minimal variant" bugs; the minimal one can't exercise the heavy capabilities. So this
skill tests **each shipped variant separately, against its own scenario list** — and a project that
ships exactly **one** binary is just the trivial case (one variant).

**Discover the variant set — don't assume it.** Re-derive the variants and each one's exact build
command **from the project every run** (the repo drifts; never trust memory or an example table). Look,
in order, for whatever the project actually uses:

1. **CI release workflow** — the build matrix is the authoritative variant list + build command
   (`.github/workflows/*.yml`, `.gitlab-ci.yml`, `azure-pipelines.yml`, a `Jenkinsfile`).
2. **Build manifest** — the feature/target definitions (`Cargo.toml [features]`, `package.json`
   scripts, `go.mod` + build tags, a `pyproject.toml`).
3. **Packaging / release config** — `Dockerfile`(s), `goreleaser.yml`, a `Makefile`/`justfile` release
   target, install scripts.
4. **Gating in source** — what's compile-gated (`grep` the gate idiom: `#[cfg(feature`, build tags,
   `process.env`-switched commands) → which commands are variant-specific.

**If none of these exist or they don't define multiple variants, ASK the user**: "How is this CLI
shipped — one binary, or several variants (feature bundles / targets / profiles)? What's the build
command for each?" Do not invent variants, and do not silently fall back to a default build. Treat a
confirmed single-binary project as one variant and proceed.

For each discovered variant, record **what it uniquely adds** — the capability you must exercise
*because the lighter variants can't* — and **what it must NOT contain** (commands behind gates this
variant doesn't enable; that absence is itself a scenario to assert).

> *Illustration only (a real project, `said`): four feature bundles — `brain` (text memory) →
> `coding` (+ source indexing) → `coding-plus` (+ LSP commands) → `full` (+ PDF/DOCX/OCR ingest), each
> `--no-default-features`. This is an example of the shape, not a set to assume — discover the real one.*
>
> ⚠️ A plain default build (e.g. `cargo build` / `npm run build` with no variant flags) is usually
> **not** a shipped artifact. Verifying it means verifying something no user downloads — build the real
> variants the release pipeline ships.

## Workflow

### 0. Pick the variants to verify

Enumerate the current shipped variants from the sources above, then **ask the user which to verify this
run** — present each with what it uniquely exercises, and let them pick one, several, or all. Assume no
default. **Completion criterion: the user has chosen an explicit set of variants, and for each you hold
its exact build flags read from the CI workflow this run.**

### 1. Build the real artifact — once per selected variant

Build each selected variant with its discovered build command (release profile), fresh this run. Record
`{variant → absolute binary path}`. **Completion criterion: every selected variant built successfully with its path
recorded; if any build fails, stop and report that variant's build error — never fall back to a different,
default, or stale binary.**

### 2. Establish a SEPARATE scenario list per variant

Each variant gets its **own self-contained list** — derive it from that variant's real surface (the CLI's own `--help` *on that variant's binary*), with no shared core assumed. Each list MUST include:

- **Positive scenarios** for every capability the variant is meant to deliver — especially the one it
  uniquely adds (the capability the lighter variants can't exercise).
- **Negative-gating scenarios** — a command that should be **absent** in this variant exits non-zero
  with "unrecognized subcommand" (the commands behind gates this variant doesn't enable).
- **Error paths** — bad subcommand, missing required argument.

Confirm each list with the user; prioritise critical paths. **Completion criterion: each selected
variant has its own list where every scenario names a concrete `binary args…`, an expected exit code, and
an expected observable (stdout substring / file created / state changed), and includes its
negative-gating scenarios — vague scenarios ("check it works") are not done.**

### 3. Run each variant's scenarios against ITS binary

Run each list with its own variant's binary, in an **isolated working dir per variant** (temp dir / fixture
copy, never the live project, never state shared between variants). Capture **exit code +
stdout/stderr + any file/state change** per scenario. Treat each run as a **tracer bullet**: run one,
read what actually happened, let it inform the next. **Completion criterion: every scenario has a
captured actual result; none skipped silently — one you couldn't run is reported and tagged with its
variant, not omitted.**

### 4. Judge against the expected observable

Compare each actual result to its expected observable — **the binary's behaviour is the sole judge**,
not your reading of the source. A pass is exit-code AND output AND side-effect matching; a mismatch on
any is a fail. A capability absent because the variant doesn't ship its feature is **not a fail** — it
fails only if the variant is *supposed* to deliver it. **Completion criterion: every scenario marked
pass/fail against its observable, actual output quoted for any fail, each result attributed to its
variant.**

### 5. Report per variant, and capture failures as issues

Produce **one results table per variant** (scenario · command · expected · actual · pass/fail), plus a
cross-variant summary — which variants fully passed, and any capability that regressed in one variant but
not another. File the **fails** to **`/qa`'s issue standard** (its template, the single-issue-vs-breakdown
decision, no file paths/line numbers, project domain language — that standard lives in `/qa`, don't
restate it here), **naming the variant in the title** so the build reproduces, or hand the list to `/qa`
for a conversational pass. **Completion criterion: a per-variant table exists and every fail is filed to
that standard (variant named) or explicitly handed off — never left only in chat.**

## Checklist per run

```
[ ] Variant set + build flags re-derived from the project (CI / manifest / user) this run, not memory
[ ] Each selected variant built fresh; absence ≠ fail unless that variant should deliver it
[ ] Each variant ran its OWN list (incl. negative-gating) against ITS binary, in a per-variant dir
[ ] One results table per variant; every fail quoted, attributed, and filed
```

## Boundaries

It **verifies and reports** — it does not fix (failures become issues; fixing is `/implement` or
`/tdd` after). A green smoke pass **complements** the integration regression net, never replaces it.
This skill verifies the **CLI**; use `/verify-mcp` for the project's MCP server (discover its variants
the same way).
