---
name: evolve
description: Run one evolution (research) cycle — study the software-supply-chain security landscape (arXiv, NIST SSDF, SLSA, competitors) and record findings to RESEARCH.md/memory. Never edits product code. Use when asked to /evolve or run the research loop.
allowed-tools: Read, Edit, Write, Bash, Glob, Grep, WebSearch, WebFetch
---

# Evolution Cycle (research/landscape — does NOT change product code)

A standalone investigation task. Where the `mutate` skill fixes bugs and broken code from tickets,
EVOLUTION studies the **software-supply-chain security landscape** and records findings into the
backlog so a future `mutate` can act on them. It writes only to `RESEARCH.md` and `memory/` —
never to `src/` or `web/`. Run it deliberately, not on the bug-fixing schedule.

## Scope — keep it grounded in real security/compliance

Research is only useful here if it maps to how this gate is judged. Anchor every search to:

- **Standards & frameworks:** NIST SSDF (SP 800-218), SLSA build-integrity levels, EO 14028,
  SBOM / provenance / attestation, PCI-DSS, SOC 2, ISO 27001 — the obligations Advisory exists to help meet.
- **The competitor frame:** JFrog (Xray / Curation / Catalog / AppTrust), Tenable **Nessus**, Snyk,
  Sonatype Nexus Firewall, Socket, Endor Labs. What do they enforce that we don't yet? Where are we ahead?
- **Primary sources:** arXiv (cs.CR — supply-chain attacks, malicious-package detection, model
  provenance, LLM data-exfiltration), CISA advisories, OpenSSF, real CVE/incident write-ups (xz,
  Shai-Hulud-style npm worms, PyPI typosquats).

Do **not** research generic programming topics. If it doesn't touch supply-chain risk, compliance
evidence, or a control this gate could enforce, it's out of scope.

## Steps

1. **Read context:** `IDENTITY.md` (you think like a compliance officer), `RESEARCH.md` (open
   questions), `memory/active_learnings.md`. Pick ONE open `### [ ]` research gap, or a clearly
   higher-value new topic from the scope above.
2. **Investigate** via web search (arXiv, vendor docs, standards bodies). Read primary sources, not
   summaries. Note specifics: a technique, a control a competitor enforces, a standard's exact
   requirement, an attack class we don't detect.
3. **Record, don't implement:**
   - Use the section-tagged gap format the dashboard parses. Each `### [ ]`/`### [x]` entry carries
     `**Section:**` (AppTrust · Xray · Curation · Catalog · AI/ML · Pipeline), `**Goal:**`, and an
     optional `**Source:**` (arXiv id / advisory / standard / competitor doc).
   - If you closed a gap, check its `### [x]` box and write 2-4 sentences of what you learned and the
     concrete implication for Advisory.
   - If you found a NEW gap or capability worth building, add a `### [ ]` entry with Section + Goal,
     framed as something `mutate` (or a human ticket) could later act on.
   - If genuinely novel, append one line to `memory/learnings.jsonl`.
4. **Commit** only `RESEARCH.md` and `memory/` changes:
   `git add RESEARCH.md memory && git commit -m "evolve: <topic>"`. Push to an `evolution/<topic>`
   branch and open a PR (same PR-only discipline) — or, for a pure backlog note, commit to a branch
   and let a human pull it in.
5. **Report:** the topic, the source(s), the implication for the gate, and any ticket you'd recommend
   filing. Then stop. **You did not change any product code — that is correct.**
