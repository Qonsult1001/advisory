# Advisory — self-built Xray / Sonatype-Firewall equivalent

Quarantine-gate supply-chain security gate for a locked-down banking production environment.
C# (ASP.NET Core) API + React policy console, fronting **Nexus Repository OSS** (free proxy/cache).
Decides allow / block per a **signed policy you own**, across the full transitive dependency tree.

## Parity status vs paid tooling

| Capability | Paid (Xray / Firewall) | This build |
|---|---|---|
| Multi-ecosystem CVE matching | ✓ | ✓ OSV.dev (PyPI, npm, NuGet, Cargo, Go) |
| Known-exploited gating | ✓ | ✓ CISA KEV |
| Exploit-probability gating | partial | ✓ EPSS |
| **Transitive tree resolution** | ✓ | ✓ per-ecosystem resolvers, cycle-safe, depth-bounded |
| License policy | ✓ | ✓ |
| Model-weight / pickle scanning | weak | ✓ real opcode scan (dangerous GLOBAL/REDUCE) |
| Secret scanning in artifacts | ✓ | ✓ regex engine |
| SBOM (CycloneDX) | ✓ | ✓ generated from resolved tree |
| Policy-as-gate + signed versions | ✓ | ✓ SHA-256 signed, versioned |
| Append-only audit | ✓ | ✓ component-count stamped |
| Non-bypassable enforcement | ✓ | ✓ Nexus pre-download hook (/api/enforce) |
| Proprietary pre-NVD / zero-day intel | ✓ | ◻ pluggable — VulnCheck plugin, activates on key |
| RBAC/SSO, HA, quarantine-release UI | ✓ | ◻ wire to bank SSO / scale-out in prod |

Reaches functional parity on detection + policy + audit. The remaining items are
operational hardening and the licensed intel feed (a credential, not a rebuild).

## Endpoints
- `POST /api/gate/evaluate` — full-tree evaluation of a package
- `POST /api/enforce` — Nexus pre-download hook (200 allow / 403 block) — makes the gate non-bypassable
- `POST /api/sbom` — CycloneDX SBOM for a package's resolved tree
- `GET/PUT /api/policy` — read / commit (re-sign) policy
- `GET /api/audit` — decision ledger
- `GET /api/sources` — intel plugin status

## Run (Docker)
```bash
docker compose up --build
# Nexus OSS  -> http://localhost:8081
# API        -> http://localhost:5000  (Swagger /swagger)
# Console    -> http://localhost:8080
```
Activate licensed zero-day intel: set `VULNCHECK_API_KEY` in docker-compose.yml — no code change.

## Enforcement (the critical config)
The gate only works if Nexus cannot serve a proxied artifact without calling `/api/enforce`
first. Configure Nexus's pre-download / routing rule to call it and treat 403 as deny.
Without this, developers can route around the gate.

## Honest residual risk (lead with this to SecOps)
Included feeds (OSV/KEV/EPSS) lag proprietary research and miss some zero-days —
acceptable for production risk-tiering, not production supply-chain. The gap closes by enabling
a licensed feed. You hand security an auditable gate where they set every threshold and
read every line — stronger than trusting a black box. Licensing cost to start: $0.

## Coverage-aware decisions & research rationale (added)

A decision never rests on a single feed's silence.

- **Health-aware sources** — each feed returns Ok / Empty / Errored / Timeout / NotConfigured,
  with timing and error detail. "Errored" is never treated as "clean".
- **Required sources (SEC-COV-01)** — policy names the feeds that must be conclusive for a clean
  Allow (default: OSV).
- **Quarantine on uncertainty (SEC-COV-02)** — if a required source errors/times out, the package
  is **Quarantined**, not Allowed. A feed outage cannot leak an unscanned package through.
- **Coverage report** — every decision records per-source status + a plain list of gaps
  ("vulncheck is not licensed — its dimension was not verified").
- **Research agent (SEC-AUD-03)** — calls Claude via the Anthropic API to write an audit-grade
  rationale that explicitly states what was checked, what was missing and why, transitive findings,
  and residual risk. Set `ANTHROPIC_API_KEY` (and optional `ANTHROPIC_MODEL`). If unset or the call
  fails, a deterministic local rationale is written instead — the trail is never empty.

## Tamper-evident audit (added)

- **Hash-chained ledger** — each entry stores the SHA-256 of the previous sealed line, so any edit
  breaks the chain and is detectable.
