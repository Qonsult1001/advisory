# Advisory — Coverage Reply to PLB-AI-001 (AI Incident Response Playbooks)

**Re:** PLB-AI-001 v1.0 (Draft) · Supplement to POL-AI-001
**Tool:** Advisory — self-hosted AI/ML supply-chain security gate + LLM Gateway
**Date:** 2026-06-11 · **Prepared for:** CISO, Direct Transact (Pty) Ltd

> Honest legend: **✅ Covered** = Advisory does this today. **🟡 Partial** = Advisory does part; a gap or
> integration remains. **⛔ Not in tool** = belongs to other controls (SIEM, EDR, CASB, IAM, banking BAU).
> Advisory is a **preventive gate + evidence source**, not a SIEM/SOAR or the IR process itself.

---

## TL;DR — what Advisory manages

Advisory can **prevent, detect, and produce audit evidence** for large parts of Playbooks 1, 2, and 4,
and the **detection + kill-switch** parts of Playbook 3. It does **not** replace your SIEM, EDR, CASB,
IAM, the IR workflow, or regulatory notification — it **feeds** them. Every gate decision and LLM call is
recorded in a signed, hash-chained audit ledger that serves as PCI DSS Req. 10 evidence.

---

## §6 Tooling Prerequisites — line-by-line


| Prerequisite (from §6)                                | Advisory | Notes                                                                                                                                                                                                                                                           |
| ----------------------------------------------------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Prompt/completion logging to SIEM with PII handling   | 🟡       | LLM Gateway logs every call with a **redacted** transcript (PII/cards/secrets masked) + DLP findings. Stored in-app; **SIEM forwarding is the open integration** (export CSV exists; syslog/webhook to SIEM = to build).                                        |
| Agent action logging (tool, args, result, identity)   | ⛔        | Advisory governs **models and LLM traffic**, not your agent orchestrator's action log. That's Playbook 3 infra you own.                                                                                                                                         |
| Model proxy with hash, signature, **pickle scanning** | ✅        | AI Catalog + WeightVerifier: byte-level pickle/opcode scan, magic-byte verification, Shadow-AI detection. Signature verify = preference where publisher signs.                                                                                                  |
| Vector store IAM with per-user ACLs + access logging  | ⛔        | Not in Advisory — that's your vector DB / IAM.                                                                                                                                                                                                                  |
| CASB/SSE with prompt-level DLP                        | 🟡       | The **LLM Gateway IS the prompt-level DLP** (PII/POPIA-GDPR, cards+Luhn+context, secrets, code, custom rules; on-prem OpenAI Privacy Filter). It is an OpenAI-compatible proxy, not a full CASB — pair with network egress control to force traffic through it. |
| EDR on dev/serving hosts                              | ⛔        | Out of scope — your EDR.                                                                                                                                                                                                                                        |
| Output filter (PII/CHD scrubbing) shared service      | ✅        | DLP inspector scans + redacts; can run inbound/outbound on the gateway.                                                                                                                                                                                         |
| Kill switches (orchestrator + gateway)                | 🟡       | **Gateway kill switch: ✅** (disable gateway / per-provider / per-model deny-list in policy). Orchestrator kill switch = your agent platform.                                                                                                                    |
| AI-specific SIEM use cases                            | 🟡       | Advisory **emits the signals** (DLP blocks, malicious-model detections, registry violations); building the SIEM correlation rules is on the SIEM side.                                                                                                          |
| Evidence vault, 12-month retention (PCI 10.7)         | 🟡       | Signed, hash-chained **audit ledger + WORM sink** exists; confirm 12-month retention config + offsite/immutable backup to fully meet 10.7.                                                                                                                      |


---

## Per-playbook coverage

### Playbook 1 — Prompt Injection

- **Detect:** 🟡 Output-filter alerts (PII/CHD in completions) and prompt-content capture → **Advisory does this** at the gateway. "Ignore previous / reveal system prompt" pattern detection = add as a **custom DLP rule** (✅ supported) or SIEM rule.
- **Contain:** 🟡 Gateway-level block/disable (✅); quarantine a poisoned RAG document = your RAG layer (⛔).
- **Evidence:** ✅ Full prompt envelope + completion + model version + user identity + redacted transcript in the ledger.
- **Verdict:** Advisory covers the **DLP/output-boundary + evidence**; injection-pattern detection is a custom rule; RAG quarantine and SIEM correlation are external.

