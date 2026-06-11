# PkgFirewall — Capability Parity & PCI Readiness (one-page summary)

## Verdict
On **detection and audit capability** PkgFirewall reaches parity with Sonatype, JFrog Xray
and Snyk, and exceeds them in three areas. It would **fail a PCI DSS 4.0 audit today** on
three operational items — all closeable, none architectural.

## Feature parity (vs Sonatype / JFrog Xray / Snyk)
- **Parity:** multi-ecosystem SCA, transitive tree, malicious-package detection, SBOM
  (CycloneDX), policy-as-gate, exceptions, tamper-evident audit, ITSM integration, durable queue.
- **We lead:** exploit-probability (EPSS+KEV) gating; coverage-aware fail-closed decisions
  (feed error ⇒ quarantine, never silent allow); real pickle-opcode model-weight scanning;
  AI-written audit rationale.
- **They lead (corrected):** NOT the vulnerability data — the full OSV corpus is downloadable in
  bulk (gs://osv-vulnerabilities, runs offline) and GitHub Advisory DB / PyPA / RustSec / KEV /
  OpenSSF are open OSV-format mirrors, so the complete history can be hosted on-prem with zero
  egress. Vendors only lead on pre-NVD/zero-day *timing* and proprietary reachability/behavioural
  analysis (closed on demand via pluggable VulnCheck/Socket), plus support/SLA/cert and production
  hardening. An on-prem mirror is in fact a resilience + data-residency advantage for a bank.
- **Cost:** Sonatype $60–150k/yr · JFrog $50–130k/yr · Snyk $35–80k/yr · **PkgFirewall ~$0**.
  (JFrog Xray can't be bought standalone — it forces the whole Artifactory platform.)

## PCI DSS 4.0 — where we stand
- **Met:** 6.3.1 (identify/rank vulns), 6.3.2 (component inventory *used* for vuln mgmt —
  mandatory since 31 Mar 2025; our SBOM + enforcing gate satisfies both halves),
  6.4.1 (automated assessment), 10.3 (audit content), 11.3.1.1 (all vulns managed).
- **Partial:** 6.3.3 (we detect/block; patch-SLA tracking lives in ITSM), 10.5 (hash-chain is
  tamper-evident; needs WORM storage to be tamper-resistant).
- **Gap (would fail today):**
  1. **SSO / per-user identity** (PCI 7, 8, 10.2) — shared API key can't attribute actions to a
     person. Highest priority.
  2. **Immutable audit storage** (PCI 10.5) — point the WORM sink at object-lock/SIEM.
  3. **Formal RBAC roles** (PCI 7.2) — map existing write/read split to named roles on SSO identities.

## Recommendation
Pilot in the R&D zone now; fund the three gap items as the costed path to a production,
PCI-assessable deployment. Sign-off claim: *"Functional parity on detection and audit, at zero
licensing, ready to pilot, with a three-item path to PCI compliance."*

Full detail, color-coded matrix and control-by-control mapping: **PkgFirewall_Assessment.docx**.