- **WORM/SIEM sink** — every entry mirrored to a pluggable `IWormSink` (default file; swap for
  S3 Object Lock / Splunk / Sentinel via `WormPath` or a custom implementation).

## Exception expiry sweep (added)

`ExceptionSweepJob` runs hourly: purges expired exceptions and records each lapse as an audit event
(`SEC-EXC-EXPIRED`). Granting an exception stays manual (human accountability); enforcement, expiry
and logging are automatic.

## New env vars
- `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL` — research agent
- `WormPath` — WORM sink file (or replace IWormSink)

## Malicious-package detection (added) — the source that was missing

CVE scanners miss freshly-published malware and typosquats — those carry no CVE, so a
CVE-only scan passes them as clean. Added a distinct `MalwareSource`:
- **Free tier:** OpenSSF Malicious Packages feed via OSV (MAL-* advisories) — typosquats,
  dependency-confusion, malicious releases. Near-free since OSV is already queried.
- **Paid tier:** Socket behavioural analysis (install-script / runtime behaviour),
  activates on `SOCKET_API_KEY` behind the same plugin seam.
Set as a **required source** by default, so a malware-feed outage triggers quarantine rather
than a clean pass.

## Ticket loop — how this closes (and how global tools do it)

Design rule: the firewall is the system of record for the **decision**; the ITSM
(Jira / ServiceNow) is the system of record for the **approval workflow**. They are linked,
not merged — the same pattern Sonatype/Artifactory use.

- On every Block / Quarantine, `ItsmWebhook` fires an outbound event (`ITSM_WEBHOOK_URL`)
  carrying component, decision, triggered controls, coverage gaps and rationale. Your ITSM
  opens/links a ticket from that event.
- The resulting ticket id is stored back on the exception (`ticket` field) → bidirectional
  reference. An auditor pivots either direction: ledger → ticket, or ticket → decision.
- We deliberately do NOT create tickets inside this tool: approval authority, SLAs and
  segregation-of-duties already live in the ITSM; duplicating them creates two divergent
  sources of truth.

## What WORM sink / sweep job mean (plain)
- **WORM sink** = Write Once Read Many storage; once a record is written it can't be edited
  or deleted (S3 Object Lock / Azure immutable blob / SIEM). Makes the audit log admissible.
  Default impl writes a file; swap `IWormSink` for real WORM in prod.
- **Sweep job** = hourly background task that purges expired exceptions and logs each lapse.

## Additional env vars
- `SOCKET_API_KEY` — paid behavioural malware tier
- `ITSM_WEBHOOK_URL` — outbound endpoint your Jira/ServiceNow consumes

## JFrog Artifactory scanning API (added as a source, not the proxy)

Nexus stays the proxy — unchanged. Artifactory's free scanning API is added as another
`IVulnSource` plugin (`ArtifactorySource`), cross-referencing component vulnerabilities into
the same gate/coverage/audit flow. Health-aware like the rest. Configure:
- `ARTIFACTORY_URL` (base, e.g. https://artifactory.internal/artifactory)
- `ARTIFACTORY_TOKEN`
Inactive (NotConfigured) until both are set. Parser is tolerant of response shape — adjust
field selectors in ParseFindings to your Artifactory version if its JSON differs.

## API authentication (added)
`ApiKeyMiddleware` guards all `/api` routes (Swagger stays open). Two scopes:
- `API_KEY` — read / evaluate
- `ADMIN_API_KEY` — required for policy writes (PUT /api/policy)
Auth is disabled when no key is configured (dev). Clients send `X-Api-Key`. Replace with
bank SSO/OIDC for production — this is the floor.

## Paid plugins wired (no longer stubs)
- **VulnCheck** (`VulnCheckSource`) — real purl-index call, maps exploit intel to KnownExploited. Activates on `VULNCHECK_API_KEY`.
- **Socket** behavioural — real issues API call inside `MalwareSource`, merges High/Critical behavioural risks. Activates on `SOCKET_API_KEY`.

## Integration tests (added)
`tests/Advisory.Tests` runs the whole app in-process (WebApplicationFactory) with a real
HTTP client. Run: `dotnet test`. 7 tests — policy signing, source listing, weights gate
(block pickle / allow safetensors), the enforce 403 path, plus live OSV/registry evaluations
(lodash known-vuln, requests tree resolution). Network tests skip automatically with
`OFFLINE_TESTS=1` for air-gapped CI. Current: 7/7 pass online, 5 pass + 2 skip offline.

Note: testing surfaced and fixed a real bug — the API was serialising decision enums as
numbers while the console expected names; now emits string enums API-wide.

## Nexus promotion bridge — the interception piece (gap now closed)

Quarantine is now a **physical location**, not just a decision label. Two-repo model:
- `quarantine-pypi` — proxy repo (upstream PyPI) developers CANNOT read; packages land here first
- `approved-pypi` — hosted repo developers DO pull from

`PromotionBridge` (background service) polls the quarantine repo, runs each component through
the full gate, then:
- **Allow** → uploads bytes to the approved repo (promote)
- **Block / Quarantine** → leaves them held in the quarantine repo (the physical quarantine
  location) and audits it; `NEXUS_DELETE_ON_HOLD=true` purges instead

This is what makes the gate actually stop packages rather than just decide. `GET /api/quarantine`
shows what's physically held right now. Bridge idles if `NEXUS_URL` is unset (decision API
still usable directly).