### Playbook 2 — Model Data Leak

- **Detect:** ✅ Output filter flags PII/CHD in completions not in context (gateway DLP); 🟡 cross-tenant vector retrieval = vector-store logs (⛔).
- **Contain:** ✅ Take model **offline at the gateway** (route/deny); rotate keys is your KMS. Suspend model promotion via the gate (✅).
- **Eradicate:** 🟡 Retire/replace model via AI Catalog registry (✅); differential privacy / retraining = your ML pipeline (⛔).
- **Evidence:** ✅ Model version, deployment ID, registry record, redacted leak artefact, decision ledger.
- **Verdict:** Strong on **gateway containment + model-registry + evidence**; memorisation analysis and vector-store ACLs are external.

### Playbook 3 — AI Agent Misuse

- **Detect:** ⛔ Agent action-log anomalies live in your orchestrator/SIEM, not Advisory.
- **Contain:** 🟡 If the agent calls LLMs **through the Advisory gateway**, you can kill its provider/model access and DLP-block its prompts (✅). The agent **kill switch / token revocation / transaction reversal** are your platform + banking BAU (⛔).
- **Model side:** ✅ If misuse traces to a model, AI Catalog verification/registry applies.
- **Verdict:** Advisory is a **choke point for the agent's LLM traffic**, not the agent governor. Mostly your agent platform + IAM.

### Playbook 4 — Malicious Model Artefact

- **Detect:** ✅ **This is Advisory's core.** Model proxy scans at ingest: pickle/unsafe-deserialisation opcodes, magic-byte format check, Shadow-AI detection of unapproved models in repos, registry allow-list enforcement (SEC-AIML-02), spoofed/typosquat repo flagging.
- **Contain:** ✅ Block artefact by hash/registry; quarantine pickle, promote only safetensors/ONNX; deny-list a publisher. EDR host isolation = external.
- **Eradicate:** ✅ Purge from registry; deny-list publisher; require safetensors. Host rebuild = Platform Eng.
- **Evidence:** ✅ Artefact hash, source, format verdict, byte-level scan result in the ledger.
- **Verdict:** **Best-covered playbook.** Advisory directly implements the model-proxy, pickle-scan, provenance, and Safetensors-preference controls §4.8 asks for.

---

## What Advisory does NOT do (be explicit with the CISO)

- It is **not** the SIEM, EDR, CASB, vector-store, IAM, or agent orchestrator.
- It does **not** perform regulatory notification (POPIA s.22 / GDPR Art.33 / PCI / SARB JS2) — it provides the **evidence** those decisions need.
- It does **not** run the IR lifecycle or the governance feedback loop — it is a **control + evidence source** that those processes consume.

## Open integration gaps to close for full §6 compliance

1. **SIEM forwarding** of LLM-call records + model-scan events (syslog/webhook) — currently in-app + CSV export only.
2. **Retention/immutability**: confirm the audit ledger meets PCI 10.7 (12 months, tamper-evident, backed up).
3. **Injection-pattern DLP rules** ("ignore previous", "reveal system prompt") — add as custom DLP rules.
4. **Egress enforcement** so all LLM traffic is forced through the Advisory gateway (network control, not Advisory).

---

## Questions in the playbook — quick answers

*(Fill the blanks the CISO will ask; honest where the tool doesn't reach.)*

- **Can we capture the full prompt envelope for evidence?** Yes — the gateway stores prompt, completion, model/version, user identity, decision, and a redacted transcript per call.
- **Can we block PII/CHD leaving via an LLM?** Yes — outbound DLP blocks cards (Luhn + context), PII (on-prem model + regex), secrets, source code, and custom patterns, before the call leaves.
- **Can we detect/stop a malicious model file?** Yes — byte-level pickle/opcode scan + format verification at ingest, registry allow-list enforcement, Shadow-AI detection.
- **Can we kill an LLM feature fast?** Yes — gateway enable/disable, per-provider allow-list, model deny-list, all in the signed policy.
- **Does it notify regulators / run IR?** No — it produces the evidence; notification and IR remain your process.

