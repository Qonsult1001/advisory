# SDK documentation standard — the non-negotiable

The bar: **radical clarity through specificity.** A reader with no prior knowledge of this system —
the proverbial "cleaner who never worked on computers" — reaches a working integration from the doc
alone. This standard is codified from a real banking SDK's consumer/developer docs. It is not advice;
it is the **pass/fail bar** for the documentation half of the skill.

Every SDK ships **two** docs, written to different readers (see the split at the end): a **Consumer
Guide** (how to *use* it) and a **Developer Guide** (how to *build/extend* it).

## The seven sections every guide has, in order

1. **Prerequisites** — a bullet checklist, no prose. Tools, versions, accounts, access. The reader
   verifies each yes/no before starting (`- Android SDK 21+`, `- An account on …`).
2. **Overview** — one paragraph: what this guide gets you to.
3. **Access & contacts** — repository links, dashboards, **and a named human** to ask when stuck
   (name + email/channel). Not "contact support" — an actual person.
4. **Numbered steps** — the body (see step rules below).
5. **Configuration reference** — every parameter, documented in full (see parameter rules below).
6. **Dependencies / initialisation order** — which module depends on what, and the **mandatory order**
   ("the main SDK must be initialised before any optional module").
7. **Notes / troubleshooting** — gotchas as `Note:` callouts; "if you see X, do Y".

## Step rules (the heart of it)

- **One action per step.** "Open the terminal, type `cd`, and drag the folder in" — not a paragraph.
- **Start with a verb.** *Go to…*, *Open…*, *Create…*, *Paste…* — never "one would configure…".
- **Self-contained.** A reader can do step 5 without re-reading step 1.
- **Exact, not paraphrased.** The real URL, the real command, the real button label — not "navigate to
  the registration endpoint" but "Go to `http://…/Register`".
- **A "you should see…" after EVERY step.** This is the rule that makes it followable. After each
  action, state the observable result — the screen, the console line, the created file, the folder
  structure. Without it the reader cannot tell success from failure. *"Paste it and hit enter; you
  should see: …"*, *"you should now have a newly created workspace."*
- **Code snippets are copy-paste-ready and explained line-by-line.** A preamble says what the block
  does and why; inline comments label each section; nothing assumes the reader knows the API used.
  *Show the whole snippet, then explain it — never dump code naked.*

## Parameter rules (configuration reference)

Every config parameter gets **all five**:

| Field | Example |
|-------|---------|
| **Name** (exact, usable in code) | `FutureBankServiceID` |
| **Type / nature** | a unique ID / a URL / a boolean |
| **Where to get it** | "supplied by FutureBank for your app" |
| **Required or optional** | Required · Optional |
| **Consequence / effect** | "without this key the system won't allow Face ID" |