### Setup after first `docker compose up`
1. Log into Nexus (:8081), change the admin password, set `NEXUS_PASS` to match.
2. Create the two repos above (quarantine = proxy to pypi.org; approved = hosted).
3. Point developers' pip index at the **approved** repo only.
4. Bridge promotes clean packages from quarantine → approved automatically.

## Where each thing lives (runtime map)
- **Package bytes:** Nexus volume (`nexus-data`) — Nexus is the store, the firewall is not.
- **Quarantine (held packages):** the `quarantine-pypi` Nexus repo, unreleased.
- **Policy, audit, decisions:** firewall API + `fw-data` volume.
- **Decision engine:** the C# API. **Interception:** the promotion bridge.

## Test suite — now 9 tests
Added two promotion-bridge tests (offline, fake Nexus): clean safetensors weight is promoted,
malicious pickle weight is held. Run `dotnet test`. 9/9 online, 7 pass + 2 skip offline.
Testing surfaced and fixed a second real bug: an empty/corrupt `policy.json` crashed startup;
PolicyStore now falls back to safe defaults.

## Nexus promotion bridge — quarantine is now physical (added)

The gap is closed. Quarantine is a real location, not just a decision label:
- **`quarantine-pypi`** — a Nexus proxy repo developers cannot read. Packages land here first.
- **`approved-pypi`** — a Nexus hosted repo developers pull from.
- **`PromotionBridge`** (background service) polls quarantine, runs each component through the
  full gate, and on **Allow** promotes (uploads) it to approved; on **Block/Quarantine** leaves
  it held in quarantine (the physical holding area) and audits it. Optional `NEXUS_DELETE_ON_HOLD`.
- **`GET /api/quarantine`** lists what is physically held right now.

Setup after first `docker compose up`: log into Nexus (:8081), create the two repos named in
the compose env, point developers' index URL at `approved-pypi`. The bridge does the rest.
Bridge idles harmlessly if `NEXUS_URL` is unset — the decision API still works standalone.

## Final test status
9/9 pass online, 7 pass + 2 skip offline. Suite covers: policy signing, source listing,
weights gate (block pickle / allow safetensors), enforce-403, live OSV/registry evaluations,
and the promotion bridge (clean→promoted, malicious pickle→held). Testing surfaced and fixed
two real bugs: number-vs-string enum serialisation, and a startup crash on an empty policy file.

## Durable intake queue (SQL Server) — added

Decouples enqueue from evaluation so developers/proxy never wait on the gate. Chosen over
Kafka/Redis deliberately: package gating is low-volume, single-consumer, audit-critical — the
documented sweet spot for a database-backed queue. SQL Server runs under the bank's existing
change-control, backup and DR; the queue table is itself an audit artifact.

- **`POST /api/queue/enqueue`** — returns 202 immediately with a message id (no waiting).
- **`IntakeConsumer`** (background) drains the queue through the full gate; at-least-once with
  explicit ack, automatic retry, and dead-letter after `MaxRetries`.
- **`GET /api/queue/depth`** — pending / dead-lettered / processed counts.
- **Concurrency:** workers claim rows with `UPDATE TOP(n) ... WITH (READPAST, UPDLOCK, ROWLOCK)
  ... OUTPUT` — the SQL Server equivalent of `SELECT ... FOR UPDATE SKIP LOCKED`; many workers
  pull disjoint batches without colliding. Stuck 'processing' rows auto-reclaim after 60s.
- **Table** `dbo.IntakeQueue` auto-created on startup; status pending|processing|done|dead.
- **Swappable:** all behind `IIntakeQueue` — drop in Kafka/Redis later (a new class) if
  consumer fan-out ever justifies it. In-memory fallback used when `SQL_CONNECTION_STRING` unset.

