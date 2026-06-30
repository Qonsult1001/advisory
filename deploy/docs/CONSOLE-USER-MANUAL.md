# Advisory — User Manual (Console & Repository)

This manual covers everything you **see and do** in Advisory as a user — the web console where you
curate and gate packages, and the Nexus repository where developers pull approved packages from. It
follows the real journey a package takes through the firewall: **search → gate → quarantine → approve
→ consume → audit**.

Two interfaces, two URLs:

| Interface | Address | Who uses it | What it's for |
|---|---|---|---|
| **Advisory console** | `<CONSOLE_URL>` | Security / curation team | Search packages, set policy, gate, approve, audit |
| **Nexus repository** | `<NEXUS_URL>` | Developers | Pull approved packages; browse what's available |

> `<CONSOLE_URL>` and `<NEXUS_URL>` are the addresses your administrator configured for this install
> (your own domain or host). Ask your administrator for the exact addresses; this manual uses the
> placeholders throughout.

> Scope: this manual documents the **visible** console surface (the left-nav groups Catalog, Xray,
> Curation, Pipeline) and the Nexus repository. Hidden/disabled areas (Administration, Memories,
> Dashboard, AppTrust, AI/ML, Mutation, Evolution) are not covered.

---

## Contents

1. [Signing in (splash + SSO)](#1-signing-in)
2. [The console at a glance (topbar & nav)](#2-the-console-at-a-glance)
3. [Catalog — research a package](#3-catalog)
4. [Curation — set your policy](#4-curation)
   - [Policy controls](#41-policy-controls)
   - [Intelligence sources](#42-intelligence-sources)
   - [Known-exploited (KEV)](#43-known-exploited-kev)
5. [Pipeline — gate, approve, and ship](#5-pipeline)
   - [Intake queue](#51-intake-queue)
   - [Quarantine](#52-quarantine)
   - [Approved packages](#53-approved-packages)
   - [Approved exceptions](#54-approved-exceptions)
6. [Xray — scan and watch](#6-xray)
   - [Scans List](#61-scans-list)
   - [On-Demand Scanning](#62-on-demand-scanning)
   - [Watches & Policies](#63-watches--policies)
   - [Overview & Watch Violations](#64-overview--watch-violations)
7. [Evidence — Decision ledger & Reports](#7-evidence)
8. [The Nexus repository (developers pull here)](#8-the-nexus-repository)
9. [Ask AI](#9-ask-ai)
10. [Reset test data](#10-reset-test-data)
11. [Troubleshooting](#11-troubleshooting)
12. [Glossary](#12-glossary)
13. [Quick reference](#13-quick-reference)

---

## 1. Signing in

Open the console at `<CONSOLE_URL>`. You land on the **splash login** — a split screen: sign-in on the left,
a green capability panel on the right.

There's a **Require SSO sign-in** switch at the bottom of the sign-in panel:

- **Switch OFF (testing mode)** — the button reads **Continue**. Click it to go straight into the
  console with no sign-in. A red note reminds you this is testing mode.
- **Switch ON** — the button reads **Sign in with SSO**. Click it and you're redirected to your
  organisation's identity provider (Entra ID / Okta / etc.). After signing in you return to the console.

Your session is remembered until you **Log out** (top-right of the console) or close the browser.

> **To log out:** click **Log out** in the top-right corner of the console. You return to the splash
> login screen.

---

## 2. The console at a glance

After sign-in, the console has three regions:

**Top bar** (left → right):
- **Advisory** wordmark — clicking it returns you to the Catalog.
- **Reset test data** button (red) — wipes test data clean (see §10).
- **Search packages, CVEs…** box — global search.
- **✦ Ask AI** — the AI assistant (see §9).
- **Connected** status + **Policy v<n> · SHA-256 …** — your live, signed policy version.
- **Avatar** + **Log out**.

**Left nav** — four groups. This is the whole visible surface:

| Group | Screens |
|---|---|
| **Catalog** | (single screen — package research) |
| **Xray** | Scans List · Overview · Watch Violations · On-Demand Scanning · Watches & Policies |
| **Curation** | Policy controls · Intelligence sources · Known-exploited (KEV) |
| **Pipeline** | Intake queue · Quarantine · Approved packages · Approved exceptions · Decision ledger · Reports |

**Main panel** — the selected screen.

---

## 3. Catalog

**Nav: Catalog.** Your research surface — look up any open-source package or CVE before it enters your
org, with live data from OSV.dev, CISA KEV, EPSS and OpenSSF.

### Search for a package
1. Pick an **ecosystem** from the dropdown (npm, PyPI, NuGet, Cargo, Go, and others).
2. Type a package name, or paste a CVE id (`CVE-2021-44228`), in the search box.
3. Press **Search**.

You can also click the **example chips** (express, lodash@4.17.15, left-pad, …) to jump straight to a
known package.

**What you see — the package overview:**
- **Approval banner** — *"✓ Approved for downloading"* (green) or *"⊘ Blocked by policy"* (red) — the
  gate's verdict on this package, previewed *before* you pull it.
- **Published date, number of versions, vulnerabilities (counts by severity), dependencies, licence,
  operational risk, OpenSSF score.**
- **Install instructions** — the exact `npm install …` / `pip install …` line.
- **Send to Intake queue** — pushes this package into the firewall pipeline (see §5.1). After clicking,
  watch it under **Pipeline → Quarantine**.
- Tabs across the top: **Vulnerabilities · Dependencies · OpenSSF · Licenses · Operational Risk**.

### Look up a CVE
Paste a CVE/GHSA id into search. You get the live advisory: severity, CVSS, **KEV (exploited) flag**,
**EPSS probability**, affected versions, and the fixed version.

> **Not found:** if you search a package that doesn't exist in that registry, you get a clear
> **"Package not found"** message — not a blank page. If you pick an ecosystem that isn't supported,
> the search reports it rather than guessing.

---

## 4. Curation

This is where you define **what the firewall allows or blocks**. Changes here don't take effect until
you **Commit & sign policy** — committing increments the version, re-signs it (SHA-256), and writes it
to the ledger.

### 4.1 Policy controls

**Nav: Curation → Policy controls.** The rules the gate enforces on every package. Three ways to set
them, easiest first:

**A. Choose a starting point (presets).** One click sets every control to a profile:
- **Strict** — locks down hard (high-severity flaws, likely-exploited, new packages, unhealthy projects).
- **Recommended** ⭐ — best for most teams: all free feeds + high/critical flaws + actively-exploited.
- **Permissive** — lightest touch: only critical flaws + actively-exploited.

The profile matching your current policy shows **✓ ACTIVE**.

**B. Build rules with AI.** Type what you want in plain English (e.g. *"block critical CVEs and GPL
licences, warn on medium"*) and press **Apply to controls**. The AI sets the matching controls and
tells you exactly what it changed (with a **see it ↓** link to each affected control). Click a **Try
one** chip for an example.

**C. ✦ Guided setup** (top-right button). A step-by-step wizard that walks you through each control one
at a time — plain question, recommended value, and *why* (with the published source) — so you can
Accept or Adjust each. Ends at *"Finish — review & commit."*

**The controls themselves** (expand **Advanced settings**). Each has a plain-English explanation and a
**⭐ Recommended** line with its source. Grouped:

| Group | Controls |
|---|---|
| **Known vulnerabilities** | Block when CVSS ≥ N · Block known-exploited (KEV) · Block when EPSS ≥ N |
| **Supply-chain hygiene** | Minimum published age (cooling-off days) · Max transitive depth · OpenSSF Scorecard ≥ N |
| **Licensing & project health** | Prohibited licences (e.g. GPL-3.0) · Operational risk action (Disabled/Notify/Block) |
| **AI-editor extension gate** | High-risk extension action · Unverified-publisher action |
| **Model-weight controls** | Safetensors-only · Block pickle / scan opcodes · Require SHA-256 hash pin |
| **Artifact content scanning** | Scan for embedded secrets + IaC misconfigurations |

**Recommended values** (industry-grounded — shown inline with sources): CVSS ≥ 7 (PCI-DSS/FIRST),
KEV on (CISA), EPSS ≥ 0.1 (FIRST), 14-day cooling-off, Scorecard ≥ 5 (OpenSSF), operational-risk
Notify, content-scan on, prohibited licences AGPL-3.0/GPL-3.0/GPL-2.0 (Google/enterprise practice).

> **When you change anything, press *Commit & sign policy* (top-right).** Until you do, the gate keeps
> running your previous signed policy.

### 4.2 Intelligence sources

**Nav: Curation → Intelligence sources.** The **data feeds** the gate reads from. Each has plain-English
"what / why" and a **recommended** tag on the core free feeds.

**Choose which feeds to use (presets):**
- **Minimal** — OSV only.
- **Recommended** ⭐ — all free feeds (OSV, malicious-packages, KEV, EPSS, extension scanner).
- **Everything** — all available (credentialed feeds activate once their key is set).

**Set up sources with AI** — describe what intelligence you want ("enable all free feeds and
exploited-in-the-wild") and it turns the right feeds on, highlighting what changed.

**The feed table** — per feed: **Tier** (Included/Licensed), **Test status** (● Ready / No credential),
**Enabled** toggle, **Required** toggle, and **Test** / **Edit** actions. Click **Egress: … — click
for full data flow** to see exactly what each feed sends out.

**Coupling warnings.** If a Policy control needs a feed that's off (e.g. you block on KEV but the CISA
KEV feed is disabled), a warning banner appears — *"…that rule can't fire"* — with a **Turn it on**
button. If you enable a feed that needs a key it doesn't have (Artifactory, Socket), a **needs key**
tag + warning lets you **Add key** or **Turn it off**.

### 4.3 Known-exploited (KEV)

**Nav: Curation → Known-exploited (KEV).** Browse the live **CISA Known-Exploited Vulnerabilities**
catalogue — the actual list behind the "known-exploited" block rule. Search by text (e.g. `log4j`) to
see which CVEs are being actively exploited in the wild right now.

---

## 5. Pipeline

The pipeline is the firewall's conveyor belt: a package is **submitted → fetched into quarantine →
gated → promoted (or held) → consumed from approved.**

### 5.1 Intake queue

**Nav: Pipeline → Intake queue.** The live view of the firewall's work queue.

**You submit packages from the Catalog** (a package page → *Send to Intake queue*) — not from here.
This page shows:
- **Pending / Processed / Dead-lettered** counters (live).
- **How a package moves through the firewall** — a 4-step explainer: Submit → Pending → **Gate cycle
  (every 30 seconds)** → Promote or Hold.
- Buttons: **→ Go to Catalog to submit a package** and **View Quarantine**.

> **The 30-second cycle:** a background worker re-evaluates quarantine every 30 s against your Policy
> controls + Intelligence feeds. A package added mid-cycle waits for the next tick — that's why status
> can briefly read *Pending* / *Promoting…*.
>
> **If a package can't be fetched** (e.g. a typo'd name that doesn't exist upstream), it is
> **dead-lettered** with a reason — it won't silently vanish.

### 5.2 Quarantine

**Nav: Pipeline → Quarantine.** Every package the firewall is currently handling, with its status:

| Status | Meaning |
|---|---|
| **Promoted** | Allowed — copied to the approved repo; developers can pull it |
| **Held / Blocked** | Stopped by the gate — stays in quarantine; an operator can override |
| **Revoked** | Approval pulled back out of approved |
| **Pending / Promoting…** | Awaiting or mid the next gate cycle |

Each row shows the **reason** (e.g. *"Allowed — promoted to approved"* or the triggered rules for a
block). Blocked rows show the violation count (e.g. `2C/0H` = 2 critical, 0 high).

### 5.3 Approved packages

**Nav: Pipeline → Approved packages.** The vetted packages that passed the gate and were promoted to
the approved repos — the list developers can pull. Search to filter. Each row has a **Revoke** button:
an operator override that **pulls an already-approved package back out** of approved (it can no longer
be pulled, and it returns to a held state).

### 5.4 Approved exceptions

**Nav: Pipeline → Approved exceptions.** Grant a time-bound waiver so a package the gate would block is
allowed anyway (with an audit trail).

**To grant an exception:**
1. Enter the package as `name==version` (e.g. `pyyaml==5.3.1`).
2. Enter a **Ticket** reference (e.g. `SEC-1234`) and the **Approver**.
3. Pick an **expiry date** (date picker; blank = +90 days).
4. Press **Add exception**.

The exception persists into the signed policy and clears any prior revoke. **To revoke an exception:**
use its **Revoke** action — this removes the exception *and* pulls the package back out of approved
(revoke wins over an exception). Expired exceptions are swept automatically (hourly) and audited.

---

## 6. Xray

The Xray group is the **scanning and watch** surface — JFrog-Xray-style.

### 6.1 Scans List

**Nav: Xray → Scans List.** Two tabs:

- **Repositories** — the **Ecosystem firewall**: all 18 gateable ecosystems shown as cards (PyPI, npm,
  NuGet, Cargo, Go, Maven, RubyGems, Composer, Conan, CRAN, DartPub, Alpine, Debian, Ubuntu,
  HuggingFace, Docker, Conda, AIEditorExtensions). Each shows its gate mechanism (Nexus-gated OSV /
  scanner / research-only) and an **Add** / **Remove** button to provision or deprovision it. Below,
  the **Repositories** table lists the live Nexus repos (`pypi-approved` hosted, `pypi-quarantine`
  proxy) with indexed-artifact counts.
- **Packages** — every package scanned across all repos, with **Verdict** (Clean / Vulnerable) and
  **Stage** (✓ Approved / ⏸ Held / ⛔ Revoked). **Click a package** to drill into only that package's
  artifacts and full scan report.

**To provision a new ecosystem:** Scans List → Repositories → click **Add** on an ecosystem card (or
the **+ Add ecosystem** dropdown). It creates the quarantine proxy + approved repos in Nexus.

### 6.2 On-Demand Scanning

**Nav: Xray → On-Demand Scanning.** Scan a single package right now without sending it through the
pipeline — useful for a quick "would this be allowed?" check. Enter ecosystem + name + version; the
scan runs and the result lands in the scan history.

### 6.3 Watches & Policies

**Nav: Xray → Watches & Policies.** Create **watches** (what to monitor) and **policies** (the rules
a watch enforces). Two tabs: **Watches** and **Policies**, each with a **New Policy** flow. Within a
policy you can **Build rules with AI** (plain-English → rules) and use **quick-add** presets (Block
Critical CVEs, Block KEV, Block Malicious Packages, Block Prohibited Licenses, Notify on Medium CVEs).
You can **delete** a policy or watch from its list.

### 6.4 Overview & Watch Violations

- **Xray → Overview** — the security-posture dashboard: violations by severity/type, indexed
  repos/artifacts, top vulnerable components. All live data.
- **Xray → Watch Violations** — the violations your watches caught (Block / Quarantine), with the
  triggered controls and waiver status.

---

## 7. Evidence

### Decision ledger

**Nav: Pipeline → Decision ledger.** The append-only, hash-chained record of **every gate decision**.
Each row: component, **Decision** (Allow / Block), tree depth, **Coverage** (complete/partial),
timestamp. **Click a row to expand** it and see:
- **Why this decision** — a plain-English AI rationale (written by the Groq assistant per decision when
  the research agent is on).
- **Triggered controls** — which policy rules fired.
- **Source coverage** — per-feed status (Ok / Empty / Skipped) and findings, plus any gaps.

The ledger refreshes live while you watch it, and the most recent decisions appear within seconds.

### Reports

**Nav: Pipeline → Reports.** Aggregated, exportable views over the decision ledger — the four
Xray-style report types:

| Report | What it shows |
|---|---|
| **Vulnerabilities** | Every vulnerability seen — severity, CVSS, EPSS, KEV, fix version |
| **Violations** | Block / Quarantine decisions with triggered controls + waiver status |
| **Legal · Licenses** | Declared licence, prohibited matches, unknowns |
| **Operational Risk** | EOL, version age, newer versions, project health |

Each report opens with an **executive summary** — headline stat cards + a distribution chart — above
the detail table. **Click any row** to expand its full detail (CVE ids link to osv.dev). **Export** via
the dropdown: **PDF** (print view), **Word (.doc)**, or **CSV**.

---

## 8. The Nexus repository

**Address: `<NEXUS_URL>`** (sign in with the repository credentials your administrator provided). This
is the **developer-facing** half — where packages physically live and where developers **pull approved
packages**. Open `<NEXUS_URL>/#browse/welcome` for the browse view.

### What's there
The firewall uses a **two-repo model per ecosystem**:

| Repo | Type | Role |
|---|---|---|
| `<eco>-quarantine` | **proxy** | Packages land here first, fetched from upstream; the gate evaluates them here. Developers cannot pull from quarantine. |
| `<eco>-approved` | **hosted** | Packages the gate **allowed** are promoted here. **This is what developers pull from.** |

Browse **`#browse/browse`** to see the components in each repo. For example `pypi-approved` holds the
PyPI packages that passed the gate.

### How a developer pulls an approved package
Point the package manager at the **approved** repo URL, not the public registry. For PyPI:

```
pip install <package> --index-url <NEXUS_URL>/repository/pypi-approved/simple/
```

Only packages the gate **promoted** are available there — a blocked package simply isn't in
`pypi-approved`, so the install fails closed. This is the **non-bypassable enforcement**: developers
can only get what the firewall approved.

### Browsing
- **Browse → Browse** — tree view of every repo and its components/assets.
- **Browse → Welcome** — the Nexus landing/welcome page.
- **Search** — find a component across repos.

> Provisioning new ecosystems and emptying repos is driven from the **Advisory console**
> (Scans List → Repositories), not from Nexus directly — keep curation in the console.

---

## 9. Ask AI

**Top bar: ✦ Ask AI.** A Groq-backed assistant grounded in your live policy and recent decisions. Ask
things like *"why was pyyaml 3.10 blocked?"* or *"what does the EPSS control do?"* and it answers in
plain English. The same engine powers **Build rules with AI** (Policy controls) and the per-decision
rationales in the Decision ledger.

---

## 10. Reset test data

**Top bar: Reset test data** (red button — a testing aid). It clears the decision ledger, scans,
revocations, the intake queue, and **empties every firewall repo** (quarantine + approved). It does
**not** change your policy or provisioned ecosystems. The immutable WORM audit mirror is retained.
A confirm dialog appears first; this cannot be undone.

> Use this to start a clean demo/test run. After it, the counters and ledger read zero, and the
> quarantine/approved repos are empty.

---

## 11. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Splash shows but console won't load after a redeploy | Browser cached the old bundle | Hard-refresh (`Ctrl+Shift+R`) or open in incognito |
| "Continue" doesn't sign me in | (it does — testing mode) | That's expected with SSO off; flip **Require SSO sign-in** on for real auth |
| Package I submitted never appears in Quarantine | Next 30-s gate cycle hasn't run, or the fetch failed | Wait one cycle; if it dead-lettered, the name/version likely doesn't exist upstream |
| "Could not send" / enqueue to an ecosystem fails | That ecosystem isn't provisioned | Scans List → Repositories → **Add** that ecosystem first |
| Policy change didn't take effect | You didn't commit | Press **Commit & sign policy** (top-right of Policy controls) |
| KEV/EPSS rule "can't fire" warning | The feed it needs is disabled | Click **Turn it on** in the warning, then commit |
| A feed shows "needs key" | Credentialed feed enabled without a key | **Add key** (or **Turn it off**) on the warning |
| "Build rules with AI" returns nothing | Rare model hiccup | Re-run, or use the quick-add presets / Guided setup |
| Decision ledger shows "No entries" right after a reset | Stale view | It refreshes live; new decisions appear within seconds |
| Developer `pip install` from approved fails | The package was blocked (not in approved) | Check Quarantine for its held reason; grant an exception if justified |

---

## 12. Glossary

- **Gate** — the decision engine that returns Allow / Block / Quarantine for a package.
- **Ecosystem** — a package world (npm, PyPI, Maven, Cargo, HuggingFace, …).
- **Quarantine repo** — the Nexus **proxy** where unvetted packages land first.
- **Approved repo** — the Nexus **hosted** repo developers pull from after approval.
- **Promotion** — copying an allowed package from quarantine to approved.
- **Hold / Block** — the gate stopping a package; it stays in quarantine.
- **Revoke** — pulling an already-approved package back out of approved.
- **Exception** — a time-bound, ticketed waiver allowing a would-be-blocked package.
- **Policy controls** — the rules the gate enforces (CVSS, KEV, EPSS, licences, …).
- **Intelligence sources** — the data feeds the gate reads (OSV, CISA KEV, EPSS, …).
- **KEV** — CISA's Known-Exploited Vulnerabilities catalogue.
- **EPSS** — FIRST's Exploit Prediction Scoring System (probability of exploitation).
- **Decision ledger** — the append-only, hash-chained audit record of every decision.
- **WORM mirror** — the immutable copy of the ledger (retained even through a reset).
- **PromotionBridge** — the background worker that re-gates quarantine every 30 seconds.

---

## 13. Quick reference

**Two addresses:** Console `<CONSOLE_URL>` (curate & gate) · Nexus `<NEXUS_URL>` (developers pull) —
both set per install; ask your administrator.

**The journey:** Catalog (research) → *Send to Intake queue* → Quarantine (gated every 30 s) →
Approved packages → developers `pip/npm install` from `<eco>-approved` → Decision ledger / Reports
(evidence).

**Set policy:** Curation → Policy controls → pick a **preset** *or* **Build rules with AI** *or*
**Guided setup** → **Commit & sign policy**.

**Turn on feeds:** Curation → Intelligence sources → **Recommended** preset → resolve any
coupling/credential warnings.

**Provision an ecosystem:** Xray → Scans List → Repositories → **Add**.

**Override the gate:** Approved exceptions (allow a blocked package) · Approved packages → Revoke
(pull an approved one).

**Evidence for auditors:** Decision ledger (per-decision, hash-chained) · Reports → Export PDF/Word/CSV.

**Log out:** top-right **Log out** → back to splash.