Plus any **side effect** the reader must know ("on first init, the SDK generates and stores a UUID as
its AppInstanceId"). Optionality is **always explicit** — never leave the reader guessing.

## Callout pattern

Gotchas and required conditions are surfaced, never buried:

```
Note: <the condition that must be true>. <the consequence if it isn't>.
```

e.g. *"Note: for this login method to work, the `pin` flag in LoginViewController must be `false`."*

## Verification, end to end

The guide **ends on a checkable success state**, not "you're done": *"the simulator should pop up and
you will have successfully built the project"*, *"you will be logged in and arrive at this screen."* A
reader always knows whether they finished correctly.

## Consumer guide vs developer guide

The two readers differ; the docs must too. Honour the split:

| | **Consumer guide** | **Developer guide** |
|--|--------------------|----------------------|
| Reader | uses the SDK; knows their IDE/terminal basics, **not** this system | builds/extends/publishes it; knows CI, versioning, build tools |
| Entry | Prerequisites → setup steps | Important links → requirements → module map |
| Voice | **imperative** — "Do this. You should see…" | **declarative** — "The X module does Y. Ensure Z." |
| Screenshots / output | heavy — visual verification at each step | minimal — only for architecture |
| Code | complete, copy-paste-ready | fragments showing the pattern |
| Stuck? | troubleshooting + named contact | links to build logs / dashboards |

## The Developer Guide's mandatory build/publish section

The Developer Guide is not done at "module map." It MUST document how the SDK is built, tested,
versioned, and shipped — the FutureBank developer docs make this the bulk of the guide:

- **Important links** — the repos, the CI dashboard, the artifact registry, the code-quality board.
  Real URLs, not "the build server."
- **CI / build pipeline** — for each job: **trigger → action → output**. FutureBank documents
  `gk-fb-android-sdk-ci` (every push → compile + unit tests → Sonarqube), `…-nightly` (daily on
  develop), `…-publish` (manual → Artifactory + git tag). Name the pipeline script path
  (`.jenkins/Jenkinsfile.ci`).
- **Publishing workflow** — the exact step-by-step to cut a release (merge → set version → run the
  publish job → verify the artifact appears).
- **Versioning policy** — state it. FutureBank uses a single `semver.properties` across every module
  *"to simplify version definitions and avoid implementation incompatibilities between different
  versions for the consuming app."* If one version spans a multi-module SDK, say so and say why.
- **Maintenance rituals** — the recurring chores (branch cleanup, version bump, dependency refresh,
  wiki sync). "Start of sprint" rituals keep the docs and versions from drifting.

## Diagrams in the guides

The diagrams themselves — module-dependency, request sequence — are the architecture record's job
([architecture-standard.md](./architecture-standard.md)); the Developer Guide **links** it rather than
restating it. FutureBank's house style is shareable read-only sequencediagram.org links plus iOS-style
dependency *trees* (each module: description · repo · contact · targets · dependencies); Mermaid is
equally fine.

## Auth / session / biometric flow docs (when the SDK has them)

Auth flows get their own step-by-step doc, to the same standard, with the extras these flows need:

- numbered steps, each with code AND the **real endpoint** (`POST …/users/login/biometric/signature`)
  and a sample request/response body;
- the **config flags** that change behaviour, called out as `Note:` (FutureBank: *"for this login
  method to work, the `pin` flag in LoginViewController must be `false`"*);
- the **network/precondition** gotchas (*"Make sure you are connected to the GK Network"*);
- a `canEvaluatePolicy`-style capability check before attempting the action.

## Per-platform doc shape (multi-platform SDKs)

The consumer/developer split is invariant, but the **file layout adapts to the platform**, and the
docs must honour that:

| Platform | Doc shape (from FutureBank) |
|----------|-----------------------------|
| Android | one **Consumer** guide + one **Developer** guide (roles) |
| iOS | **topic-per-file** — Project Set Up · Project Structure · each Flow · CI · Committing |
| Web | install (npm) **and** embed (`<script>`) ingress, token-based init, locale config |
| MCP/agents | the tool catalogue (each tool: name · input schema · idempotency key · auth) is the consumer surface |

Pick the shape that fits the platform; keep the seven sections and every rule above inside it. A
multi-platform SDK documents **what differs per platform** explicitly (FutureBank: Android = Gradle +
`semver.properties`; iOS = clone-4-repos + `pod install` + `.xcworkspace`).

## What this standard forbids

- Code without a per-line explanation and preamble.
- A step with no "you should see…" verification.
- A parameter missing any of: name / type / source / optionality / consequence.
- Jargon, tool, or repo named without a definition or link.
- "You'll figure it out" / "navigate to the appropriate endpoint" — vagueness anywhere.
- One merged doc for both readers — the consumer and the developer get **separate** guides.
- A Developer Guide with no build/publish/versioning section — "how to use" without "how to ship".
- A doc set with no sequence diagram and no dependency view.
- A "contact support" with no **named human** (name + email/channel).
- A multi-platform SDK that hides what differs per platform behind one generic page.

## The pass test

Hand the Consumer Guide to someone who has never seen the system. If they reach a working integration
by following it literally — pasting every command, checking every "you should see" — the doc passes.
If they get stuck at a step with no verification, an undefined term, or an undocumented parameter, it
fails. There is no partial credit; this half is the differentiator.