Config: `SQL_CONNECTION_STRING`, `INTAKE_BATCH`.

## Test status
12/12 pass (9 gate/bridge + 3 queue). Queue tests cover enqueue→read→ack roundtrip,
dead-letter, and consumer-drains-through-gate.

## User-based access — Entra ID (Azure AD) + RBAC (added)

Replaces the shared API key with real per-user identity, closing the PCI 7/8/10.2 gaps.

- **Authentication:** Entra ID via OIDC/JWT (`Microsoft.Identity.Web`). Configure with
  `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__Audience`. Unset => a dev-only fallback
  authenticates locally as all-roles so the API runs without an IdP (never active once ClientId is set).
- **Roles (Entra app roles → policies):**
  - **Admin** — edit policy, manage sources (PUT /api/policy).
  - **Approver** — grant/revoke exceptions (/api/exceptions), cannot change policy.
  - **Viewer** — read-only (policy, audit, queue, quarantine, evaluate).
- **Attribution (PCI 10.2):** every decision, policy change, exception grant/revoke is stamped
  with the authenticated user (`Actor` on each audit entry); background jobs record "system".
- **Separation of duties (PCI 7.2):** exception granting is a separate Approver-scoped endpoint,
  so an approver need not hold policy-edit rights.

Concurrency was already multi-user (stateless API + SQL queue with READPAST/UPDLOCK); this adds
the per-user identity layer on top.

## Nexus enforcement — wired (added)

The gate is now physically enforced across all five ecosystems via the two-repo model,
automated by `scripts/nexus-setup.sh`:

```
pip install foo  ──>  <eco>-approved (hosted, dev-facing)   ── only vetted packages live here
                          ▲
                          │ PromotionBridge promotes on clean gate decision
                          │
upstream PyPI/npm/… ──> <eco>-quarantine (proxy, NOT dev-facing)  ── new packages land here first
```

Setup (once, after `docker compose up`):
```bash
NEXUS_URL=http://localhost:8081 NEXUS_USER=admin NEXUS_PASS=... ./scripts/nexus-setup.sh
```
This creates `pypi-quarantine`/`pypi-approved`, `npm-quarantine`/`npm-approved`, etc. Then point
developers' package managers at the **-approved** repos only. Developers cannot reach quarantine,
so nothing reaches them until the bridge evaluates it and promotes it.

Two enforcement modes, both supported:
- **Async (Nexus OSS, default):** PromotionBridge polls quarantine, evaluates, promotes/holds.
- **Synchronous inline (Nexus Pro pre-download webhook, or a reverse proxy in front of Nexus):**
  call `POST /api/enforce` — returns 200 to serve, 403 to block. The endpoint already exists.

## Audit immutability — SIEM / WORM (planned approach)

Today the audit ledger is hash-chained (tamper-evident) and mirrored to a pluggable
`IWormSink` (default: append-only file). For production PCI 10.5 (protect audit trail from
modification) we will point `IWormSink` at one of:

- **SIEM forwarding (recommended):** stream every sealed audit line to the bank's existing SIEM
  (Splunk HEC, Microsoft Sentinel via Log Analytics, or syslog/CEF to ArcSight/QRadar). The SIEM
  is already an approved, write-once, retention-governed system of record — so we inherit its
  immutability, alerting, and retention controls rather than building our own. This is a new
  `IWormSink` implementation (~50 lines) posting to the SIEM ingest endpoint; no core changes.
- **Object-lock storage:** write sealed lines to S3 Object Lock (compliance mode) or Azure
  immutable blob, where deletes/overwrites are refused at the storage layer for the retention period.

Why SIEM first: it adds detective control (alert on dead-letter spikes, blocked-package trends,
policy changes by user) on top of immutability, and it lands the trail where SOC analysts already
look — closing PCI 10.5 and strengthening 10.x monitoring in one move. The hash-chain remains the
integrity proof; the SIEM/object-lock provides the tamper-resistance.

## Correction: vulnerability data is open, not a vendor moat

An on-prem **offline mirror** of the vulnerability corpus is supported and recommended for
air-gapped/zero-egress deployment. The full OSV database is published as bulk dumps
(`gs://osv-vulnerabilities`) and runs fully offline; the GitHub Advisory Database, PyPA,
RustSec, OpenSSF Malicious Packages and CISA KEV are all open, OSV-format, and cloneable —
the same authoritative sources the commercial tools ingest. Mirror them inside the perimeter
and the gate keeps detecting with zero external dependency. The only genuine paid-vendor edge
is pre-NVD/zero-day *timing* and proprietary reachability analysis, closed on demand via the
pluggable VulnCheck/Socket sources. (To wire a local OSV mirror, point the OSV source's base
URL at your mirror host instead of api.osv.dev.)

