---
name: document-walkthrough
description: Write step-by-step walkthroughs for any system's CLI / MCP / orchestrator — a hand-held beginner tutorial (zero → first success) plus task-oriented how-to guides (one per real goal). Use when the user wants step-by-step usage docs, a getting-started guide, a how-to, a "show me how to use it" walkthrough, or onboarding docs anyone can follow.
---

# Document — walkthrough

Write the docs that teach a person to **use** a system by *doing*, not by reading a command list.
Global and system-agnostic: discover the real CLI / MCP / orchestrator surface, then produce two
**task-first** document types — a **tutorial** that takes a first-timer from zero to a guaranteed first
success, and **how-to guides** that each walk a user through one real goal. This is the
*learning/doing* half; `document-user-manual` is the *reference* half (every command catalogued).
**Don't conflate them** — the single most common documentation mistake is mixing a tutorial with a
how-to. The split below is load-bearing.

## Tutorial vs how-to — the split you must hold

(The Diátaxis distinction — keep them as separate documents.)

| | **Tutorial** (learning) | **How-to guide** (doing) |
|--|-------------------------|--------------------------|
| Serves | a beginner *at study* — may not know what to ask | a user *at work* — has a specific goal |
| Promise | **a successful first experience**, guaranteed | the result, achieved |
| Path | **one safe route, no choices** — eliminate the unexpected | **forks/branches** on real-world conditions ("if X, then…") |
| Owner of success | **the writer** — you guarantee it works | the user — you give correct directions |
| Scope | a contrived, complete first journey | start and end at a *meaningful* point, not exhaustive |
| Voice | "we'll now do X; you'll see Y" | "to do X: …" |

## Author from the real surface, never from memory

Every step you write must run against the **actual** surface — the CLI's real `--help`/commands, the
MCP server's real `tools/list`, the orchestrator's real entry. A walkthrough whose steps don't actually
work is worse than none, because the reader *will* run them. Verify (step 5) before you ship.

## Scope — two SEPARATE questions, both asked before writing

Scope is two **independent** questions. They are easy to confuse, so keep them distinct and ask each
on its own. Neither is a "detail level" — every walkthrough is written to the same depth (step 4)
regardless of scope; scope only sets **which build** you document and **how much of that build** you cover.

> ⚠️ Beware the word **"full"** — it is ambiguous and must never be offered as a bare option. "Full"
> can mean the *fullest build* (axis 1) **or** *cover everything in a build* (axis 2). Always say which:
> "the `full` build" vs "the whole surface of *this* build". When the user says "full version", ask which
> they mean before proceeding.

### Question 1 — WHICH BUILD (the variant axis)

A project may ship the tool in **multiple compiled variants** (feature bundles / targets / profiles),
each exposing a *different set of commands* — a command in one is **absent** in another. Documenting a
build the reader doesn't have is a dead end.

1. **Discover the real variant list** from the project — CI release matrix (`.github/workflows/*`),
   build manifest (`Cargo.toml [features]` / `package.json` scripts), packaging (`Dockerfile`/`Makefile`),
   source gates (`#[cfg(feature`).
2. **Present the discovered builds to the user by name** and ASK which to document (one, several, or
   all — each chosen build gets its own walkthrough set). Do not offer abstract "basic/full"; offer the
   real names you found.
3. If you find only one build, confirm that with the user, then proceed.

### Question 2 — HOW MUCH OF THAT BUILD (the coverage axis)

Within the chosen build, the user may want the **whole** surface or just **one capability area**.

1. **Run the build's real `--help`** (or `tools/list`) and **group its commands into capability areas**
   — clusters of related tasks (e.g. a "core/getting-started" group, a "versioning/history" group, an
   "import/migration" group, a "maintenance" group), named from this build's actual commands.
2. **Present those real areas** and ASK: cover the **whole build**, or **specific area(s)**? "Whole
   build" means a how-to for every area (more guides, same depth each); a subset means only those areas.
   Either way the tutorial is the same single zero→first-success path.

Each chosen **build × coverage** gets its own tutorial + how-to set — never document a command the build
doesn't ship or an area the user didn't pick. A single-build, whole-surface request is the trivial case.

## Workflow

### 1. Settle scope, then discover the surface and real tasks

