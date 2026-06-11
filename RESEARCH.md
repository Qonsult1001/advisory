# Research Backlog

The Evolution (research) loop studies the supply-chain security landscape and records findings here
as enhancement candidates. It NEVER edits product code — a human approves a finding in the dashboard,
which files a `mutation` ticket that the bug-fix loop then implements (PR-only).

Each entry is a `### [ ]` checkbox (open) / `### [x]` (closed) with:
- **Section:** which product area it enhances — one of: AppTrust · Xray · Curation · Catalog · AI/ML · Pipeline.
- **Goal:** what to study/build and why.
- **Source:** (optional) arXiv id / advisory / standard / competitor doc that prompted it.

The dashboard groups these by Section. Run research on a **weekly** schedule or via "Run research now".

### [ ] The gate decision pipeline end to end
**Section:** Pipeline
**Goal:** Trace a package from intake → resolve → vuln sources → policy controls → decision →
promotion bridge. Know exactly where a change could alter a security outcome, so I never weaken one
by accident.

### [ ] The signed policy + audit hash-chain
**Section:** Pipeline
**Goal:** Understand how FirewallPolicy is versioned, signed, and how the audit ledger chains. Any
change near here must preserve the integrity guarantees.

### [ ] Test coverage map
**Section:** Curation
**Goal:** Identify which controls have real tests and which don't. Run mutation testing to find
tests that pass but don't actually catch bugs. Prioritise closing those gaps.

### [ ] Audit endpoint/control-state consistency across the API
**Section:** Pipeline
**Goal:** #2 showed two endpoints disagreeing on whether the gateway was active. Sweep the API for
other places where a policy toggle is honored in one path but not a sibling path (models vs chat,
scan vs block, enabled vs listed). A control that's enforced inconsistently is a finding.

### [ ] SLSA Build L3 provenance for the promotion bridge
**Section:** AppTrust
**Source:** SLSA v1.0 (slsa.dev); NIST SP 800-218 SSDF PO.3/PS.2
**Goal:** SLSA L3 wants signed, non-falsifiable build provenance. Our promotion bridge moves
packages quarantine→approved but doesn't attest *why/who*. Study what a signed provenance
attestation on promotion would look like (in-toto/SLSA predicate) so AppTrust can prove chain-of-custody.

### [ ] Model provenance & signed weights in the AI Catalog
**Section:** AI/ML
**Source:** arXiv cs.CR (model supply-chain); Hugging Face model-signing; Sigstore
**Goal:** We verify weight bytes (pickle scan) but not *origin signatures*. Study HF model signing /
Sigstore-for-models so Catalog can verify a model was published by who it claims, not just that the
bytes are structurally safe.

### [ ] Reachability-aware vuln scoring vs competitors
**Section:** Xray
**Source:** Endor Labs / Snyk reachability; arXiv cs.CR call-graph reachability
**Goal:** Competitors down-rank a CVE if the vulnerable symbol isn't reachable. We have a
ReachabilityAnalyzer — study whether our Xray violations could use it to suppress unreachable-CVE
noise (with an audit trail), matching JFrog/Snyk behaviour.