## JFrog-parity capabilities added (and how the intel actually flows)

These four features close most of the operational gap to JFrog Xray's end-to-end experience,
at $0. Each was built, deployed, and verified end-to-end on the running stack.

### How per-package intelligence is sourced (the "where does all this come from" answer)
We do **not** scrape advisory sites daily. We query **OSV.dev**, which federates the *same open
feeds JFrog ingests*: NVD (CVE + CVSS), GitHub Advisory DB (GHSA), PyPA, RustSec, OpenSSF
Malicious Packages, CISA KEV. Each OSV advisory record already carries the CVE aliases, CVSS
vector, CWE ids, publish date, and the full categorized reference list (advisory / patch commit /
exploit PoC / vendor bulletins) — the exact links a tool like Xray shows. We capture and display
that record as-is. JFrog's genuine edge is **timing** (their research team publishes some CVEs
pre-NVD) and **contextual analysis**, not the base data — which is why both are pluggable here
(VulnCheck/Socket) rather than a rebuild.

### 1. Fix-version remediation (`compliant version selection`)
Each finding carries the nearest non-vulnerable upgrade target, parsed from OSV's
`affected[].ranges[].events[].fixed`. Shown per-finding and fed to the AI rationale.

### 2. Rich CVE detail
Findings now carry CVE/GHSA/PYSEC **aliases**, the **CVSS vector** (v3/v4), **CWE** ids,
**publish date**, and **categorized reference links** (Advisory / Exploit (PoC) / Patch / Report /
Web). The console renders an expandable per-finding detail panel mirroring Xray's CVE view.

### 3. Secrets + IaC scanning (`Advanced Security`, step 7)
When artifact bytes are available (`LocalPath`, e.g. the promotion bridge), `SecretScanner`
(AWS/GCP/GitHub keys, private keys, JWTs, Slack tokens) and `IacScanner` (open ingress, public
S3, plaintext creds, privileged/host-network containers, disabled TLS, Docker root) run; High
hits **block** (`SEC-SECRET-01` / `SEC-IAC-01`). Coordinate-only evals report the dimension as
`Skipped` — never a false clean pass. Toggle: `EnableContentScan`.

### 4. Contextual analysis / reachability (npm — the JFrog "moat")
When a consuming project source is supplied (`projectPath`), a bundled acorn-based Node analyzer
builds the first-party import→call graph and marks each finding **Reachable / NotReachable /
Unknown**. With `DowngradeUnreachable` on, a finding *proven* unreachable does not block on its
own. **Honest scope:** single-hop first-party reachability — it removes the "CVE in a package you
never call" false-positive class and is explicit (`Unknown`) on dynamic/namespace imports. It is
**not** a full transitive call graph through dependency internals. Controls: `SEC-REACH-01`
(annotate), `SEC-REACH-02` (downgrade). Runs naturally at the CI/build step where the consuming
repo is checked out (the same integration point as JFrog Frogbot).

### Watches & violations (`governance view`, steps 4–5)
- **Violations** — every Block/Quarantine projected as a structured record (resource, worst
  severity, triggered controls, status Open/Waived, attributed watch). Waived when a matching,
  unexpired exception exists. `GET /api/violations?status=Open|Waived`. Derived from the signed
  ledger + policy, so there is no divergent second source of truth.
- **Watches** — named bindings of a rule-set to a resource scope (default: `PROD-watch`,
  `Security-watch`, `License-watch`), each with rules (type CVEs/Malicious/License; actions
  block/notify). The gate engine enforces; watches scope/label which rules apply where. Watches
  live inside the signed policy, so editing them versions + re-signs the policy.
  `GET /api/watches`.

### New env / policy
- Research agent is now **Groq** (OpenAI-compatible): `GROQ_API_KEY`, `GROQ_MODEL`
  (default `openai/gpt-oss-120b`); deterministic fallback if unset. AI rationale fires **only on
  issues** (non-Allow, findings, triggered controls, or inconclusive coverage) — never on a clean
  Allow.
- Policy flags: `EnableContentScan`, `EnableReachability`, `DowngradeUnreachable`, plus the
  `Watches` list.