First settle scope by asking the **two separate questions** above — Q1 *which build* (present the real
discovered builds by name) and Q2 *how much of it* (present the real capability areas from that build's
`--help`). Then, **for each chosen build × coverage**, read its real surface (commands/flags,
tools/schemas, orchestrator entry — discover it, don't assume) and identify the **tasks a real user
actually wants to accomplish** — from the README's "getting started", the most-common command sequences,
support questions, the lifecycle. The tasks, not the commands, are what you'll document. **Completion
criterion: both questions are answered by the user against REAL options you presented (named builds; named
capability areas) — not abstract "basic/full"; and for each chosen build × coverage a list exists of
(a) the single zero→first-success journey for its tutorial, and (b) the discrete real-world goals each
how-to will cover — every entry tied to *that scope's* real surface, every goal phrased as a user task
("import a folder", "connect the MCP server to my editor"), not a command name, and nothing outside the
chosen coverage.**

### 2. Write the tutorial — one safe path to first success

Take an absolute beginner from nothing to one concrete, visible win, on **a single route with no
choices**. Skeleton: [reference/tutorial.md](./reference/tutorial.md). Be explicit about the basics
(where to type, what to install, what each step *does*); after every action state **what they should
see**; keep it **safe** (a beginner can restart with no damage); end with a **recap + next steps**.
**You own their success** — if a step can fail, remove the failure or pre-empt it. **Completion
criterion: the tutorial runs as one choice-free path from a clean start to a stated first success; every
step has a "you should see…" check; prerequisites are listed upfront; it ends with a recap and a pointer
onward.**

### 3. Write the how-to guides — one per real goal

For each goal from step 1, write a focused guide that starts and ends at a **meaningful** point (not a
from-scratch re-teach). Skeleton: [reference/how-to.md](./reference/how-to.md). Written **from the
user's goal**, not the machinery; **branch** where the real world forks ("if you're on Windows…", "if
the server is already running…"); cover the common case fully and link to reference for the long tail.
**Completion criterion: each how-to names its goal in the title, states its starting assumptions, gives
numbered task steps that branch where reality does, and stops at the achieved result — no how-to
re-explains what the tutorial already taught.**

### 4. Make every step copy-paste-real and observable

Across both types: each step is **one action**, starts with a verb, shows the **exact** command/tool
call (not paraphrased), and states the **observable result**. Code blocks are complete and explained
where a beginner needs it; no undefined jargon; a named human/contact or a link for when stuck.

**Use the system's user-facing words, never its internals.** A walkthrough names things the way a user
thinks about them, not the way the code does — use the domain terms a user already holds, not the
implementation's (data-structure names, algorithm acronyms, on-disk/storage terms, build-artifact
names). When the real output or reference docs leak an internal term, that is a signal to fix the
*system's* wording, not to teach the internal term. Lead with the one primary command that "just works"
for the common case; treat lower-level commands as power-user tools that don't belong in a novice path.
Describe the scope by what these docs *are* (the task they get done), not by listing the features they
*aren't* — an out-of-scope feature list is noise to a beginner who has no place to put it.

**Completion criterion: every step in every document is a single concrete action with an exact command
and an expected observable; no step says "configure appropriately", assumes undocumented knowledge, or
exposes an internal term where a user-facing word exists.**

### 5. Verify by walking it yourself

Run the tutorial start to finish, and a sample of each how-to, **against the real system** — paste the
commands, hit the "you should see…" checks, confirm the first-success and each goal are actually
reached. Fix any drift. **Completion criterion: the tutorial has been executed end-to-end and reaches
its stated success; a representative how-to from each goal has been executed to its result; the docs
match the real system — evidence, not assertion.**

## The pass test

Hand the **tutorial** to someone who has never seen the system; if they reach the first success by
following it literally — pasting every command, hitting every check — it passes. Hand a **how-to** to
someone who knows the basics but needs that one task done; if they get the result without reading
anything else, it passes. A reader stuck at a step with no check, an undefined term, or a missing branch
is a fail.

## What this is not

- Not `document-user-manual` — that's the **reference** (every command/flag/tool catalogued); this is
  the **journey** (do this task, succeed).
- Not `sdk-factory` — that documents a *client library* for developers; this teaches a *user* to operate
  the system.
- Not one merged doc — the tutorial and the how-tos are **separate documents** with different readers;
  merging them is the conflation Diátaxis warns against.
