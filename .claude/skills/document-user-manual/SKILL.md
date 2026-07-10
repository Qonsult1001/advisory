---
name: document-user-manual
description: Author a production-grade, world-class user manual for a CLI and/or an MCP server — separate manuals, lifecycle-first (create → import → align → query), exhaustive command/tool coverage, enterprise concerns. Use when the user wants a user manual, end-to-end usage docs, a getting-started-to-production guide, or to document every CLI command / MCP tool.
---

# Document — user manual

Author a **production-ready user manual** for **any system's** CLI and/or MCP surface, the way a
shipped product ships one: a reader goes from nothing to a working, production-configured system
without leaving the page. This skill is the **method**, system-agnostic — the system being documented
(its commands, tools, modes, data model) is discovered in step 1, never assumed. Examples below name a
generic memory/data system for illustration only; substitute the real system's vocabulary.

CLI and MCP get **separate manuals** — they are different surfaces with different entry, dispatch, and
state models — each grounded in the **real command/tool surface**, not the source's aspirations.

The spine is the **lifecycle**: every manual is organised as a journey — *create → import → align →
operate → query → maintain → harden* — not an alphabetical command dump. A command dump is reference;
a manual is a path a reader walks. (The stage *names* adapt to the system: a system with no "import"
step skips it; one with deployment as its hard part weights that.)

## Author from the real surface, never from memory

The cardinal rule: every command, flag, tool, and argument documented must be **verified against the
actual built surface** — the CLI's real `--help` / command enum, the MCP server's real `tools/list`.
Where existing docs disagree with the live surface (a doc claiming fewer tools than the server
advertises, a flag that was renamed), the **live surface wins** and the discrepancy is itself worth
documenting. A manual that documents commands the binary doesn't have is worse than no manual.

## Workflow

### 0. Settle the scope — variant *and* capability area

Scope has **two axes**; ask the user about both before documenting:

- **Build variant** — a system may ship several compiled variants (feature bundles / targets /
  profiles), each with a different surface. Discover the set (CI matrix / build manifest / Dockerfile /
  source gates) and **if several exist, ASK which to document** (one manual per variant, or one).
- **Capability area** — even within one variant, the user may want only **part** of the surface (e.g.
  "just the memory/brain commands, not code-indexing"). **ASK: whole surface, or a specific area?** If a
  subset, get the exact capability area and document only its commands/tools.

**Completion criterion: both axes are settled with the user — the variant set is chosen, and the
capability scope is either "whole surface" or a named subset. The manual covers exactly that scope, no
more, no less; commands outside it are not documented (and, if relevant, a one-line "see the X manual"
pointer is left).**

### 1. Enumerate the real surface (within scope)

For the CLI: read the actual command surface (the command enum / `--help` for every subcommand + its
flags). For MCP: read the real `tools/list` and each tool's input schema. **Restrict the inventory to
the scope chosen in step 0** (the selected variant, and the capability area if a subset was chosen).
Reconcile against any existing docs; the live surface is authoritative. **Completion criterion: a
complete inventory exists for the chosen scope — every in-scope CLI command + flag, or in-scope MCP tool
+ argument — each traced to the real surface, with any doc-vs-reality discrepancy noted. A command/tool
you couldn't verify is flagged, not silently documented.**

### 2. Map the lifecycle journey

Order the inventory into the lifecycle path, not alphabetically: **create** a base · **import / ingest**
data · **align** (configure, tag, index) · **operate** (the day-to-day verbs) · **query / retrieve** ·
**maintain** (compact, sync, snapshot) · **harden** (the enterprise section, below). Each command/tool
lands in exactly one lifecycle stage; a few appear in two only if genuinely used at both. **Completion
criterion: every inventoried command/tool is assigned to a lifecycle stage, and each stage has at least
its create→query happy path covered end-to-end.**

### 3. Write each stage as a runnable walkthrough

For each stage, write the prose + **a real, copy-pasteable example** (an actual command line / an
actual tool call with real arguments) and **the observable result** a reader should see (output, exit
code, state change). A reader who pastes the example must get the shown result. If the system has a
**unit-of-state** a user creates and manages (a database, a file, a workspace, a project), cover
explicitly **when to create one vs several** and how it relates to imports/exports/snapshots — this is
the question users get wrong most. Write in the **user's voice**: task-oriented ("to import a folder…"),
domain language from `CONTEXT.md`, no internal implementation on the page. **Completion criterion:
every lifecycle stage has at least one runnable example with its expected observable; the
create→import→align→query path is demonstrated as one continuous, reproducible sequence.**

### 4. Document the production / enterprise layer

A production manual is not done at the happy path. Cover, as their own sections, **every enterprise
concern the system actually exposes** — document the ones present, skip the ones that don't apply.
Sweep this checklist against the real surface: **modes / tiers** (and what each forbids),
**encryption / signing**, **audit trail** (how to read and verify it), **access control / roles**,
**retention / data-lifecycle / legal hold**, **backup & restore**, **deployment / install**, and
**configuration** (every config key, its default, where it lives). **Completion criterion: every
production capability the surface exposes has a section; nothing security-, compliance-, or
deployment-relevant is left to "see the code".**

### 5. Add the reader's safety net

Close each manual with the parts that make it *usable under pressure*: a **troubleshooting** table
(symptom → cause → fix), an **exit-code / error-shape** reference, a **glossary** of the domain terms
(reuse `CONTEXT.md` / the project's ubiquitous language — don't reinvent names), and a **quick-reference
card** (the whole surface on one screen). **Completion criterion: troubleshooting, error reference,
glossary, and quick-reference all present; a reader hitting a failure can self-serve a fix from the
manual alone.**

### 6. Verify the manual against the real system

Before declaring done, **run a sample of the documented examples against the real surface** and
confirm each produces the shown observable. Fix any drift. **Completion criterion: a representative
sample of examples across all lifecycle stages has been executed and matches the manual; the manual is
evidence-backed, not asserted.**

## Output

Two separate files, comprehensive depth, in the project's docs home (e.g.
`docs/manual/CLI-USER-MANUAL.md` and `docs/manual/MCP-USER-MANUAL.md`). Use the section skeletons:
[reference/cli-manual.md](./reference/cli-manual.md) · [reference/mcp-manual.md](./reference/mcp-manual.md).

## What this is not

- Not API/source reference docs (that's generated from code) — this is the **human journey**.
- Not the testing skills — `/verify-cli` and `/verify-mcp` *prove* the surface works; this *teaches* a
  user to use it. (They share the "enumerate the real surface" first move — reuse that inventory.)
