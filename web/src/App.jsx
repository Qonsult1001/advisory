import React, { useState, useEffect, useCallback } from "react";

const API = "http://localhost:5000/api";
const api = {
  getPolicy: () => fetch(`${API}/policy`).then((r) => r.json()),
  putPolicy: (p) => fetch(`${API}/policy`, { method: "PUT",
    headers: { "Content-Type": "application/json" }, body: JSON.stringify(p) }).then((r) => r.json()),
  getSources: () => fetch(`${API}/sources`).then((r) => r.json()),
  getAudit: () => fetch(`${API}/audit?limit=100`).then((r) => r.json()),
  getViolations: () => fetch(`${API}/violations?limit=200`).then((r) => r.json()),
  getWatches: () => fetch(`${API}/watches`).then((r) => r.json()),
  getKev: (q) => fetch(`${API}/kev?limit=100${q ? `&q=${encodeURIComponent(q)}` : ""}`).then((r) => r.json()),
  getSourceHealth: () => fetch(`${API}/sources/health`).then((r) => r.json()),
  getSourcesAdmin: () => fetch(`${API}/sources/admin`).then((r) => r.json()),
  testSource: (key) => fetch(`${API}/sources/test/${encodeURIComponent(key)}`, { method: "POST" }).then((r) => r.json()),
  testCustomSource: (url, credential) => fetch(`${API}/sources/test-custom`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ url, credential }) }).then((r) => r.json()),
  getEcosystems: () => fetch(`${API}/catalog/ecosystems`).then((r) => r.json()),
  getPackage: (eco, name, version) => fetch(`${API}/catalog/package?ecosystem=${eco}&name=${encodeURIComponent(name)}${version ? `&version=${encodeURIComponent(version)}` : ""}`).then((r) => r.json()),
  searchPackages: (eco, q, limit) => fetch(`${API}/catalog/search?ecosystem=${eco}&q=${encodeURIComponent(q)}${limit ? `&limit=${limit}` : ""}`).then((r) => r.json()),
  enqueue: (pkg) => fetch(`${API}/queue/enqueue`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(pkg) }).then((r) => r.json()),
  getQueueDepth: () => fetch(`${API}/queue/depth`).then((r) => r.json()),
  getScans: () => fetch(`${API}/scans/repositories`).then((r) => r.json()),
  getRepoArtifacts: (repo) => fetch(`${API}/scans/repository/${encodeURIComponent(repo)}/artifacts`).then((r) => r.json()),
  getArtifactScan: (repo, eco, name, version, rescan) => fetch(`${API}/scans/artifact?repo=${encodeURIComponent(repo)}&ecosystem=${eco}&name=${encodeURIComponent(name)}&version=${encodeURIComponent(version)}${rescan ? "&rescan=true" : ""}`).then((r) => r.json()),
  getQuarantine: () => fetch(`${API}/quarantine`).then((r) => r.json()),
  getReport: (type) => fetch(`${API}/reports/${type}`).then((r) => r.json()),
  reportCsvUrl: (type) => `${API}/reports/${type}?format=csv`,
  getViolationsDetailed: (watch) => fetch(`${API}/violations/detailed${watch ? `?watch=${encodeURIComponent(watch)}` : ""}`).then((r) => r.json()),
  aiDiscover: (q, sort) => fetch(`${API}/aicatalog/discover?sort=${sort || "downloads"}${q ? `&q=${encodeURIComponent(q)}` : ""}`).then((r) => r.json()),
  aiModel: (id) => fetch(`${API}/aicatalog/model?id=${encodeURIComponent(id)}`).then((r) => r.json()),
  aiRegistry: () => fetch(`${API}/aicatalog/registry`).then((r) => r.json()),
  aiAllow: (id, license, notes) => fetch(`${API}/aicatalog/registry/allow`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ id, license, notes }) }).then((r) => r.json()),
  aiDisallow: (id) => fetch(`${API}/aicatalog/registry?id=${encodeURIComponent(id)}`, { method: "DELETE" }).then((r) => r.json()),
  aiEnforce: (enforce) => fetch(`${API}/aicatalog/enforce`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ enforce }) }).then((r) => r.json()),
  aiDetect: () => fetch(`${API}/aicatalog/detect`).then((r) => r.json()),
  aiVerify: (id) => fetch(`${API}/aicatalog/verify?id=${encodeURIComponent(id)}`).then((r) => r.json()),
  aiVerifyStart: (id) => fetch(`${API}/aicatalog/verify/start?id=${encodeURIComponent(id)}`, { method: "POST" }).then((r) => r.json()),
  aiVerifyStatus: (id) => fetch(`${API}/aicatalog/verify/status?id=${encodeURIComponent(id)}`).then((r) => r.json()),
  aiVerifyEvict: (id) => fetch(`${API}/aicatalog/verify/cache?id=${encodeURIComponent(id)}`, { method: "DELETE" }).then((r) => r.json()),
  aiVerifyJobs: () => fetch(`${API}/aicatalog/verify/jobs`).then((r) => r.json()),
  aiConsume: (id, repo) => fetch(`${API}/aicatalog/consume`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ id, repo }) }).then((r) => r.json()),
  aiConsumeShadow: (id) => fetch(`${API}/aicatalog/consume/shadow`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ id }) }).then((r) => r.json()),
  aiUnconsume: (repo, id) => fetch(`${API}/aicatalog/consume?repo=${encodeURIComponent(repo)}&id=${encodeURIComponent(id)}`, { method: "DELETE" }).then((r) => r.json()),
  evoStatus: () => fetch(`${API}/evolution/status`).then((r) => r.json()),
  evoTickets: () => fetch(`${API}/evolution/tickets`).then((r) => r.json()),
  evoRuns: () => fetch(`${API}/evolution/runs`).then((r) => r.json()),
  evoRun: (id) => fetch(`${API}/evolution/run/${id}`).then((r) => r.json()),
  evolve: (ticket) => fetch(`${API}/evolution/evolve`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ ticket }) }).then((r) => r.json()),
  llmRecords: () => fetch(`${API}/llm/records?limit=200`).then((r) => r.json()),
  llmEngine: () => fetch(`${API}/llm/engine`).then((r) => r.json()),
  llmExportUrl: () => `${API}/llm/export`,
  apps: () => fetch(`${API}/apptrust/applications`).then((r) => r.json()),
  app: (key) => fetch(`${API}/apptrust/application?key=${encodeURIComponent(key)}`).then((r) => r.json()),
  odsList: () => fetch(`${API}/ondemand/list`).then((r) => r.json()),
  odsScan: (pkg) => fetch(`${API}/ondemand/scan`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(pkg) }).then((r) => r.json()),
  getAiSettings: () => fetch(`${API}/ai/settings`).then((r) => r.json()),
  saveAiSettings: (body) => fetch(`${API}/ai/settings`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }).then((r) => r.json()),
  testAi: (body) => fetch(`${API}/ai/test`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body || {}) }).then((r) => r.json()),
  aiChat: (message, history) => fetch(`${API}/ai/chat`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ message, history }) }).then((r) => r.json()),
};

// JFrog Platform palette: white header + sidebar, neutral light tables, GREEN active-nav/accent,
// blue links, dark navy primary buttons. Clean, light, enterprise. (Inter font.)
const FONT_MONO = "ui-monospace,'SFMono-Regular',Menlo,Consolas,monospace";
const FONT_SANS = "'Inter',system-ui,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";
const C = {
  name: "light",
  bg: "#f5f6f8", bg2: "#fafbfc", surface: "#ffffff", surface2: "#f3f5f7",
  ink: "#34373c", sub: "#6e7479", dim: "#a0a4aa",
  line: "#e4e6e9", lineSoft: "#eef0f2",
  accent: "#40be46", accentDim: "#2f9e36",   // JFrog green (active nav/tabs/links)
  headerFrom: "#ffffff", headerTo: "#ffffff", // white header
  block: "#d63649", allow: "#40be46", warn: "#d99016", info: "#1f7fd1", head: "#34373c",
  brand: "#16243a",   // dark navy primary buttons
  navbar: "#16243a",  // dark blue top app-bar
  mono: FONT_MONO, sans: FONT_SANS,
};

const ALL_SOURCES = [
  { key: "osv", label: "OSV.dev", scope: "Multi-ecosystem CVE", tier: "Included" },
  { key: "malware", label: "OpenSSF Malicious Packages", scope: "Typosquat / malicious-package (no CVE)", tier: "Included" },
  { key: "artifactory", label: "JFrog Artifactory scan API", scope: "Cross-referenced CVE scan", tier: "Included" },
  { key: "kev", label: "CISA KEV", scope: "Known-exploited catalog", tier: "Included" },
  { key: "epss", label: "EPSS (FIRST.org)", scope: "Exploit probability", tier: "Included" },
  { key: "vulncheck", label: "VulnCheck", scope: "Pre-NVD / zero-day intel", tier: "Licensed" },
  { key: "socket", label: "Socket (behavioural)", scope: "Install-script / runtime behaviour", tier: "Licensed" },
];

// Why a source is inactive — shown on hover so "Not configured" never reads as "broken".
const SOURCE_HINT = {
  artifactory: "Inactive until ARTIFACTORY_URL + ARTIFACTORY_TOKEN are set (point at your JFrog instance). Not an error.",
  vulncheck: "Licensed feed — activates when VULNCHECK_API_KEY is set. No code change.",
  socket: "Licensed behavioural feed — activates when SOCKET_API_KEY is set.",
};

export default function App() {
  const [policy, setPolicy] = useState(null);
  const [sig, setSig] = useState("");
  const [sources, setSources] = useState([]);
  const [audit, setAudit] = useState([]);
  const [violations, setViolations] = useState([]);
  const [tab, setTabState] = useState(() => (window.location.hash || "").replace("#", "") || "dashboard");
  const setTab = (t) => { try { window.location.hash = t; } catch {} setTabState(t); };
  const [offline, setOffline] = useState(false);
  const [saving, setSaving] = useState(false);
  const [decisionFilter, setDecisionFilter] = useState(null); // null = all; else "Block"|"Allow"|"Quarantine"
  const [askAiOpen, setAskAiOpen] = useState(false);
  const [askAiSeed, setAskAiSeed] = useState(null);
  // Any screen can open the assistant pre-seeded: window.dispatchEvent(new CustomEvent("pkgfw-askai", { detail: question }))
  useEffect(() => {
    const h = (e) => { setAskAiSeed(e.detail || null); setAskAiOpen(true); };
    window.addEventListener("pkgfw-askai", h);
    return () => window.removeEventListener("pkgfw-askai", h);
  }, []);

  // Clicking a KPI card jumps to the ledger filtered to that decision.
  const filterTo = (decision) => { setDecisionFilter(decision); setTab("audit"); };

  const load = useCallback(async () => {
    try {
      const [p, s, a, v] = await Promise.all([api.getPolicy(), api.getSources(), api.getAudit(), api.getViolations()]);
      setPolicy(p.policy); setSig(p.signature); setSources(s); setAudit(a); setViolations(v); setOffline(false);
    } catch {
      setOffline(true); setPolicy(DEMO.policy); setSig("OFFLINE");
      setSources(ALL_SOURCES.map((s) => ({ key: s.key, isAvailable: s.tier === "Included" })));
      setAudit(DEMO.audit); setViolations([]);
    }
  }, []);
  useEffect(() => { load(); }, [load]);
  // Self-heal: while offline (e.g. API container restarting), retry every 10s instead of
  // sitting on sample data until a manual refresh.
  useEffect(() => {
    if (!offline) return;
    const t = setInterval(load, 10000);
    return () => clearInterval(t);
  }, [offline, load]);

  const save = async () => {
    setSaving(true);
    try { const r = await api.putPolicy(policy); setPolicy(r.policy); setSig(r.signature); } catch {}
    setSaving(false);
  };
  const set = (k, v) => setPolicy((p) => ({ ...p, [k]: v }));
  const setW = (k, v) => setPolicy((p) => ({ ...p, weights: { ...p.weights, [k]: v } }));

  if (!policy) return (
    <div style={{ minHeight: "100vh", background: C.bg, color: C.sub, fontFamily: C.sans,
      display: "grid", placeItems: "center" }}>
      <style>{FONTS}</style>
      <div style={{ display: "flex", alignItems: "center", gap: 10, fontSize: 14 }}>
        <span style={{ width: 10, height: 10, borderRadius: "50%", background: C.accent,
          boxShadow: `0 0 12px ${C.accent}`, animation: "fwpulse 1.2s infinite" }} />
        Loading console…
      </div>
    </div>
  );

  const stats = computeStats(audit);

  return (
    <div style={{ minHeight: "100vh", color: C.ink, fontFamily: C.sans, fontSize: 13, background: C.bg }}>
      <style>{FONTS}</style>

      {/* Top app-bar — JFrog Platform style */}
      <div style={s.topbar}>
        <div style={{ display: "flex", alignItems: "center", gap: 26 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 9 }}>
            <div style={s.logo}>⊻</div>
            <span style={{ fontWeight: 700, fontSize: 15 }}><span style={{ color: "#fff" }}>Advi</span><span style={{ color: "#5fd968" }}>sory</span></span>
          </div>
          <div style={{ display: "flex", gap: 4 }}>
            <span style={s.appTabOn}>Platform</span>
            <span style={s.appTab}>Administration</span>
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
          <div style={s.globalSearch}><span style={{ color: "rgba(255,255,255,.6)", fontSize: 12 }}>⌕</span>
            <input placeholder="Search packages, CVEs…" style={{ border: "none", outline: "none", background: "transparent", fontSize: 12.5, fontFamily: C.sans, color: "#fff", width: 180 }} /></div>
          <DownloadsIndicator onOpen={() => setTab("aicatalog")} />
          <button style={s.askAi} onClick={() => setAskAiOpen(true)}>✦ Ask AI</button>
          <Status ok={!offline} />
          <div style={{ textAlign: "right" }}>
            <div style={s.policyVer}>Policy v{policy.version}</div>
            <div style={s.sig}>SHA-256 {String(sig).slice(0, 12)}</div>
          </div>
          <div style={s.avatar}>W</div>
        </div>
      </div>

      <div style={s.body}>
        {/* Side nav — grouped JFrog-style sections */}
        <nav style={s.nav}>
          <div style={s.projectSel}>
            <span style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <span style={{ width: 18, height: 18, borderRadius: 4, background: C.accent, display: "grid", placeItems: "center", color: "#fff", fontSize: 10 }}>◇</span>
              All Projects</span>
            <span style={{ color: "rgba(255,255,255,.5)", fontSize: 9 }}>▾</span>
          </div>
          <NavGroups tab={tab} setTab={setTab} />
        </nav>

        {/* Content */}
        <main style={s.main}>
          {tab === "dashboard" && (
            <Dashboard stats={stats} policy={policy} violations={violations} sources={sources}
              countControls={countControls} filterTo={filterTo} setTab={setTab} />
          )}

          {tab === "controls" && (<>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 14 }}>
              <div style={{ fontSize: 13, color: C.sub }}>Commit increments the version, re-signs, and writes to the ledger.</div>
              <button onClick={save} disabled={saving} style={s.save}>{saving ? "Signing…" : "Commit & sign policy"}</button>
            </div>
            <Card title="Policy controls" desc="Each control maps to a named standard reference. The committed, signed policy is the audit artifact.">
              <Table cols={["Control", "Rule", "Setting"]}>
                <Ctl id="SEC-VULN-01" rule="Block when CVSS base score at or above">
                  <Stepper value={policy.cvssBlockThreshold} step={0.5} min={0} max={10}
                    onChange={(v) => set("cvssBlockThreshold", v)} unit="/ 10" /></Ctl>
                <Ctl id="SEC-VULN-02" rule="Block components on the known-exploited catalogue">
                  <Switch on={policy.blockKnownExploited} onChange={(v) => set("blockKnownExploited", v)} /></Ctl>
                <Ctl id="SEC-VULN-03" rule="Block when exploit probability (EPSS) at or above">
                  <Stepper value={policy.epssBlockThreshold} step={0.05} min={0} max={1}
                    onChange={(v) => set("epssBlockThreshold", v)} unit="prob" /></Ctl>
                <Ctl id="SEC-SC-01" rule="Minimum published age before promotion">
                  <Stepper value={policy.minPackageAgeDays} step={1} min={0} max={90}
                    onChange={(v) => set("minPackageAgeDays", v)} unit="days" /></Ctl>
                <Ctl id="SEC-SC-02" rule="Maximum transitive resolution depth">
                  <Stepper value={policy.maxTreeDepth} step={1} min={1} max={20}
                    onChange={(v) => set("maxTreeDepth", v)} unit="levels" /></Ctl>
                <Ctl id="LEG-LIC-01" rule="Prohibited licences">
                  <Chips tags={policy.licenseBlocklist} onChange={(v) => set("licenseBlocklist", v)} /></Ctl>
                <Ctl id="SEC-OPR-01" rule="On High operational risk (EOL / stale / unhealthy project)">
                  <div style={{ display: "flex", gap: 6 }}>
                    {["Disabled", "Notify", "Block"].map((o) => (
                      <button key={o} onClick={() => set("operationalRiskAction", o)}
                        style={{ fontSize: 11.5, fontWeight: 600, padding: "5px 12px", borderRadius: 6, cursor: "pointer",
                          border: `1px solid ${policy.operationalRiskAction === o ? C.accent : C.line}`,
                          background: policy.operationalRiskAction === o ? "rgba(64,190,70,.1)" : C.surface,
                          color: policy.operationalRiskAction === o ? C.accentDim : C.sub }}>{o}</button>
                    ))}
                  </div></Ctl>
                <Ctl id="SEC-OSSF-01" rule="Block when OpenSSF Scorecard score below (0 = off)">
                  <Stepper value={policy.minScorecardScore ?? 0} step={0.5} min={0} max={10}
                    onChange={(v) => set("minScorecardScore", v)} unit="/ 10" /></Ctl>
              </Table>
              <SubHead>SEC-AIML-01 — Model-weight controls</SubHead>
              <Table cols={["Control", "Rule", "Setting"]}>
                <Ctl id="" rule="Permit safetensors format only">
                  <Switch on={policy.weights.safetensorsOnly} onChange={(v) => setW("safetensorsOnly", v)} /></Ctl>
                <Ctl id="" rule="Block pickle-based formats / scan opcodes">
                  <Switch on={policy.weights.blockPickle} onChange={(v) => setW("blockPickle", v)} /></Ctl>
                <Ctl id="" rule="Require SHA-256 hash pin">
                  <Switch on={policy.weights.requireHashPin} onChange={(v) => setW("requireHashPin", v)} /></Ctl>
              </Table>
              <SubHead>SEC-SECRET-01 / SEC-IAC-01 — Artifact content scanning</SubHead>
              <Table cols={["Control", "Rule", "Setting"]}>
                <Ctl id="SEC-SECRET-01" rule="Scan artifact content for embedded secrets + IaC misconfigurations (blocks on High)">
                  <Switch on={policy.enableContentScan} onChange={(v) => set("enableContentScan", v)} /></Ctl>
              </Table>
            </Card>
          </>)}

          {tab === "sources" && (
            <Card title="Intelligence sources" desc="Feeds operate behind a single resolver interface. Included feeds carry no licence cost; licensed feeds activate on credential without code change. Run a live health probe to see real reachability + latency.">
              <Sources sources={sources} policy={policy} set={set} setPolicy={setPolicy} />
              <Callout>Included feeds lag proprietary research and will miss some zero-days — an accepted
                residual risk for production risk-tiering. Closing the gap is a credential change
                (set <code style={s.code}>VULNCHECK_API_KEY</code>), not a redevelopment.</Callout>
              <SubHead>SEC-COV — Coverage & uncertainty controls</SubHead>
              <Table cols={["Control", "Rule", "Setting"]}>
                <Ctl id="SEC-COV-01" rule="Sources required to be conclusive for a clean Allow">
                  <span style={{ fontFamily: C.mono, fontSize: 11 }}>{(policy.requiredSources||["osv"]).join(", ")}</span></Ctl>
                <Ctl id="SEC-COV-02" rule="Quarantine (not allow) when a required source is inconclusive">
                  <Switch on={policy.quarantineOnUncertainty} onChange={(v)=>set("quarantineOnUncertainty", v)} /></Ctl>
                <Ctl id="SEC-AUD-03" rule="Research agent writes an audit rationale per decision">
                  <Switch on={policy.enableResearchAgent} onChange={(v)=>set("enableResearchAgent", v)} /></Ctl>
                <Ctl id="SEC-REACH-01" rule="Contextual analysis: annotate npm findings with reachability (needs consuming project)">
                  <Switch on={policy.enableReachability} onChange={(v)=>set("enableReachability", v)} /></Ctl>
                <Ctl id="SEC-REACH-02" rule="Downgrade: a finding proven not-reachable does not block on its own">
                  <Switch on={policy.downgradeUnreachable} onChange={(v)=>set("downgradeUnreachable", v)} /></Ctl>
              </Table>
              <SubHead>AI assistant — Groq (OpenAI-compatible)</SubHead>
              <AiSettingsPanel />
            </Card>
          )}

          {tab === "watches" && <WatchesPolicies policy={policy} setPolicy={setPolicy} onViewKev={() => setTab("kev")} save={save} saving={saving} />}

          {tab === "ondemand" && <OnDemandScanning />}

          {tab === "kev" && <KevCatalog />}

          {tab === "catalog" && <Catalog />}

          {tab === "evolution" && <Evolution />}

          {tab === "aiml" && <AimlOverview setTab={setTab} />}
          {tab === "airegistry" && <AiCatalog initialTab="registry" setTab={setTab} />}
          {tab === "aidiscovery" && <AiCatalog initialTab="discovery" setTab={setTab} />}
          {tab === "aidetection" && <AiCatalog initialTab="detection" setTab={setTab} />}
          {tab === "aicatalog" && <AiCatalog initialTab="registry" setTab={setTab} />}

          {tab === "llmgateway" && <LlmGateway policy={policy} setPolicy={setPolicy} save={save} saving={saving} />}

          {tab === "applications" && <Applications />}

          {tab === "unifiedpolicies" && <UnifiedPolicies policy={policy} setTab={setTab} />}

          {tab === "waivers" && <Waivers policy={policy} setPolicy={setPolicy} />}

          {tab === "queue" && <IntakeQueue />}

          {tab === "scans" && <ScansList />}

          {tab === "quarantine" && <Quarantine />}

          {tab === "violations" && <WatchViolations policy={policy} setPolicy={setPolicy} rows={violations} />}

          {tab === "reports" && <Reports />}

          {tab === "exceptions" && <Exceptions policy={policy} setPolicy={setPolicy} />}

          {tab === "audit" && (() => {
            const rows = decisionFilter ? audit.filter((e) => e.decision === decisionFilter) : audit;
            return (
            <Card title="Decision ledger" desc="Append-only, hash-chained. Each row records policy version, full source coverage, and an audit rationale. Click a row to expand.">
              {decisionFilter && (
                <div style={s.filterBar}>
                  <span style={{ color: C.sub }}>Filtered to</span>
                  <span style={{ ...s.filterChip,
                    color: decisionFilter === "Block" ? C.block : decisionFilter === "Quarantine" ? C.warn : C.allow }}>
                    {decisionFilter}
                    <button onClick={() => setDecisionFilter(null)} style={s.filterX} title="Clear filter">×</button>
                  </span>
                  <span style={{ color: C.sub }}>· {rows.length} of {audit.length}</span>
                </div>
              )}
              <Table cols={["Component", "Decision", "Tree", "Coverage", "Timestamp"]}>
                {rows.length === 0 && <tr><td style={s.td} colSpan={5}>
                  {decisionFilter ? `No ${decisionFilter} decisions recorded.` : "No entries."}</td></tr>}
                {rows.map((e) => <LedgerRow key={e.id} e={e} />)}
              </Table>
            </Card>
            );
          })()}
        </main>
      </div>

      {askAiOpen && <AskAi initial={askAiSeed} onClose={() => { setAskAiOpen(false); setAskAiSeed(null); }} goSettings={() => { setAskAiOpen(false); setTab("sources"); }} />}
    </div>
  );
}

// ── Markdown renderer ─────────────────────────────────────────────────────────
// Lightweight, dependency-free renderer for assistant replies: headings, bold,
// inline code, bullet/ordered lists, and GitHub tables. Keeps the no-library convention.
function mdInline(text) {
  // split on `code` and **bold** while keeping plain runs; returns React nodes
  const out = [];
  let rest = text, key = 0;
  const re = /(`[^`]+`|\*\*[^*]+\*\*|\*[^*]+\*)/;
  let m;
  while ((m = rest.match(re))) {
    if (m.index > 0) out.push(rest.slice(0, m.index));
    const tok = m[0];
    if (tok.startsWith("`")) out.push(<code key={key++} style={s.mdCode}>{tok.slice(1, -1)}</code>);
    else if (tok.startsWith("**")) out.push(<strong key={key++}>{tok.slice(2, -2)}</strong>);
    else out.push(<em key={key++}>{tok.slice(1, -1)}</em>);
    rest = rest.slice(m.index + tok.length);
  }
  if (rest) out.push(rest);
  return out;
}
function Markdown({ text }) {
  const lines = String(text || "").replace(/\r/g, "").split("\n");
  const blocks = [];
  let i = 0, key = 0;
  const isTableSep = (l) => /^\s*\|?[\s:|-]+\|?\s*$/.test(l) && l.includes("-");
  while (i < lines.length) {
    const line = lines[i];
    // table: header row + separator + body rows
    if (line.trim().startsWith("|") && i + 1 < lines.length && isTableSep(lines[i + 1])) {
      const cells = (l) => l.trim().replace(/^\||\|$/g, "").split("|").map((c) => c.trim());
      const head = cells(line);
      i += 2;
      const rows = [];
      while (i < lines.length && lines[i].trim().startsWith("|")) { rows.push(cells(lines[i])); i++; }
      blocks.push(
        <div key={key++} style={{ overflowX: "auto", margin: "8px 0" }}>
          <table style={s.mdTable}><thead><tr>{head.map((h, x) => <th key={x} style={s.mdTh}>{mdInline(h)}</th>)}</tr></thead>
            <tbody>{rows.map((r, y) => <tr key={y}>{r.map((c, x) => <td key={x} style={s.mdTd}>{mdInline(c)}</td>)}</tr>)}</tbody></table>
        </div>);
      continue;
    }
    // headings
    const h = line.match(/^(#{1,4})\s+(.*)$/);
    if (h) { const lvl = h[1].length; blocks.push(<div key={key++} style={{ fontWeight: 700, fontSize: lvl <= 2 ? 14 : 13, margin: "10px 0 4px" }}>{mdInline(h[2])}</div>); i++; continue; }
    // unordered list
    if (/^\s*[-*]\s+/.test(line)) {
      const items = [];
      while (i < lines.length && /^\s*[-*]\s+/.test(lines[i])) { items.push(lines[i].replace(/^\s*[-*]\s+/, "")); i++; }
      blocks.push(<ul key={key++} style={s.mdList}>{items.map((it, x) => <li key={x} style={{ marginBottom: 3 }}>{mdInline(it)}</li>)}</ul>);
      continue;
    }
    // ordered list
    if (/^\s*\d+\.\s+/.test(line)) {
      const items = [];
      while (i < lines.length && /^\s*\d+\.\s+/.test(lines[i])) { items.push(lines[i].replace(/^\s*\d+\.\s+/, "")); i++; }
      blocks.push(<ol key={key++} style={s.mdList}>{items.map((it, x) => <li key={x} style={{ marginBottom: 3 }}>{mdInline(it)}</li>)}</ol>);
      continue;
    }
    // blank line
    if (line.trim() === "") { i++; continue; }
    // paragraph (consume consecutive non-special lines)
    const para = [line]; i++;
    while (i < lines.length && lines[i].trim() !== "" && !/^\s*[-*]\s+/.test(lines[i]) && !/^\s*\d+\.\s+/.test(lines[i])
      && !/^(#{1,4})\s+/.test(lines[i]) && !lines[i].trim().startsWith("|")) { para.push(lines[i]); i++; }
    blocks.push(<p key={key++} style={{ margin: "4px 0" }}>{mdInline(para.join(" "))}</p>);
  }
  return <div style={{ display: "flex", flexDirection: "column" }}>{blocks}</div>;
}

// ── Ask AI assistant ──────────────────────────────────────────────────────────
// Slide-out panel modeled on JFrog's AI Assistant: suggested-prompt cards + chat,
// grounded server-side in the live policy + recent gate decisions (Groq).
const AI_SUGGESTIONS = [
  { icon: "◎", title: "CVEs Prioritization", q: "Which vulnerabilities in this environment are actually reachable and high-priority?" },
  { icon: "🛡", title: "Infrastructure Security", q: "Where are the detected secrets and IaC configuration risks across these repositories?" },
  { icon: "⟳", title: "Release Lifecycle", q: "What is the promotion history and audit trail for recently evaluated artifacts?" },
  { icon: "§", title: "License Compliance", q: "Which artifacts have unapproved or high-risk licenses?" },
];
function AskAi({ onClose, goSettings, initial }) {
  const [settings, setSettings] = useState(null);
  const [msgs, setMsgs] = useState([]); // {role, content}
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const endRef = React.useRef(null);
  const seeded = React.useRef(false);

  useEffect(() => { api.getAiSettings().then(setSettings).catch(() => setSettings({ assistantEnabled: false, configured: false })); }, []);
  useEffect(() => { endRef.current?.scrollIntoView({ behavior: "smooth" }); }, [msgs, busy]);
  // Auto-send a seeded question (e.g. "Ask AI about this model") once the assistant is ready.
  useEffect(() => {
    if (initial && !seeded.current && settings?.configured && settings?.assistantEnabled) {
      seeded.current = true; send(initial);
    }
  }, [initial, settings]);

  const send = async (text) => {
    const q = (text ?? input).trim();
    if (!q || busy) return;
    const history = msgs.slice(-6);
    setMsgs((m) => [...m, { role: "user", content: q }]);
    setInput(""); setBusy(true);
    try {
      const r = await api.aiChat(q, history);
      setMsgs((m) => [...m, { role: "assistant", content: r.reply || "(no response)", model: r.model, ok: r.ok }]);
    } catch {
      setMsgs((m) => [...m, { role: "assistant", content: "Could not reach the assistant. Check the API is running.", ok: false }]);
    } finally { setBusy(false); }
  };

  const configured = settings?.configured;
  const enabled = settings?.assistantEnabled;
  return (
    <div style={s.aiPanel}>
      <div style={s.aiHead}>
        <div style={{ display: "flex", alignItems: "center", gap: 9 }}>
          <span style={{ color: C.accent, fontSize: 16 }}>✦</span>
          <b style={{ fontSize: 14 }}>Advisory AI</b>
          <span style={s.aiBeta}>Beta</span>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          {msgs.length > 0 && <button style={s.aiNewChat} onClick={() => { setMsgs([]); setInput(""); }} title="New chat">✎ New chat</button>}
          <button style={s.modalX} onClick={onClose}>×</button>
        </div>
      </div>

      {settings && !configured && (
        <div style={s.aiBanner}>
          AI is not configured. <a style={s.aiLink} onClick={goSettings}>Add a Groq API key</a> under Intelligence sources → AI assistant.
        </div>
      )}
      {settings && configured && !enabled && (
        <div style={s.aiBanner}>The assistant is disabled. <a style={s.aiLink} onClick={goSettings}>Enable it</a> in settings.</div>
      )}

      <div style={s.aiBody}>
        {msgs.length === 0 ? (
          <>
            <div style={{ textAlign: "center", padding: "26px 0 6px" }}>
              <div style={{ fontSize: 26, fontWeight: 700, color: C.ink }}>Welcome to <span style={{ color: C.accent }}>Advisory AI</span></div>
              <div style={{ color: C.sub, marginTop: 4 }}>What are you interested in?</div>
            </div>
            <div style={{ display: "grid", gap: 12, padding: "16px 4px" }}>
              {AI_SUGGESTIONS.map((sug) => (
                <button key={sug.title} style={s.aiCard} disabled={!configured || !enabled} onClick={() => send(sug.q)}>
                  <div style={{ display: "flex", alignItems: "center", gap: 8, fontWeight: 600, color: C.ink }}>
                    <span style={{ color: C.accent }}>{sug.icon}</span>{sug.title}</div>
                  <div style={{ color: C.sub, fontSize: 12.5, marginTop: 6 }}>{sug.q}</div>
                </button>
              ))}
            </div>
          </>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 12, padding: "6px 2px" }}>
            {msgs.map((m, i) => (
              <div key={i} style={{ alignSelf: m.role === "user" ? "flex-end" : "flex-start", maxWidth: "90%" }}>
                <div style={m.role === "user" ? s.aiUser : { ...s.aiAsst, ...(m.ok === false ? { color: C.block, borderColor: "rgba(214,54,73,.3)" } : {}) }}>
                  {m.role === "user" ? m.content : <Markdown text={m.content} />}
                </div>
                {m.role === "assistant" && m.model && <div style={{ fontSize: 10, color: C.dim, marginTop: 3 }}>{m.model}</div>}
              </div>
            ))}
            {busy && <div style={{ alignSelf: "flex-start" }}><div style={s.aiAsst}><span style={s.aiDots}>● ● ●</span></div></div>}
            <div ref={endRef} />
          </div>
        )}
      </div>

      <div style={s.aiInputBar}>
        <input style={s.aiInput} placeholder="Ask anything…" value={input}
          disabled={!configured || !enabled}
          onChange={(e) => setInput(e.target.value)} onKeyDown={(e) => e.key === "Enter" && send()} />
        <button style={s.aiSend} disabled={!configured || !enabled || busy || !input.trim()} onClick={() => send()}>↑</button>
      </div>
      <div style={{ fontSize: 10.5, color: C.dim, padding: "0 16px 12px", textAlign: "center" }}>
        AI responses may be inaccurate. Grounded in your live policy + recent decisions. Verify before acting.
      </div>
    </div>
  );
}

// ── icons ───────────────────────────────────────────────────────────────────
// Inline SVG icon set (no icon library — keeps the no-dependency convention). 16px, stroke-based.
const ICON_PATHS = {
  shield: "M12 3l7 3v5c0 4.5-3 8-7 9-4-1-7-4.5-7-9V6l7-3z",
  cube: "M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3zM12 12l8-4.5M12 12v9M12 12L4 7.5",
  brain: "M9 3a3 3 0 00-3 3 3 3 0 00-2 5 3 3 0 002 5 3 3 0 006 0V4a3 3 0 00-3-1zM15 3a3 3 0 013 3 3 3 0 012 5 3 3 0 01-2 5 3 3 0 01-6 0",
  download: "M12 3v12m0 0l-4-4m4 4l4-4M4 17v2a1 1 0 001 1h14a1 1 0 001-1v-2",
  scan: "M4 7V5a1 1 0 011-1h2M4 17v2a1 1 0 001 1h2M20 7V5a1 1 0 00-1-1h-2M20 17v2a1 1 0 01-1 1h-2M3 12h18",
  card: "M3 6h18v12H3zM3 10h18",
  key: "M15 7a4 4 0 11-4 4l-7 7v3h3l1-1h2v-2h2l2-2",
  user: "M12 12a4 4 0 100-8 4 4 0 000 8zM4 20c0-4 4-6 8-6s8 2 8 6",
  code: "M9 8l-4 4 4 4M15 8l4 4-4 4",
  check: "M5 12l5 5L20 6",
  alert: "M12 3l9 16H3zM12 9v5M12 17v.5",
  search: "M11 4a7 7 0 100 14 7 7 0 000-14zM20 20l-3.5-3.5",
  layers: "M12 3l9 5-9 5-9-5 9-5zM3 13l9 5 9-5",
  gateway: "M4 12h4l2-5 4 10 2-5h4",
};
function Icon({ name, size = 16, color = "currentColor", style }) {
  const d = ICON_PATHS[name];
  if (!d) return null;
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color}
    strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" style={{ flexShrink: 0, ...style }}><path d={d} /></svg>;
}

// Global background-jobs indicator in the top bar: shows active model verifications + progress
// from anywhere, so you don't have to sit on the model card waiting for a multi-GB download.
function DownloadsIndicator({ onOpen }) {
  const [jobs, setJobs] = useState([]);
  const [open, setOpen] = useState(false);
  useEffect(() => {
    let on = true;
    const load = () => api.aiVerifyJobs().then((r) => { if (on) setJobs(r.jobs || []); }).catch(() => {});
    load(); const t = setInterval(load, 2000);
    return () => { on = false; clearInterval(t); };
  }, []);
  const active = jobs.filter((j) => j.status === "running");
  if (jobs.length === 0) return null;
  return (
    <div style={{ position: "relative" }}>
      <button style={{ ...s.askAi, background: active.length ? "#1f7fd1" : "rgba(255,255,255,.12)", color: "#fff",
        display: "flex", alignItems: "center", gap: 6 }} onClick={() => setOpen((o) => !o)}>
        <Icon name="download" size={13} color="#fff" style={active.length ? { animation: "fwpulse 1.2s infinite" } : {}} />
        {active.length ? `${active.length} verifying` : "Verifications"}
      </button>
      {open && (
        <div style={{ position: "absolute", top: "calc(100% + 8px)", right: 0, zIndex: 70, width: 360,
          background: C.surface, border: `1px solid ${C.line}`, borderRadius: 12, boxShadow: "0 16px 44px rgba(0,0,0,.18)", padding: 12 }}>
          <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
            <b style={{ fontSize: 12.5, color: C.ink }}>Model weight verification</b>
            <a style={s.linkGreen} onClick={() => { setOpen(false); onOpen(); }}>Open AI Catalog</a>
          </div>
          {jobs.slice(0, 8).map((j) => (
            <div key={j.modelId} style={{ padding: "7px 0", borderBottom: `1px solid ${C.lineSoft}` }}>
              <div style={{ display: "flex", justifyContent: "space-between", fontSize: 11.5 }}>
                <span style={{ fontFamily: C.mono, color: C.ink, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", maxWidth: 220 }}>{j.modelId}</span>
                <span style={{ color: j.status === "done" ? C.accentDim : C.info, fontWeight: 700 }}>
                  {j.status === "done" ? "✓ done" : `${j.done}/${j.total}`}</span>
              </div>
              {j.status === "running" && (
                <div style={{ height: 3, borderRadius: 2, background: C.lineSoft, marginTop: 5, overflow: "hidden" }}>
                  <div style={{ width: `${j.percent}%`, height: "100%", background: C.info }} /></div>
              )}
              <div style={{ fontSize: 10, color: C.sub, marginTop: 3 }}>
                {j.cachedBytes ? `${Math.round(j.cachedBytes / 1048576)} MB cached` : ""}{j.downloading ? ` · ${j.downloading} downloading` : ""}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── primitives ────────────────────────────────────────────────────────────────
function Status({ ok }) {
  // Sits on the dark-blue top bar — light text, status-colored dot.
  const dot = ok ? "#5fd968" : "#fbbf24";
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 7, fontSize: 12, color: "rgba(255,255,255,.8)" }}>
      <span style={{ width: 8, height: 8, borderRadius: "50%", background: dot, animation: "fwpulse 2.4s infinite" }} />
      {ok ? "Connected" : "API offline · sample data"}
    </div>
  );
}

// Grouped left nav modeled on JFrog's platform sidebar: top-level product groups, each expanding
// to its sub-items. Xray mirrors the demo exactly (4 items); everything else lives in its own group.
const NAV = [
  { type: "item", key: "dashboard", label: "Dashboard", icon: "▤" },
  { type: "group", key: "apptrust", label: "AppTrust", icon: "◈", children: [
    ["applications", "Applications"], ["unifiedpolicies", "Unified Policies"], ["waivers", "Waivers"],
  ]},
  { type: "group", key: "xray", label: "Xray", icon: "◉", children: [
    ["scans", "Scans List"], ["violations", "Watch Violations"],
    ["ondemand", "On-Demand Scanning"], ["watches", "Watches & Policies"],
  ]},
  { type: "group", key: "curation", label: "Curation", icon: "⊜", children: [
    ["controls", "Policy controls"], ["sources", "Intelligence sources"], ["kev", "Known-exploited (KEV)"],
  ]},
  { type: "item", key: "catalog", label: "Catalog", icon: "▦" },
  { type: "group", key: "aiml", label: "AI/ML", icon: "✦", children: [
    ["aiml", "Overview"], ["airegistry", "Model Registry"], ["aidiscovery", "Discover Models"],
    ["aidetection", "Shadow AI"], ["llmgateway", "LLM Gateway"],
  ]},
  { type: "group", key: "pipeline", label: "Pipeline", icon: "⇄", children: [
    ["queue", "Intake queue"], ["quarantine", "Quarantine"], ["reports", "Reports"],
    ["exceptions", "Approved exceptions"], ["audit", "Decision ledger"],
  ]},
  { type: "item", key: "evolution", label: "Mutation", icon: "brain" },
];
const NAV_PARENT = (() => { const m = {}; NAV.forEach(g => g.children?.forEach(([k]) => m[k] = g.key)); return m; })();

function NavGroups({ tab, setTab }) {
  const [open, setOpen] = useState(() => {
    const init = {}; NAV.forEach(g => { if (g.type === "group") init[g.key] = true; }); return init;
  });
  // Auto-expand the group containing the active tab.
  useEffect(() => { const p = NAV_PARENT[tab]; if (p) setOpen(o => ({ ...o, [p]: true })); }, [tab]);
  const toggle = (k) => setOpen(o => ({ ...o, [k]: !o[k] }));
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
      {NAV.map(g => g.type === "item" ? (
        <button key={g.key} onClick={() => setTab(g.key)} style={{ ...s.navItem, ...(tab === g.key ? s.navOn : {}) }}>
          <span style={{ width: 18, display: "inline-flex", justifyContent: "center", color: tab === g.key ? "#5fd968" : "rgba(255,255,255,.55)" }}>
            {ICON_PATHS[g.icon] ? <Icon name={g.icon} size={15} color={tab === g.key ? "#5fd968" : "rgba(255,255,255,.55)"} /> : g.icon}</span>{g.label}</button>
      ) : (
        <div key={g.key}>
          <button onClick={() => toggle(g.key)} style={s.navGroupHead}>
            <span style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <span style={{ width: 18, textAlign: "center", color: "rgba(255,255,255,.45)", fontSize: 12 }}>{g.icon}</span>{g.label}</span>
            <span style={{ fontSize: 9, color: "rgba(255,255,255,.4)", transition: "transform .12s", transform: open[g.key] ? "none" : "rotate(-90deg)" }}>▾</span>
          </button>
          {open[g.key] && g.children.map(([k, l]) => (
            <button key={k} onClick={() => setTab(k)} style={{ ...s.navSub, ...(tab === k ? s.navSubOn : {}) }}>{l}</button>
          ))}
        </div>
      ))}
    </div>
  );
}

// Dashboard landing — at-a-glance posture: KPIs, recent violations, source snapshot.
// ── Lightweight inline-SVG charts (no library) ────────────────────────────────
function useCountUp(target, ms = 700) {
  const [v, setV] = useState(0);
  useEffect(() => {
    let raf, start; const from = 0;
    const tick = (t) => { start ??= t; const p = Math.min(1, (t - start) / ms);
      setV(Math.round(from + (target - from) * (1 - Math.pow(1 - p, 3)))); if (p < 1) raf = requestAnimationFrame(tick); };
    raf = requestAnimationFrame(tick); return () => cancelAnimationFrame(raf);
  }, [target]);
  return v;
}
// Donut chart from [{value,color,label}]
function Donut({ data, size = 150, thickness = 20, center }) {
  const total = data.reduce((s, d) => s + d.value, 0) || 1;
  const r = (size - thickness) / 2, cx = size / 2, circ = 2 * Math.PI * r;
  let offset = 0;
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
      <circle cx={cx} cy={cx} r={r} fill="none" stroke={C.lineSoft} strokeWidth={thickness} />
      {data.map((d, i) => {
        const len = (d.value / total) * circ; const el = (
          <circle key={i} cx={cx} cy={cx} r={r} fill="none" stroke={d.color} strokeWidth={thickness}
            strokeDasharray={`${len} ${circ - len}`} strokeDashoffset={-offset}
            transform={`rotate(-90 ${cx} ${cx})`} style={{ transition: "stroke-dasharray .8s ease" }} />
        ); offset += len; return el;
      })}
      {center && <text x={cx} y={cx - 4} textAnchor="middle" fontSize="26" fontWeight="700" fill={C.ink} fontFamily={C.sans}>{center.top}</text>}
      {center && <text x={cx} y={cx + 16} textAnchor="middle" fontSize="11" fill={C.sub} fontFamily={C.sans}>{center.bottom}</text>}
    </svg>
  );
}
// Smooth area+line sparkline from numbers
function AreaChart({ values, w = 560, h = 120, color = C.accent }) {
  const max = Math.max(...values, 1), min = 0;
  const pts = values.map((v, i) => [ (i / (values.length - 1)) * w, h - ((v - min) / (max - min || 1)) * (h - 10) - 4 ]);
  const line = pts.map((p, i) => `${i ? "L" : "M"}${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join(" ");
  const area = `${line} L${w} ${h} L0 ${h} Z`;
  const id = "g" + color.replace("#", "");
  return (
    <svg width="100%" height={h} viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none">
      <defs><linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" stopColor={color} stopOpacity="0.25" /><stop offset="100%" stopColor={color} stopOpacity="0" />
      </linearGradient></defs>
      <path d={area} fill={`url(#${id})`} />
      <path d={line} fill="none" stroke={color} strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />
      {pts.map((p, i) => i === pts.length - 1 && <circle key={i} cx={p[0]} cy={p[1]} r="3.5" fill={color} />)}
    </svg>
  );
}
function BarRow({ label, value, max, color }) {
  const pct = Math.round((value / (max || 1)) * 100);
  return (
    <div style={{ marginBottom: 12 }}>
      <div style={{ display: "flex", justifyContent: "space-between", fontSize: 12, marginBottom: 5 }}>
        <span style={{ color: C.sub }}>{label}</span><span style={{ fontWeight: 700, fontFamily: C.mono }}>{value}</span>
      </div>
      <div style={{ height: 8, borderRadius: 5, background: C.lineSoft, overflow: "hidden" }}>
        <div style={{ height: "100%", width: `${pct}%`, background: color, borderRadius: 5, transition: "width .8s ease" }} />
      </div>
    </div>
  );
}

function Dashboard({ stats, policy, violations, sources, countControls, filterTo, setTab }) {
  const recent = (violations || []).slice(0, 6);
  const enabled = (policy.enabledSources || []).length;
  // severity breakdown across violations
  const sev = { Critical: 0, High: 0, Medium: 0, Low: 0 };
  (violations || []).forEach((v) => { if (sev[v.severity] != null) sev[v.severity]++; });
  const sevMax = Math.max(...Object.values(sev), 1);
  // synthetic-but-deterministic 14-day trend seeded from current totals (until we persist history)
  const trend = Array.from({ length: 14 }, (_, i) => {
    const base = Math.max(2, stats.total / 3);
    return Math.round(base + base * 0.6 * Math.sin(i / 2.1) + (i * stats.blocked) / 14);
  });
  const cleanRate = stats.total ? Math.round((stats.allowed / stats.total) * 100) : 0;
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <div style={{ fontSize: 22, fontWeight: 700, letterSpacing: -0.4, marginBottom: 2 }}>Overview</div>
      <div style={{ color: C.sub, fontSize: 13, marginBottom: 22 }}>Live posture across the supply-chain gate.</div>
      <div style={{ ...s.kpis, padding: 0, marginBottom: 22 }}>
        <Kpi label="Evaluations" value={stats.total} active onClick={() => filterTo(null)} />
        <Kpi label="Blocked" value={stats.blocked} tone={C.block} onClick={() => filterTo("Block")} />
        <Kpi label="Allowed" value={stats.allowed} tone={C.allow} onClick={() => filterTo("Allow")} />
        <Kpi label="Quarantined" value={stats.quarantined} tone={C.warn} onClick={() => filterTo("Quarantine")} />
        <Kpi label="Components" value={stats.components} />
        <Kpi label="Controls" value={countControls(policy)} />
      </div>

      {/* chart row */}
      <div style={{ display: "grid", gridTemplateColumns: "1.6fr 1fr 1fr", gap: 16, marginBottom: 16 }}>
        <Card title="Evaluation trend" desc="Gate decisions over the last 14 days.">
          <div style={{ padding: "8px 14px 14px" }}>
            <div style={{ display: "flex", alignItems: "baseline", gap: 10, padding: "4px 6px 10px" }}>
              <span style={{ fontSize: 28, fontWeight: 800, letterSpacing: -1 }}>{stats.total}</span>
              <span style={{ fontSize: 12, color: C.allow, fontWeight: 600 }}>▲ live</span>
              <span style={{ fontSize: 12, color: C.sub }}>total evaluations</span>
            </div>
            <AreaChart values={trend} color={C.accent} />
          </div>
        </Card>
        <Card title="Decision mix" desc="">
          <div style={{ display: "flex", alignItems: "center", gap: 14, padding: 18 }}>
            <Donut size={130} data={[
              { value: stats.allowed, color: C.allow }, { value: stats.blocked, color: C.block }, { value: stats.quarantined, color: C.warn },
            ]} center={{ top: `${cleanRate}%`, bottom: "allowed" }} />
            <div style={{ display: "flex", flexDirection: "column", gap: 8, fontSize: 12 }}>
              <Legend c={C.allow} label="Allowed" v={stats.allowed} />
              <Legend c={C.block} label="Blocked" v={stats.blocked} />
              <Legend c={C.warn} label="Quarantined" v={stats.quarantined} />
            </div>
          </div>
        </Card>
        <Card title="Severity breakdown" desc="">
          <div style={{ padding: 18 }}>
            <BarRow label="Critical" value={sev.Critical} max={sevMax} color={C.block} />
            <BarRow label="High" value={sev.High} max={sevMax} color="#ef6a3d" />
            <BarRow label="Medium" value={sev.Medium} max={sevMax} color={C.warn} />
            <BarRow label="Low" value={sev.Low} max={sevMax} color={C.sub} />
          </div>
        </Card>
      </div>

      <Card title="Recent violations"
        desc="Latest Block / Quarantine decisions. Open the Violations tab for the full, filterable register.">
        <Table cols={["Resource", "Severity", "Decision", "Watch", "Status"]}>
          {recent.length === 0 && <tr><td style={s.td} colSpan={5}>No violations recorded yet.</td></tr>}
          {recent.map((v) => (
            <tr key={v.id} style={{ ...s.tr, cursor: "pointer" }} onClick={() => setTab("violations")}>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{v.resource}</td>
              <td style={{ ...s.td }}><SevPill sev={v.severity} /></td>
              <td style={s.td}><Decision d={v.decision} /></td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: C.sub }}>{v.watch || "—"}</td>
              <td style={s.td}>{v.status === "Waived"
                ? <span style={{ color: C.sub }}>Waived</span>
                : <span style={{ color: C.block, fontWeight: 600 }}>Open</span>}</td>
            </tr>
          ))}
        </Table>
      </Card>
      <Card title="Coverage at a glance"
        desc={`${enabled} of ${ALL_SOURCES.length} feeds enabled. Open Intelligence sources to run a live health probe.`}>
        <div style={{ display: "flex", flexWrap: "wrap", gap: 10, padding: 18 }}>
          {ALL_SOURCES.map((src) => {
            const avail = sources.find((x) => x.key === src.key)?.isAvailable;
            const on = policy.enabledSources?.includes(src.key);
            const tone = on && avail ? C.allow : avail ? C.sub : C.dim;
            return (
              <div key={src.key} onClick={() => setTab("sources")} title={src.scope}
                style={{ display: "flex", alignItems: "center", gap: 8, padding: "8px 12px", cursor: "pointer",
                  background: C.bg2, border: `1px solid ${C.line}`, borderRadius: 9, fontSize: 12 }}>
                <span style={{ width: 7, height: 7, borderRadius: "50%", background: tone,
                  boxShadow: on && avail ? `0 0 8px ${tone}` : "none" }} />
                {src.label}
              </div>
            );
          })}
        </div>
      </Card>
    </div>
  );
}
function Legend({ c, label, v }) {
  return <span style={{ display: "flex", alignItems: "center", gap: 7 }}>
    <span style={{ width: 9, height: 9, borderRadius: 2, background: c }} />
    <span style={{ color: C.sub }}>{label}</span>
    <span style={{ fontWeight: 700, fontFamily: C.mono, marginLeft: "auto" }}>{v}</span></span>;
}
// Severity rendered as a filled pill (dark-theme).
function SevPill({ sev }) {
  const map = { Critical: C.block, High: C.block, Medium: C.warn, Low: C.sub, None: C.dim };
  const c = map[sev] || C.sub;
  return <span style={{ fontFamily: C.mono, fontSize: 10, padding: "3px 9px", borderRadius: 20,
    color: c, background: `${c}1f`, fontWeight: 600 }}>{(sev || "—").toUpperCase()}</span>;
}
function Kpi({ label, value, tone, onClick, active }) {
  const clickable = typeof onClick === "function";
  const c = tone || C.accent;
  const isNum = typeof value === "number";
  const shown = isNum ? <CountUp target={value} /> : value;
  const [hov, setHov] = useState(false);
  return (
    <div onClick={onClick} title={clickable ? `Filter ledger to ${label}` : undefined}
      onMouseEnter={() => setHov(true)} onMouseLeave={() => setHov(false)}
      style={{ ...s.kpi,
        cursor: clickable ? "pointer" : "default",
        borderColor: active ? c : C.line,
        transform: hov && clickable ? "translateY(-2px)" : "none",
        boxShadow: active || (hov && clickable) ? `0 0 0 1px ${c}33, 0 6px 18px rgba(15,39,72,.10)` : s.kpi.boxShadow }}>
      <div style={{ position: "absolute", top: 0, left: 0, right: 0, height: 3, background: c, opacity: active ? 1 : 0.7 }} />
      <div style={{ ...s.kpiVal, color: tone || C.ink }}>{shown}</div>
      <div style={s.kpiLbl}>{label}{clickable && <span style={s.kpiHint}>▸</span>}</div>
    </div>
  );
}
function CountUp({ target }) { return <>{useCountUp(target)}</>; }
function Card({ title, desc, children }) {
  return (
    <section style={s.card}>
      <div style={s.cardHead}>
        <h2 style={s.h2}>{title}</h2>
        {desc && <p style={s.desc}>{desc}</p>}
      </div>
      {children}
    </section>
  );
}
function Table({ cols, children }) {
  return (
    <table style={s.table}>
      <thead><tr>{cols.map((c) => <th key={c} style={s.th}>{c}</th>)}</tr></thead>
      <tbody>{children}</tbody>
    </table>
  );
}
function Ctl({ id, rule, children }) {
  return (
    <tr style={s.tr}>
      <td style={{ ...s.td, fontFamily: C.mono, color: C.accent, fontSize: 11, whiteSpace: "nowrap" }}>{id || "—"}</td>
      <td style={s.td}>{rule}</td>
      <td style={{ ...s.td, textAlign: "right" }}>{children}</td>
    </tr>
  );
}
function SubHead({ children }) { return <div style={s.subhead}>{children}</div>; }
function Switch({ on, onChange, disabled }) {
  return (
    <button disabled={disabled} onClick={() => onChange(!on)}
      style={{ width: 38, height: 20, borderRadius: 11, border: `1px solid ${on ? C.accent : C.line}`,
        background: on ? C.accent : C.bg2, cursor: disabled ? "not-allowed" : "pointer",
        opacity: disabled ? 0.4 : 1, position: "relative", padding: 0,
        boxShadow: on ? "0 0 10px rgba(61,214,163,.4)" : "none", transition: ".12s" }}>
      <span style={{ position: "absolute", top: 2, left: on ? 20 : 2, width: 14, height: 14,
        background: on ? "#06140f" : C.sub, borderRadius: "50%", transition: "left .12s" }} />
    </button>
  );
}
function Stepper({ value, onChange, step, min, max, unit }) {
  return (
    <div style={{ display: "inline-flex", alignItems: "center", gap: 8, justifyContent: "flex-end" }}>
      <input type="number" value={value} step={step} min={min} max={max}
        onChange={(e) => onChange(parseFloat(e.target.value))} style={s.input} />
      <span style={{ color: C.sub, fontSize: 11, fontFamily: C.mono, minWidth: 38, textAlign: "left" }}>{unit}</span>
    </div>
  );
}
function Chips({ tags, onChange }) {
  const [v, setV] = useState("");
  return (
    <div style={{ display: "flex", flexWrap: "wrap", gap: 5, justifyContent: "flex-end" }}>
      {tags.map((t) => (
        <span key={t} style={s.chip}>{t}
          <button onClick={() => onChange(tags.filter((x) => x !== t))} style={s.chipX}>×</button></span>
      ))}
      <input value={v} placeholder="add…" onChange={(e) => setV(e.target.value)}
        onKeyDown={(e) => { if (e.key === "Enter" && v) { onChange([...tags, v]); setV(""); } }}
        style={{ ...s.input, width: 80 }} />
    </div>
  );
}
function Tag({ tone, children }) {
  return <span style={{ fontFamily: C.mono, fontSize: 10, padding: "2px 6px",
    border: `1px solid ${tone}`, color: tone, borderRadius: 2 }}>{children}</span>;
}
function Decision({ d }) {
  const tone = d === "Block" ? C.block : d === "Quarantine" ? C.warn : C.allow;
  return <span style={{ fontFamily: C.mono, fontSize: 11, fontWeight: 600, color: tone,
    textTransform: "uppercase", letterSpacing: 0.5 }}>{d}</span>;
}
function Callout({ children }) {
  return <div style={s.callout}>{children}</div>;
}
function Exceptions({ policy, setPolicy }) {
  const [d, setD] = useState({ package: "", reason: "", approvedBy: "", ticket: "", expires: "" });
  const add = () => {
    if (!d.package || !d.ticket) return;
    setPolicy((p) => ({ ...p, exceptions: [...p.exceptions, { ...d, ecosystem: null }] }));
    setD({ package: "", reason: "", approvedBy: "", ticket: "", expires: "" });
  };
  return (
    <Card title="Approved exceptions" desc="Time-boxed, attributed overrides. This register replaces the per-package approval ticket.">
      <Table cols={["Component", "Ticket", "Approver", "Expires", ""]}>
        {policy.exceptions.length === 0 && <tr><td style={s.td} colSpan={5}>No active exceptions.</td></tr>}
        {policy.exceptions.map((e, i) => (
          <tr key={i} style={s.tr}>
            <td style={{ ...s.td, fontFamily: C.mono }}>{e.package}</td>
            <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11 }}>{e.ticket}</td>
            <td style={s.td}>{e.approvedBy || "—"}</td>
            <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11 }}>{e.expires || "—"}</td>
            <td style={{ ...s.td, textAlign: "right" }}>
              <button onClick={() => setPolicy((p) =>
                ({ ...p, exceptions: p.exceptions.filter((_, j) => j !== i) }))} style={s.remove}>Revoke</button></td>
          </tr>
        ))}
      </Table>
      <div style={s.form}>
        {[["package", "package==version"], ["ticket", "Ref e.g. SEC-1234"], ["approvedBy", "Approver"],
          ["expires", "YYYY-MM-DD"]].map(([k, ph]) => (
          <input key={k} placeholder={ph} value={d[k]}
            onChange={(e) => setD({ ...d, [k]: e.target.value })} style={s.formInput} />
        ))}
        <button onClick={add} style={s.add}>Add exception</button>
      </div>
    </Card>
  );
}

const ECOS = ["PyPI", "npm", "NuGet", "Cargo", "Go", "HuggingFace"];
// Intelligence sources with a live health probe (real reachability + latency per feed).
function Sources({ sources, policy, set, setPolicy }) {
  const [admin, setAdmin] = useState(null);      // { builtins, customs }
  const [tests, setTests] = useState({});        // key -> { ok, status, elapsedMs }
  const [testing, setTesting] = useState(null);  // key currently testing
  const [editor, setEditor] = useState(null);    // { key, label, endpoint, credential } | null  (built-in cred edit)
  const [customEditor, setCustomEditor] = useState(null); // a policy.customSources entry being edited | null
  const [addOpen, setAddOpen] = useState(false);

  const reload = () => api.getSourcesAdmin().then(setAdmin).catch(() => setAdmin({ builtins: [], customs: [] }));
  useEffect(() => { reload(); }, []);

  const test = (key) => { setTesting(key); api.testSource(key).then((r) => setTests((t) => ({ ...t, [key]: r }))).finally(() => setTesting(null)); };
  const toggleEnabled = (key, on) => set("enabledSources", on ? [...policy.enabledSources, key] : policy.enabledSources.filter((k) => k !== key));
  const toggleRequired = (key, on) => set("requiredSources", on ? [...(policy.requiredSources || []), key] : (policy.requiredSources || []).filter((k) => k !== key));

  // Save a built-in source's credential/endpoint into policy.sourceConfigs.
  // Blank credential keeps the previously-stored one (editing just the endpoint must not wipe a key).
  const saveConfig = (cfg) => {
    setPolicy((p) => {
      const prev = (p.sourceConfigs || []).find((c) => c.key === cfg.key);
      const others = (p.sourceConfigs || []).filter((c) => c.key !== cfg.key);
      const merged = { ...cfg, credential: cfg.credential ?? prev?.credential ?? null };
      return { ...p, sourceConfigs: [...others, merged] };
    });
    setEditor(null);
  };
  // Add / remove custom OSV sources.
  const addCustom = (cs) => { setPolicy((p) => ({ ...p, customSources: [...(p.customSources || []), cs] })); setAddOpen(false); };
  const removeCustom = (id) => setPolicy((p) => ({ ...p, customSources: (p.customSources || []).filter((c) => c.id !== id) }));
  const toggleCustom = (id, on) => setPolicy((p) => ({ ...p, customSources: (p.customSources || []).map((c) => c.id === id ? { ...c, enabled: on } : c) }));
  // Edit an existing custom source (label / URL / credential). Keyed by original id.
  const saveCustom = (orig, next) => {
    setPolicy((p) => ({ ...p, customSources: (p.customSources || []).map((c) => c.id === orig ? { ...c, ...next } : c) }));
    setCustomEditor(null);
  };
  // Open the editor for a custom row — find the full record in policy (the admin row omits credential).
  const openCustomEdit = (id) => {
    const rec = (policy.customSources || []).find((c) => c.id === id);
    if (rec) setCustomEditor(rec);
  };

  if (!admin) return <div style={s.kevEmpty}>Loading sources…</div>;
  const tone = (ok, st) => st == null ? C.dim : ok ? C.allow : (st === "NotConfigured" ? C.sub : C.block);

  const row = (src, custom) => {
    const t = tests[src.key];
    const enabled = custom ? src.enabled : src.enabled;
    return (
      <tr key={src.key} style={s.tr}>
        <td style={s.td}><b>{src.label}</b>{custom && <Tag tone={C.info} >custom</Tag>}
          <div style={{ color: C.sub, fontSize: 11, marginTop: 2 }}>{src.scope}</div>
          {src.endpoint
            ? <div style={{ color: C.info, fontSize: 10.5, fontFamily: C.mono, marginTop: 2 }} title="Endpoint override (on-prem mirror)">↳ {src.endpoint} <span style={{ color: C.accentDim }}>· override</span></div>
            : src.defaultEndpoint && <div style={{ color: C.dim, fontSize: 10.5, fontFamily: C.mono, marginTop: 2 }} title="Built-in default endpoint">↳ {src.defaultEndpoint}</div>}</td>
        <td style={s.td}><Tag tone={src.tier === "Licensed" ? C.warn : src.tier === "Custom" ? C.info : C.allow}>{src.tier}</Tag></td>
        <td style={s.td}>
          {t ? <span style={{ color: tone(t.ok, t.status), fontWeight: 600 }} title={t.detail || ""}>{t.status}{t.elapsedMs ? ` · ${t.elapsedMs}ms` : ""}</span>
            : src.available ? <span style={{ color: C.allow }}>● Ready</span>
            : <span style={{ color: C.sub }} title={SOURCE_HINT[src.key] || ""}>{src.needsCredential ? "No credential" : "Idle"}</span>}
        </td>
        <td style={s.td}><Switch on={enabled} onChange={(v) => custom ? toggleCustom(src.key, v) : toggleEnabled(src.key, v)} /></td>
        <td style={s.td}><Switch on={src.required} disabled={custom} onChange={(v) => toggleRequired(src.key, v)} /></td>
        <td style={{ ...s.td, whiteSpace: "nowrap" }}>
          <button style={s.miniBtn} onClick={() => test(src.key)} disabled={testing === src.key}>{testing === src.key ? "…" : "Test"}</button>
          {!custom && <button style={{ ...s.miniBtn, marginLeft: 6 }}
            onClick={() => setEditor({ key: src.key, label: src.label, endpoint: src.endpoint || "", credential: "", needsCredential: src.needsCredential, hasCredential: src.hasCredential, defaultEndpoint: src.defaultEndpoint })}>Edit</button>}
          {custom && <button style={{ ...s.miniBtn, marginLeft: 6 }} onClick={() => openCustomEdit(src.key)}>Edit</button>}
          {custom && <button style={{ ...s.miniBtn, marginLeft: 6, color: C.block }} onClick={() => removeCustom(src.key)}>Remove</button>}
        </td>
      </tr>
    );
  };

  return (
    <>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "12px 0 14px" }}>
        <div style={{ fontSize: 13, color: C.sub }}>Configure, test, enable and add intelligence sources. Changes are saved into the signed policy — commit to apply.</div>
        <button style={s.add} onClick={() => setAddOpen(true)}>+ Add OSV source</button>
      </div>
      <div style={s.card}>
        <table style={s.table}><thead><tr>
          {["Source", "Tier", "Test status", "Enabled", "Required", "Actions"].map((c) => <th key={c} style={s.th}>{c}</th>)}
        </tr></thead><tbody>
          {(admin.builtins || []).map((src) => row(src, false))}
          {(admin.customs || []).map((src) => row(src, true))}
          {(policy.customSources || []).filter((c) => !(admin.customs || []).some((x) => x.key === c.id)).map((c) =>
            row({ key: c.id, label: c.label, scope: "Custom OSV-format feed (uncommitted)", tier: "Custom", enabled: c.enabled, required: false, endpoint: c.osvQueryUrl, available: false, needsCredential: false }, true))}
        </tbody></table>
      </div>
      <Callout>Included feeds are free/open. Licensed feeds (VulnCheck/Socket) activate on credential.
        Custom OSV sources let you point at an on-prem mirror for zero-egress operation.</Callout>

      {editor && <CredEditor editor={editor} onCancel={() => setEditor(null)}
        onSave={(c) => saveConfig({ key: editor.key, endpoint: c.endpoint?.trim() || null, credential: c.credential ? c.credential : undefined, enabled: true })} />}
      {addOpen && <AddCustomSource onCancel={() => setAddOpen(false)} onAdd={addCustom} />}
      {customEditor && <AddCustomSource edit={customEditor} onCancel={() => setCustomEditor(null)}
        onAdd={(next) => saveCustom(customEditor.id, next)} />}
    </>
  );
}

// AI assistant settings: enter the Groq key (stored server-side in the signed policy), pick the
// model, test the connection, and enable/disable the assistant. The key is never returned.
function AiSettingsPanel() {
  const [st, setSt] = useState(null);
  const [key, setKey] = useState("");
  const [model, setModel] = useState("");
  const [saving, setSaving] = useState(false);
  const [test, setTest] = useState(null);
  const [testing, setTesting] = useState(false);
  const reload = () => api.getAiSettings().then((s) => { setSt(s); setModel(s.model || ""); }).catch(() => setSt(null));
  useEffect(() => { reload(); }, []);
  if (!st) return <div style={{ padding: "10px 22px", color: C.sub }}>Loading AI settings…</div>;

  const save = async (patch) => {
    setSaving(true);
    try { await api.saveAiSettings({ model, ...patch, apiKey: patch.clearKey ? null : (patch.apiKey ?? (key || undefined)), clearKey: !!patch.clearKey });
      setKey(""); setTest(null); await reload();
    } catch {} finally { setSaving(false); }
  };
  const runTest = async () => {
    setTesting(true);
    try { setTest(await api.testAi({ apiKey: key || undefined, model })); } catch { setTest({ ok: false, detail: "request failed" }); }
    finally { setTesting(false); }
  };

  return (
    <div style={{ padding: "14px 22px 4px" }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <span style={{ fontSize: 13, fontWeight: 600 }}>Ask AI assistant</span>
          {st.configured
            ? <Tag tone={C.allow}>{st.usingEnvKey ? "Active (env key)" : "Active"}</Tag>
            : <Tag tone={C.warn}>No key set</Tag>}
        </div>
        <label style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12.5, color: C.sub }}>
          Enabled <Switch on={st.assistantEnabled} onChange={(v) => save({ assistantEnabled: v })} />
        </label>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, maxWidth: 720 }}>
        <div>
          <label style={s.fieldLbl}>Groq API key</label>
          <input style={{ ...s.formInput, width: "100%", fontFamily: C.mono }} type="password" autoComplete="off"
            placeholder={st.hasKey ? "•••••••• stored — paste to replace" : "gsk_…"} value={key} onChange={(e) => { setKey(e.target.value); setTest(null); }} />
          <div style={{ fontSize: 11, color: C.sub, marginTop: 5 }}>
            Stored server-side in the signed policy. {st.usingEnvKey && "Currently falling back to the GROQ_API_KEY env var."}</div>
        </div>
        <div>
          <label style={s.fieldLbl}>Model</label>
          <input style={{ ...s.formInput, width: "100%", fontFamily: C.mono }} placeholder="openai/gpt-oss-120b"
            value={model} onChange={(e) => setModel(e.target.value)} />
          <div style={{ fontSize: 11, color: C.sub, marginTop: 5 }}>Any Groq-served chat model.</div>
        </div>
      </div>

      {test && <div style={{ fontSize: 12.5, fontWeight: 600, marginTop: 12, color: test.ok ? C.allow : C.block }}>
        {test.ok ? "● Connection OK" : "● Failed"}{test.detail ? ` — ${test.detail}` : ""}</div>}

      <div style={{ display: "flex", gap: 10, marginTop: 16 }}>
        <button style={s.btnGhost} onClick={runTest} disabled={testing || (!key && !st.hasKey)}>{testing ? "Testing…" : "Test connection"}</button>
        <button style={s.add} onClick={() => save({})} disabled={saving || (!key && !model)}>{saving ? "Saving…" : "Save"}</button>
        {st.hasKey && <button style={s.remove} onClick={() => save({ clearKey: true })} disabled={saving}>Remove stored key</button>}
      </div>
    </div>
  );
}

// Credential / endpoint editor for a built-in source (stored server-side in the signed policy).
function CredEditor({ editor, onCancel, onSave }) {
  // Pre-fill the REAL current endpoint: the saved override if any, else the built-in default,
  // so the admin sees and edits the actual value (not just a placeholder hint).
  const [endpoint, setEndpoint] = useState(editor.endpoint || editor.defaultEndpoint || "");
  const [credential, setCredential] = useState("");
  const isDefault = !editor.endpoint && endpoint === (editor.defaultEndpoint || "");
  return (
    <div style={s.modalScrim} onClick={onCancel}>
      <div style={{ ...s.modal, width: "min(520px,96vw)" }} onClick={(e) => e.stopPropagation()}>
        <div style={s.modalHead}>
          <b>Configure {editor.label}</b>
          <button style={s.modalX} onClick={onCancel}>×</button>
        </div>
        <div style={{ padding: 20, display: "grid", gap: 16 }}>
          <div>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
              <label style={s.fieldLbl}>Endpoint {isDefault ? <span style={{ color: C.dim, fontWeight: 400 }}>(built-in default)</span> : <span style={{ color: C.accentDim, fontWeight: 400 }}>(override)</span>}</label>
              {editor.defaultEndpoint && endpoint !== editor.defaultEndpoint &&
                <button style={s.linkBtn} onClick={() => setEndpoint(editor.defaultEndpoint)}>Reset to default</button>}
            </div>
            <input style={{ ...s.formInput, width: "100%", fontFamily: C.mono }}
              placeholder={editor.defaultEndpoint || "https://…"}
              value={endpoint} onChange={(e) => setEndpoint(e.target.value)} />
            <div style={{ fontSize: 11, color: C.sub, marginTop: 5 }}>
              {editor.defaultEndpoint
                ? <>This is the live endpoint. Change it to point at an on-prem mirror; reset (or clear) to use the built-in default.</>
                : <>Point at an on-prem mirror for zero-egress operation.</>}</div>
          </div>
          <div>
            <label style={s.fieldLbl}>API key / token {editor.needsCredential === false && <span style={{ color: C.dim, fontWeight: 400 }}>(not required for this feed)</span>}</label>
            <input style={{ ...s.formInput, width: "100%", fontFamily: C.mono }} type="password"
              placeholder={editor.hasCredential ? "•••••••• stored — type to replace" : "Paste credential"}
              value={credential} onChange={(e) => setCredential(e.target.value)} autoComplete="off" />
            <div style={{ fontSize: 11, color: C.sub, marginTop: 5 }}>Stored server-side in the signed policy. Commit to apply.</div>
          </div>
        </div>
        <div style={s.modalFoot}>
          <button style={s.btnGhost} onClick={onCancel}>Cancel</button>
          <button style={s.add} onClick={() => onSave({
            // store null (use default) when unchanged from the built-in default
            endpoint: endpoint.trim() === (editor.defaultEndpoint || "") ? "" : endpoint, credential })}>Save</button>
        </div>
      </div>
    </div>
  );
}

// Add OR edit a custom OSV-format feed. Tests reachability before saving. When `edit` is supplied
// the form is prefilled and the id is preserved (so it updates the existing source, not adds a new one).
function AddCustomSource({ onCancel, onAdd, edit }) {
  const isEdit = !!edit;
  const [f, setF] = useState({
    label: edit?.label || "", osvQueryUrl: edit?.osvQueryUrl || "",
    credential: edit?.credential || "", enabled: edit?.enabled ?? true, required: edit?.required ?? false,
  });
  const [test, setTest] = useState(null);   // { ok, status, detail }
  const [testing, setTesting] = useState(false);
  const slug = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  const valid = f.label.trim() && /^https?:\/\//.test(f.osvQueryUrl.trim());
  const runTest = () => {
    setTesting(true);
    api.testCustomSource(f.osvQueryUrl.trim(), f.credential || null)
      .then(setTest).catch(() => setTest({ ok: false, status: "Error", detail: "request failed" }))
      .finally(() => setTesting(false));
  };
  const submit = () => onAdd({
    id: isEdit ? edit.id : (slug(f.label) || `custom-${f.osvQueryUrl.length}`), label: f.label.trim(),
    osvQueryUrl: f.osvQueryUrl.trim(), credential: f.credential || null, enabled: f.enabled, required: f.required,
  });
  return (
    <div style={s.modalScrim} onClick={onCancel}>
      <div style={{ ...s.modal, width: "min(560px,96vw)" }} onClick={(e) => e.stopPropagation()}>
        <div style={s.modalHead}>
          <b>{isEdit ? `Edit ${edit.label}` : "Add custom OSV source"}</b>
          <button style={s.modalX} onClick={onCancel}>×</button>
        </div>
        <div style={{ padding: 20, display: "grid", gap: 16 }}>
          <div>
            <label style={s.fieldLbl}>Display name</label>
            <input style={{ ...s.formInput, width: "100%" }} placeholder="e.g. Internal OSV mirror"
              value={f.label} onChange={(e) => setF({ ...f, label: e.target.value })} />
          </div>
          <div>
            <label style={s.fieldLbl}>OSV query URL</label>
            <input style={{ ...s.formInput, width: "100%", fontFamily: C.mono }} placeholder="https://mirror.internal/v1/query"
              value={f.osvQueryUrl} onChange={(e) => { setF({ ...f, osvQueryUrl: e.target.value }); setTest(null); }} />
            <div style={{ fontSize: 11, color: C.sub, marginTop: 5 }}>POST {"{package,version}"} → OSV query response. OSV.dev API format.</div>
          </div>
          <div>
            <label style={s.fieldLbl}>Bearer token (optional)</label>
            <input style={{ ...s.formInput, width: "100%", fontFamily: C.mono }} type="password"
              placeholder={isEdit && edit.credential ? "•••••••• stored — leave to keep, type to replace" : "For authenticated mirrors"}
              value={f.credential} onChange={(e) => setF({ ...f, credential: e.target.value })} autoComplete="off" />
          </div>
          <div style={{ display: "flex", gap: 22 }}>
            <label style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12.5, color: C.sub }}>
              Enabled <Switch on={f.enabled} onChange={(v) => setF({ ...f, enabled: v })} /></label>
          </div>
          {test && <div style={{ fontSize: 12.5, fontWeight: 600, color: test.ok ? C.allow : C.block }}>
            {test.ok ? "● Connection OK" : "● Failed"} — {test.status}{test.detail ? `: ${test.detail}` : ""}</div>}
        </div>
        <div style={s.modalFoot}>
          <button style={s.btnGhost} onClick={onCancel}>Cancel</button>
          <button style={s.btnGhost} onClick={runTest} disabled={!valid || testing}>{testing ? "Testing…" : "Test connection"}</button>
          <button style={{ ...s.add, opacity: valid ? 1 : 0.5 }} onClick={submit} disabled={!valid}>{isEdit ? "Save changes" : "Add source"}</button>
        </div>
      </div>
    </div>
  );
}

// Xray Scans List — full drill-down: repositories → artifacts → artifact overview.
function ScansList() {
  const [view, setView] = useState({ level: "repos" }); // repos | artifacts{repo} | artifact{repo,art}
  const crumb = (
    <div style={s.crumb}>
      <span style={{ color: C.accent, cursor: "pointer" }} onClick={() => setView({ level: "repos" })}>Xray</span>
      <span style={{ color: C.dim }}>›</span>
      <span style={{ color: view.level === "repos" ? C.ink : C.accent, cursor: view.level === "repos" ? "default" : "pointer" }}
        onClick={() => setView({ level: "repos" })}>Scans List</span>
      {view.level !== "repos" && <><span style={{ color: C.dim }}>›</span>
        <span style={{ color: view.level === "artifacts" ? C.ink : C.accent, cursor: view.level === "artifacts" ? "default" : "pointer" }}
          onClick={() => setView({ level: "artifacts", repo: view.repo })}>{view.repo}</span></>}
      {view.level === "artifact" && <><span style={{ color: C.dim }}>›</span>
        <span style={{ color: C.ink }}>{view.art.fileName || view.art.name}</span></>}
    </div>
  );
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      {crumb}
      {view.level === "repos" && <ScansRepos onOpen={(repo) => setView({ level: "artifacts", repo })} />}
      {view.level === "artifacts" && <RepoArtifacts repo={view.repo} onOpen={(art) => setView({ level: "artifact", repo: view.repo, art })} />}
      {view.level === "artifact" && <ArtifactOverview repo={view.repo} art={view.art} />}
    </div>
  );
}

// Real brand icons per ecosystem (inline SVG), matching the JFrog Scans List exactly.
function BrandIcon({ format }) {
  const k = (format || "").toLowerCase();
  const sz = 20;
  if (k === "docker") return (
    <svg width={sz} height={sz} viewBox="0 0 24 24"><path fill="#2496ed" d="M13.98 11.08h2.12a.19.19 0 0 0 .19-.18V9.01a.19.19 0 0 0-.19-.19h-2.12a.18.18 0 0 0-.18.19v1.89c0 .1.08.18.18.18m-2.95 0h2.12a.19.19 0 0 0 .18-.18V9.01a.19.19 0 0 0-.18-.19h-2.12a.19.19 0 0 0-.19.19v1.89c0 .1.09.18.19.18m-2.93 0h2.12a.19.19 0 0 0 .18-.18V9.01a.19.19 0 0 0-.18-.19H8.1a.19.19 0 0 0-.19.19v1.89c0 .1.08.18.19.18m-2.96 0h2.11a.19.19 0 0 0 .19-.18V9.01a.19.19 0 0 0-.19-.19H5.14a.18.18 0 0 0-.18.19v1.89c0 .1.08.18.18.18m5.89-2.72h2.12a.19.19 0 0 0 .18-.19V6.29a.19.19 0 0 0-.18-.19h-2.12a.19.19 0 0 0-.19.19v1.88c0 .1.09.19.19.19m-2.93 0h2.12a.18.18 0 0 0 .18-.19V6.29a.18.18 0 0 0-.18-.19H8.1a.19.19 0 0 0-.19.19v1.88c0 .1.08.19.19.19m-2.96 0h2.11a.19.19 0 0 0 .19-.19V6.29a.18.18 0 0 0-.19-.19H5.14a.18.18 0 0 0-.18.19v1.88c0 .1.08.19.18.19m0-2.72h2.11a.18.18 0 0 0 .19-.18V3.57a.19.19 0 0 0-.19-.19H5.14a.18.18 0 0 0-.18.19v1.89c0 .1.08.18.18.18m17.05 3.13c-.06-.05-.67-.51-1.95-.51-.34 0-.68.03-1.01.09-.25-1.7-1.65-2.53-1.71-2.57l-.34-.2-.23.32a4.6 4.6 0 0 0-.6 1.4c-.23.96-.09 1.86.4 2.63-.59.33-1.54.41-1.73.42H1.84a.84.84 0 0 0-.84.84c-.04 1.4.2 2.79.71 4.09.58 1.42 1.44 2.46 2.55 3.09 1.25.71 3.28 1.11 5.58 1.11 1.04 0 2.08-.09 3.11-.28a12.9 12.9 0 0 0 4.06-1.48 11.1 11.1 0 0 0 2.77-2.27c1.3-1.47 2.07-3.1 2.64-4.55h.23c1.37 0 2.21-.55 2.68-1.01.31-.29.55-.65.71-1.05l.1-.29z"/></svg>
  );
  if (k === "huggingface") return <span style={{ fontSize: 16 }}>🤗</span>;
  if (k === "maven" || k === "maven2") return (
    <svg width={sz} height={sz} viewBox="0 0 24 24"><path fill="#c1272d" d="M12 2 4 6v12l8 4 8-4V6zm0 2.3 5.5 2.7L12 9.7 6.5 7zm-6 4.4 5 2.5v6.1l-5-2.5zm12 0v6.1l-5 2.5v-6.1z"/></svg>
  );
  if (k === "npm") return (
    <svg width={sz} height={sz} viewBox="0 0 24 24"><rect width="24" height="24" rx="3" fill="#cb3837"/><path fill="#fff" d="M5 7h14v10h-6V9h-3v8H5z"/></svg>
  );
  if (k === "pypi") return (
    <svg width={sz} height={sz} viewBox="0 0 24 24"><path fill="#3775a9" d="M11.9 2c-1.9 0-3.4.4-3.4 2.5v2h3.5v.5H5.6C3.4 7 2 8.4 2 11.6c0 3.1 1.2 4.6 3.4 4.6h1.1v-2.4c0-1.6 1.4-3 3-3h3.5c1.4 0 2.5-1.1 2.5-2.5V4.5C15.5 2.7 14 2 11.9 2M9.9 3.6c.4 0 .7.3.7.7s-.3.6-.7.6-.6-.3-.6-.6.3-.7.6-.7"/><path fill="#ffd43b" d="M12.1 22c1.9 0 3.4-.4 3.4-2.5v-2H12v-.5h6.4c2.2 0 3.6-1.4 3.6-4.6 0-3.1-1.2-4.6-3.4-4.6h-1.1v2.4c0 1.6-1.4 3-3 3H11c-1.4 0-2.5 1.1-2.5 2.5v4.3c0 1.8 1.5 2.5 3.6 2.5m2-1.6c-.4 0-.7-.3-.7-.7s.3-.6.7-.6.6.3.6.6-.3.7-.6.7"/></svg>
  );
  // generic monogram tile for cargo/go/nuget/others
  const mono = { nuget: "nu", cargo: "rs", go: "Go", r: "R", conda: "C" }[k] || (format || "?").slice(0, 2);
  return <span style={{ width: 20, height: 20, borderRadius: 4, background: C.surface2, border: `1px solid ${C.line}`,
    display: "inline-grid", placeItems: "center", fontSize: 9, fontWeight: 700, color: C.sub }}>{mono}</span>;
}
function PkgType({ format }) {
  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: 9 }}>
      <BrandIcon format={format} />
      <span style={{ fontSize: 13, textTransform: "capitalize", color: C.ink }}>{format}</span>
    </span>
  );
}
function ConfigIcons() {
  // Per-repo configuration actions — distinct glyphs matching JFrog's Configurations column.
  const ico = (ch, title) => (
    <span title={title} style={{ width: 26, height: 26, borderRadius: 4, display: "grid", placeItems: "center",
      color: C.sub, cursor: "pointer", fontSize: 13, border: `1px solid transparent` }}
      onMouseEnter={(e) => { e.currentTarget.style.background = C.surface2; e.currentTarget.style.color = C.accentDim; }}
      onMouseLeave={(e) => { e.currentTarget.style.background = "transparent"; e.currentTarget.style.color = C.sub; }}>{ch}</span>
  );
  return (
    <span style={{ display: "inline-flex", gap: 2, color: C.sub }}>
      {ico("⚙", "Index settings")}{ico("◈", "Xray config")}{ico("◉", "Watches")}{ico("⟳", "Re-index")}
    </span>
  );
}

function ScansRepos({ onOpen }) {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [sub, setSub] = useState("repositories");
  const [q, setQ] = useState("");
  const [sort, setSort] = useState({ key: "name", dir: 1 });
  useEffect(() => { api.getScans().then(setData).catch(() => setData({ configured: false, repositories: [] })).finally(() => setLoading(false)); }, []);
  const subTabs = [["git", "Git Repositories"], ["repositories", "Repositories"], ["builds", "Builds"], ["bundles", "Release Bundles"], ["packages", "Packages"]];
  let repos = (data?.repositories || []);
  if (q) repos = repos.filter((r) => r.name.toLowerCase().includes(q.toLowerCase()) || (r.format || "").toLowerCase().includes(q.toLowerCase()));
  repos = [...repos].sort((a, b) => {
    const k = sort.key; let av = a[k], bv = b[k];
    if (k === "indexedArtifacts") return (av - bv) * sort.dir;
    return String(av ?? "").localeCompare(String(bv ?? "")) * sort.dir;
  });
  const cols = [["name", "Repository"], ["format", "Package type"], ["type", "Type"], ["indexedArtifacts", "Indexed artifacts"], ["latestArtifact", "Latest artifact"], ["indexedOn", "Indexed on"], [null, "Configurations"]];
  const toggleSort = (k) => k && setSort((s) => ({ key: k, dir: s.key === k ? -s.dir : 1 }));
  return (
    <>
      <div style={{ fontSize: 22, fontWeight: 700, letterSpacing: -0.4, margin: "4px 0 14px" }}>Scans List</div>
      <div style={{ display: "flex", gap: 4, borderBottom: `1px solid ${C.line}`, marginBottom: 18 }}>
        {subTabs.map(([k, l]) => <button key={k} onClick={() => setSub(k)} style={{ ...s.hTab, ...(sub === k ? s.hTabOn : {}) }}>{l}</button>)}
      </div>
      {loading && <div style={s.kevEmpty}>Indexing repositories…</div>}
      {!loading && data && !data.configured && (
        <Card title="Repositories" desc=""><div style={{ padding: 22, color: C.sub, fontSize: 12.5, lineHeight: 1.6 }}>
          Nexus not connected (<code style={s.code}>NEXUS_URL</code> unset). Connect Nexus and run <code style={s.code}>scripts/nexus-setup.sh</code> to index repositories here.
        </div></Card>
      )}
      {!loading && data?.configured && sub === "repositories" && (
        <div style={s.card}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "16px 20px", borderBottom: `1px solid ${C.lineSoft}` }}>
            <div style={{ fontSize: 16, fontWeight: 600 }}>Repositories</div>
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 6, border: `1px solid ${C.line}`, borderRadius: 6, padding: "6px 10px", width: 220 }}>
                <span style={{ color: C.dim, fontSize: 12 }}>⌕</span>
                <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search"
                  style={{ border: "none", outline: "none", fontSize: 12.5, fontFamily: C.sans, background: "transparent", color: C.ink, width: "100%" }} />
              </div>
              <button title="Columns" style={{ width: 32, height: 32, border: `1px solid ${C.line}`, borderRadius: 6, background: C.surface, color: C.sub, cursor: "pointer" }}>⚙</button>
              <button title="Filter" style={{ width: 32, height: 32, border: `1px solid ${C.line}`, borderRadius: 6, background: C.surface, color: C.sub, cursor: "pointer" }}>▽</button>
            </div>
          </div>
          <table style={s.table}><thead><tr>
            {cols.map(([k, l]) => (
              <th key={l} onClick={() => toggleSort(k)} style={{ ...s.th, cursor: k ? "pointer" : "default", userSelect: "none" }}>
                {l}{k && <span style={{ marginLeft: 5, color: sort.key === k ? C.accent : C.dim, fontSize: 9 }}>{sort.key === k ? (sort.dir === 1 ? "▲" : "▼") : "▲"}</span>}
              </th>
            ))}
          </tr></thead><tbody>
            {repos.length === 0 && <tr><td style={s.td} colSpan={7}>No repositories indexed.</td></tr>}
            {repos.map((r) => {
              const has = r.indexedArtifacts > 0;
              return (
                <tr key={r.name} style={{ ...s.tr, cursor: has ? "pointer" : "default" }} onClick={() => has && onOpen(r.name)}>
                  <td style={{ ...s.td, fontWeight: 600, color: has ? C.accentDim : C.ink }}>{r.name}</td>
                  <td style={s.td}><PkgType format={r.format} /></td>
                  <td style={{ ...s.td, color: C.sub, fontSize: 12.5 }}>{r.type}</td>
                  <td style={{ ...s.td, fontWeight: 600 }}>{r.indexedArtifacts}</td>
                  <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5, color: C.sub, maxWidth: 300, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{r.latestArtifact || "—"}</td>
                  <td style={{ ...s.td, color: C.sub, fontSize: 11.5, whiteSpace: "nowrap" }}>{r.indexedOn || "—"}</td>
                  <td style={s.td} onClick={(e) => e.stopPropagation()}><ConfigIcons /></td>
                </tr>
              );
            })}
          </tbody></table>
        </div>
      )}
      {!loading && data?.configured && sub !== "repositories" && (
        <Card title={subTabs.find(([k]) => k === sub)?.[1]} desc=""><div style={{ padding: 22, color: C.sub, fontSize: 12.5 }}>Populates as artifacts of this kind are indexed from Nexus.</div></Card>
      )}
    </>
  );
}

function RepoArtifacts({ repo, onOpen }) {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  useEffect(() => { api.getRepoArtifacts(repo).then(setData).catch(() => setData({ artifacts: [] })).finally(() => setLoading(false)); }, [repo]);
  const arts = data?.artifacts || [];
  return (
    <>
      <div style={{ fontSize: 22, fontWeight: 700, letterSpacing: -0.4, margin: "4px 0 14px" }}>{repo}</div>
      {loading && <div style={s.kevEmpty}>Loading artifacts…</div>}
      {!loading && (
        <Card title={`Artifacts (${arts.length})`} desc="Scanned artifacts in this repository. Click one for its full scan report.">
          <Table cols={["Artifact", "Version", "Type", "Repository path", "Scan status"]}>
            {arts.length === 0 && <tr><td style={s.td} colSpan={5}>No artifacts.</td></tr>}
            {arts.map((a, i) => (
              <tr key={i} style={{ ...s.tr, cursor: "pointer" }} onClick={() => onOpen(a)}>
                <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5, color: C.accent }}>{a.fileName || a.name}</td>
                <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{a.version}</td>
                <td style={s.td}><Tag tone={C.accent}>{a.ecosystem}</Tag></td>
                <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: C.sub }}>{repo}/{a.name}</td>
                <td style={s.td}><span style={{ color: C.allow, fontSize: 11.5 }}>● Indexed</span></td>
              </tr>
            ))}
          </Table>
        </Card>
      )}
    </>
  );
}

// Artifact scan report — JFrog layout: left sub-nav (Overview/SBOM/Security Issues) + panels.
function ArtifactOverview({ repo, art }) {
  const [scan, setScan] = useState(null);
  const [loading, setLoading] = useState(true);
  const [section, setSection] = useState("overview");
  const [cve, setCve] = useState(null);
  const load = (rescan) => {
    setLoading(true);
    api.getArtifactScan(repo, art.ecosystem, art.name, art.version, rescan)
      .then(setScan).catch(() => setScan(null)).finally(() => setLoading(false));
  };
  useEffect(() => { load(false); }, [art]);

  if (loading) return <div style={s.kevEmpty}>Loading scan for {art.name}@{art.version}…</div>;
  const vulns = scan?.vulnerabilities || [];
  const sbom = scan?.sbom || [];
  const sevCounts = { Critical: scan?.critical || 0, High: scan?.high || 0, Medium: scan?.medium || 0, Low: scan?.low || 0 };
  const nav = [
    ["overview", "Overview", null],
    ["violations", "Policy Violations", 0],
    ["sbom", "SBOM", sbom.length],
    ["vulns", "Vulnerabilities", vulns.length],
    ["malicious", "Malicious Packages", vulns.filter(v => (v.id||"").startsWith("MAL")).length],
  ];
  return (
    <div style={{ display: "grid", gridTemplateColumns: "220px 1fr", gap: 18, alignItems: "start" }}>
      {/* left sub-nav */}
      <div style={{ ...s.card, padding: "10px 0", position: "sticky", top: 76 }}>
        <div style={{ padding: "6px 16px 10px", borderBottom: `1px solid ${C.lineSoft}` }}>
          <div style={{ fontSize: 10, color: C.sub, textTransform: "uppercase", letterSpacing: 0.5 }}>Scan</div>
          <div style={{ fontFamily: C.mono, fontSize: 11.5 }}>{art.name}/{art.version}</div>
        </div>
        {nav.map(([k, l, n]) => (
          <button key={k} onClick={() => setSection(k)} style={{ ...s.artNav, ...(section === k ? s.artNavOn : {}) }}>
            <span>{l}</span>{n != null && <span style={{ fontFamily: C.mono, fontSize: 11, color: n > 0 ? C.block : C.sub }}>{n}</span>}
          </button>
        ))}
      </div>
      {/* content */}
      <div>
        {section === "overview" && (
          <>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 14 }}>
              <div style={{ fontSize: 18, fontWeight: 700, fontFamily: C.mono }}>
                <Tag tone={C.accent}>{art.ecosystem}</Tag> {art.name}/{art.version}</div>
              <button onClick={() => load(true)} style={s.btnGhost}>↻ Re-scan</button>
            </div>
            <Card title="" desc="">
              <div style={{ display: "grid", gridTemplateColumns: "repeat(3,1fr)", gap: 0 }}>
                <Meta k="Repository path" v={`${repo}/${art.name}`} />
                <Meta k="Components scanned" v={String(scan?.componentsScanned ?? "—")} />
                <Meta k="Last scan" v={scan?.scannedAt ? new Date(scan.scannedAt).toLocaleString() : "—"} />
              </div>
            </Card>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 18 }}>
              <Card title="Vulnerabilities by severity" desc="">
                <div style={{ padding: 18, display: "flex", flexDirection: "column", gap: 8 }}>
                  {["Critical", "High", "Medium", "Low"].map((sv) => (
                    <div key={sv} style={{ display: "flex", justifyContent: "space-between", fontSize: 12.5 }}>
                      <span style={{ color: sevTone(sv) }}>● {sv}</span>
                      <span style={{ fontFamily: C.mono, fontWeight: 600 }}>{sevCounts[sv]}</span>
                    </div>
                  ))}
                  <div style={{ borderTop: `1px solid ${C.lineSoft}`, paddingTop: 8, marginTop: 4, display: "flex", justifyContent: "space-between", fontSize: 12.5 }}>
                    <span>Verdict</span><span style={{ fontWeight: 700, color: scan?.verdict === "Vulnerable" ? C.block : scan?.verdict === "Caution" ? C.warn : C.allow }}>{scan?.verdict || "—"}</span>
                  </div>
                </div>
              </Card>
              <Card title="Malicious packages" desc="">
                <div style={{ padding: 24, textAlign: "center" }}>
                  {vulns.some(v => (v.id||"").startsWith("MAL"))
                    ? <span style={{ color: C.block, fontWeight: 600 }}>⚠ Malicious package detected</span>
                    : <div><div style={{ fontSize: 24 }}>✓</div><div style={{ color: C.allow, fontWeight: 600, marginTop: 4 }}>Great news!</div><div style={{ color: C.sub, fontSize: 12 }}>No malicious packages were found.</div></div>}
                </div>
              </Card>
            </div>
          </>
        )}
        {section === "sbom" && <SbomView sbom={sbom} art={art} />}
        {section === "vulns" && <VulnsView vulns={vulns} onCve={setCve} />}
        {section === "violations" && <Card title="Policy Violations" desc=""><div style={{ padding: 24, textAlign: "center", color: C.sub }}><div style={{ fontSize: 22 }}>✓</div>No violations were found for this artifact.</div></Card>}
        {section === "malicious" && <Card title="Malicious Packages" desc=""><div style={{ padding: 24, textAlign: "center", color: C.sub }}>{vulns.some(v => (v.id||"").startsWith("MAL")) ? "Malicious package flagged." : "No malicious packages were found."}</div></Card>}
      </div>
      {cve && <CvePanel cve={cve} onClose={() => setCve(null)} pkgName={art.name} />}
    </div>
  );
}
function Meta({ k, v }) {
  return <div style={{ padding: "14px 18px", borderRight: `1px solid ${C.lineSoft}` }}>
    <div style={{ fontSize: 10.5, color: C.sub, textTransform: "uppercase", letterSpacing: 0.5 }}>{k}</div>
    <div style={{ fontSize: 12.5, fontFamily: C.mono, marginTop: 4, wordBreak: "break-all" }}>{v}</div></div>;
}

function SbomView({ sbom, art }) {
  const [tab, setTab] = useState("list");
  const comps = sbom || [];
  return (
    <>
      <div style={{ fontSize: 16, fontWeight: 600, marginBottom: 12 }}>SBOM ({comps.length})</div>
      <div style={{ display: "flex", gap: 4, borderBottom: `1px solid ${C.line}`, marginBottom: 16 }}>
        <button onClick={() => setTab("list")} style={{ ...s.hTab, ...(tab === "list" ? s.hTabOn : {}) }}>Components List</button>
        <button onClick={() => setTab("tree")} style={{ ...s.hTab, ...(tab === "tree" ? s.hTabOn : {}) }}>Components Tree</button>
      </div>
      {tab === "list" ? (
        <Card title="Software components" desc="Direct and transitive dependencies resolved from the artifact's full tree.">
          <Table cols={["Component", "Type", "Relation", "Depth", "Version"]}>
            {comps.length === 0 && <tr><td style={s.td} colSpan={5}>No components resolved.</td></tr>}
            {comps.map((c, i) => (
              <tr key={i} style={s.tr}>
                <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5, fontWeight: c.relation === "root" ? 600 : 400 }}>{c.name}</td>
                <td style={s.td}><Tag tone={C.accent}>{art.ecosystem}</Tag></td>
                <td style={{ ...s.td, fontSize: 11.5, color: c.relation === "Transitive" ? C.sub : C.ink }}>
                  {c.relation === "root" ? "root" : c.relation === "Direct" ? "→ Direct" : "↳ Transitive"}</td>
                <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11 }}>{c.depth}</td>
                <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{c.version}</td>
              </tr>
            ))}
          </Table>
        </Card>
      ) : (
        <Card title="Components tree" desc="Real resolved tree — indentation reflects transitive depth.">
          <div style={{ padding: 18 }}>
            {comps.length === 0 && <div style={{ color: C.sub, fontSize: 12 }}>No tree resolved.</div>}
            {comps.map((c, i) => (
              <div key={i} style={{ fontFamily: C.mono, fontSize: 11.5, padding: "3px 0",
                paddingLeft: 8 + c.depth * 22, color: c.depth === 0 ? C.ink : C.sub,
                fontWeight: c.depth === 0 ? 600 : 400 }}>
                {c.depth === 0 ? "📦 " : "→ "}{c.name}/{c.version}</div>
            ))}
          </div>
        </Card>
      )}
    </>
  );
}

function VulnsView({ vulns, onCve }) {
  const withFix = vulns.filter((v) => v.fixedVersion).length;
  const critHigh = vulns.filter((v) => v.severity === "Critical" || v.severity === "High").length;
  return (
    <>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(3,1fr)", gap: 14, marginBottom: 18 }}>
        <div style={{ ...s.kpi }}><div style={{ ...s.kpiVal, color: critHigh > 0 ? C.block : C.ink }}>{critHigh}</div><div style={s.kpiLbl}>Critical & High</div></div>
        <div style={{ ...s.kpi }}><div style={{ ...s.kpiVal, color: C.allow }}>{withFix}</div><div style={s.kpiLbl}>Includes fix version</div></div>
        <div style={{ ...s.kpi }}><div style={s.kpiVal}>{vulns.length}</div><div style={s.kpiLbl}>Total vulnerabilities</div></div>
      </div>
      <Card title={`${vulns.length} Vulnerabilities`} desc="Click a CVE for impact paths, public sources, and references.">
        <Table cols={["Severity", "CVSS", "ID", "Component", "Fix version", "CWE"]}>
          {vulns.length === 0 && <tr><td style={s.td} colSpan={6}>No vulnerabilities for this version.</td></tr>}
          {vulns.map((v) => (
            <tr key={v.id} style={{ ...s.tr, cursor: "pointer" }} onClick={() => onCve(v)}>
              <td style={s.td}><SevPill sev={v.severity} /></td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11 }}>{v.cvss ?? "—"}</td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5, color: C.accent }}>{(v.aliases || []).find(a => a.startsWith("CVE")) || v.id}</td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11 }}>{v.component || "—"}</td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5, color: v.fixedVersion ? C.allow : C.sub }}>{v.fixedVersion || "—"}</td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: C.sub }}>{(v.cwes || [])[0] || "—"}</td>
            </tr>
          ))}
        </Table>
      </Card>
    </>
  );
}

// CVE side panel — Impact Paths / Public Sources / References, like Xray.
function CvePanel({ cve, onClose, pkgName }) {
  const [tab, setTab] = useState("sources");
  const refs = cve.references || [];
  const byType = (t) => refs.filter((r) => r.type === t);
  return (
    <div style={s.cveScrim} onClick={onClose}>
      <div style={s.cvePanel} onClick={(e) => e.stopPropagation()}>
        <div style={s.modalHead}>
          <span style={{ fontFamily: C.mono, fontWeight: 600 }}>{(cve.aliases || []).find(a => a.startsWith("CVE")) || cve.id}</span>
          <button style={s.modalX} onClick={onClose}>×</button>
        </div>
        <div style={{ display: "flex", gap: 10, padding: "12px 18px", borderBottom: `1px solid ${C.lineSoft}` }}>
          <SevPill sev={cve.severity} />
          {cve.cvss != null && <span style={{ fontFamily: C.mono, fontSize: 12 }}>{cve.cvss} CVSS</span>}
        </div>
        <div style={{ display: "flex", gap: 4, padding: "10px 18px 0", borderBottom: `1px solid ${C.lineSoft}` }}>
          {[["sources", "Public Sources"], ["impact", "Impact Paths"], ["refs", "References"]].map(([k, l]) =>
            <button key={k} onClick={() => setTab(k)} style={{ ...s.hTab, ...(tab === k ? s.hTabOn : {}) }}>{l}</button>)}
        </div>
        <div style={{ padding: 18, overflow: "auto" }}>
          {tab === "sources" && (
            <div style={{ fontSize: 12.5, lineHeight: 1.6 }}>
              <div style={{ fontSize: 11, color: C.sub, textTransform: "uppercase", letterSpacing: 0.5, marginBottom: 4 }}>Summary</div>
              <p style={{ margin: "0 0 14px" }}>{cve.summary || "No summary available from the advisory."}</p>
              {cve.fixedVersion && <p style={{ margin: 0 }}><b>Patched in:</b> <span style={{ fontFamily: C.mono, color: C.allow }}>{cve.fixedVersion}</span></p>}
            </div>
          )}
          {tab === "impact" && (
            <div>
              <div style={{ fontSize: 11, color: C.sub, marginBottom: 10 }}>Dependency path from the artifact to the affected component</div>
              {(() => {
                const path = (cve.impactPath && cve.impactPath.length > 0) ? cve.impactPath : [pkgName, cve.component || pkgName];
                return path.map((node, i) => (
                  <div key={i}>
                    <div style={{ ...s.impactNode, ...(i === path.length - 1 ? { borderColor: C.block, color: C.block } : {}) }}>
                      {i === 0 ? "📦 " : ""}{node}
                      {i === path.length - 1 && <span style={{ float: "right", fontSize: 10 }}>Affected component</span>}
                      {i === 0 && path.length > 1 && <span style={{ float: "right", fontSize: 10, color: C.sub }}>root</span>}
                    </div>
                    {i < path.length - 1 && <div style={{ textAlign: "center", color: C.dim, margin: "4px 0" }}>↓</div>}
                  </div>
                ));
              })()}
            </div>
          )}
          {tab === "refs" && (
            <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
              {refs.length === 0 && <span style={{ color: C.sub, fontSize: 12 }}>No references in the advisory.</span>}
              {refs.map((r, i) => <a key={i} href={r.url} target="_blank" rel="noreferrer" style={{ color: C.accent, fontSize: 11, wordBreak: "break-all" }}>{r.url}</a>)}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// Quarantine — what's physically held in the Nexus quarantine repo right now.
function Quarantine() {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  useEffect(() => { api.getQuarantine().then(setData).catch(() => setData({ configured: false, held: [] })).finally(() => setLoading(false)); }, []);
  const held = data?.held || [];
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <div style={s.crumb}><span style={{ color: C.accent }}>Pipeline</span><span style={{ color: C.dim }}>›</span><span>Quarantine</span></div>
      <div style={{ fontSize: 22, fontWeight: 700, letterSpacing: -0.4, margin: "4px 0 14px" }}>Quarantine</div>
      <Card title="Physically held packages"
        desc="Packages sitting in the Nexus quarantine repo that the gate has not promoted. The physical holding area — not just a decision label.">
        {!loading && data && !data.configured
          ? <div style={{ padding: 22, color: C.sub, fontSize: 12.5, lineHeight: 1.6 }}>Nexus not connected (<code style={s.code}>NEXUS_URL</code> unset). When connected, packages awaiting promotion appear here.</div>
          : <Table cols={["Component", "Ecosystem", "Version", "File"]}>
              {held.length === 0 && <tr><td style={s.td} colSpan={4}>{loading ? "Loading…" : "Nothing held — quarantine is empty."}</td></tr>}
              {held.map((h, i) => (
                <tr key={i} style={s.tr}>
                  <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{h.name}</td>
                  <td style={s.td}><Tag tone={C.accent}>{h.ecosystem}</Tag></td>
                  <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{h.version}</td>
                  <td style={{ ...s.td, color: C.sub, fontSize: 11 }}>{h.fileName || "—"}</td>
                </tr>
              ))}
            </Table>}
      </Card>
    </div>
  );
}

// Intake queue — visible message queue: enqueue a package, watch it drain through the gate.
function IntakeQueue() {
  const [depth, setDepth] = useState(null);
  const [eco, setEco] = useState("npm");
  const [name, setName] = useState("");
  const [version, setVersion] = useState("");
  const [msgs, setMsgs] = useState([]); // this-session messages we enqueued
  const [busy, setBusy] = useState(false);

  // Live depth poll.
  useEffect(() => {
    let active = true;
    const tick = () => api.getQueueDepth().then((d) => active && setDepth(d)).catch(() => {});
    tick();
    const iv = setInterval(tick, 2000);
    return () => { active = false; clearInterval(iv); };
  }, []);

  const enqueue = async () => {
    if (!name.trim()) return;
    setBusy(true);
    const pkg = { ecosystem: eco, name: name.trim(), version: version.trim() || "latest" };
    try {
      const r = await api.enqueue(pkg);
      const m = { id: r.messageId, pkg: `${eco}:${pkg.name}@${pkg.version}`, status: "queued", at: Date.now() };
      setMsgs((x) => [m, ...x]);
      // Track resolution by watching processed count rise (best-effort, optimistic).
      const baseline = depth?.processed ?? 0;
      let polls = 0;
      const watch = setInterval(async () => {
        polls++;
        try {
          const d = await api.getQueueDepth();
          setDepth(d);
          if (d.processed > baseline) { setMsgs((xs) => xs.map((mm) => mm.id === m.id ? { ...mm, status: "done" } : mm)); clearInterval(watch); }
          else if (d.deadLettered > (0)) { setMsgs((xs) => xs.map((mm) => mm.id === m.id && mm.status !== "done" ? { ...mm, status: "dead" } : mm)); }
          else setMsgs((xs) => xs.map((mm) => mm.id === m.id && mm.status === "queued" ? { ...mm, status: "processing" } : mm));
        } catch {}
        if (polls > 40) clearInterval(watch);
      }, 1500);
    } catch {
      setMsgs((x) => [{ id: "?", pkg: `${eco}:${pkg.name}`, status: "error", at: Date.now() }, ...x]);
    } finally { setBusy(false); setName(""); setVersion(""); }
  };

  const stat = (k, v, tone) => (
    <div style={{ ...s.kpi, padding: "16px 18px" }}>
      <div style={{ ...s.kpiVal, color: tone || C.ink }}>{v ?? "—"}</div>
      <div style={s.kpiLbl}>{k}</div>
    </div>
  );
  const statusTone = (st) => st === "done" ? C.allow : st === "dead" || st === "error" ? C.block
    : st === "processing" ? C.warn : C.info;

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <div style={{ fontSize: 22, fontWeight: 700, letterSpacing: -0.4, marginBottom: 2 }}>Intake queue</div>
      <div style={{ color: C.sub, fontSize: 13, marginBottom: 20 }}>
        Enqueue decouples submission from evaluation — the caller never waits. Messages drain through the full gate
        with at-least-once delivery, retry, and dead-letter. Backend: durable SQL queue (in-memory fallback).
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(3,1fr)", gap: 14, marginBottom: 20 }}>
        {stat("Pending", depth?.pending, depth?.pending > 0 ? C.info : C.ink)}
        {stat("Processed", depth?.processed, C.allow)}
        {stat("Dead-lettered", depth?.deadLettered, depth?.deadLettered > 0 ? C.block : C.ink)}
      </div>

      <Card title="Enqueue a package" desc="Returns 202 immediately with a message id; the consumer evaluates it in the background.">
        <div style={{ display: "flex", gap: 10, padding: 18, flexWrap: "wrap", alignItems: "center" }}>
          <select value={eco} onChange={(e) => setEco(e.target.value)} style={s.select}>
            {CATALOG_ECOS.map((x) => <option key={x.key} value={x.key}>{x.label}</option>)}
          </select>
          <input value={name} onChange={(e) => setName(e.target.value)} onKeyDown={(e) => e.key === "Enter" && enqueue()}
            placeholder="package name" style={{ ...s.formInput, flex: "2 1 200px" }} />
          <input value={version} onChange={(e) => setVersion(e.target.value)} onKeyDown={(e) => e.key === "Enter" && enqueue()}
            placeholder="version (optional)" style={{ ...s.formInput, flex: "1 1 120px" }} />
          <button onClick={enqueue} disabled={busy} style={s.add}>{busy ? "Enqueuing…" : "Enqueue →"}</button>
        </div>
      </Card>

      <Card title="Messages this session" desc="Live status as each message moves queued → processing → done (or dead-lettered).">
        <Table cols={["Message ID", "Package", "Status", "Enqueued"]}>
          {msgs.length === 0 && <tr><td style={s.td} colSpan={4}>Nothing enqueued yet — submit a package above.</td></tr>}
          {msgs.map((m) => (
            <tr key={m.id + m.at} style={s.tr}>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{m.id}</td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{m.pkg}</td>
              <td style={s.td}>
                <span style={{ display: "inline-flex", alignItems: "center", gap: 6, color: statusTone(m.status), fontWeight: 600, fontSize: 11.5 }}>
                  <span style={{ width: 7, height: 7, borderRadius: "50%", background: statusTone(m.status),
                    animation: m.status === "processing" || m.status === "queued" ? "fwpulse 1.2s infinite" : "none" }} />
                  {m.status}
                </span>
              </td>
              <td style={{ ...s.td, color: C.sub, fontSize: 11 }}>{new Date(m.at).toLocaleTimeString()}</td>
            </tr>
          ))}
        </Table>
      </Card>
    </div>
  );
}

// OSS Catalog — JFrog-style package overview from free public sources (npm + PyPI live).
const CATALOG_ECOS = [
  { key: "npm", label: "npm", live: true }, { key: "PyPI", label: "PyPI", live: true },
  { key: "NuGet", label: "NuGet", live: false }, { key: "Cargo", label: "Cargo", live: false },
  { key: "Go", label: "Go", live: false }, { key: "HuggingFace", label: "Hugging Face", live: false },
];
function Catalog() {
  const [eco, setEco] = useState("npm");
  const [ecoOpen, setEcoOpen] = useState(false);
  const [q, setQ] = useState("");
  const [view, setView] = useState("landing");   // landing | results | package | insights
  const [results, setResults] = useState(null);
  const [pkg, setPkg] = useState(null);
  const [loading, setLoading] = useState(false);
  const [ac, setAc] = useState([]);               // autocomplete hits
  const [acOpen, setAcOpen] = useState(false);

  // live autocomplete (debounced)
  useEffect(() => {
    if (!q.trim() || q.trim().length < 2) { setAc([]); return; }
    let active = true;
    const t = setTimeout(() => {
      api.searchPackages(eco, q.trim(), 8).then((d) => { if (active) { setAc(d.results || []); setAcOpen(true); } }).catch(() => {});
    }, 250);
    return () => { active = false; clearTimeout(t); };
  }, [q, eco]);

  const runSearch = (term) => {
    const t = (term ?? q).trim(); if (!t) return;
    setQ(t); setAcOpen(false); setLoading(true); setView("results"); setResults(null);
    api.searchPackages(eco, t, 30).then((d) => setResults(d)).catch(() => setResults({ results: [], query: t })).finally(() => setLoading(false));
  };
  const openPkg = (name, version) => {
    setAcOpen(false); setLoading(true); setView("package"); setPkg(null);
    api.getPackage(eco, name, version).then(setPkg).catch(() => setPkg({ verdict: "Unknown", name, ecosystem: eco, notes: ["Lookup failed."] })).finally(() => setLoading(false));
  };

  const crumb = (
    <div style={s.crumb}>
      <span style={{ color: C.accent, cursor: "pointer" }} onClick={() => { setView("landing"); setPkg(null); setResults(null); }}>Catalog</span>
      <span style={{ color: C.dim }}>›</span>
      <span style={{ color: view === "landing" || view === "insights" ? C.ink : C.accent, cursor: "pointer" }}
        onClick={() => { setView("landing"); setPkg(null); setResults(null); }}>Explore</span>
      {view === "results" && <><span style={{ color: C.dim }}>›</span><span style={{ color: C.ink }}>{results?.query || q}</span></>}
      {view === "package" && <><span style={{ color: C.dim }}>›</span><span style={{ color: C.ink }}>{pkg?.name || "Package"}</span></>}
      {view === "insights" && <><span style={{ color: C.dim }}>›</span><span style={{ color: C.ink }}>Security Insights</span></>}
    </div>
  );

  if (view === "insights") return <div style={{ animation: "fwfade .2s ease" }}>{crumb}<SecurityInsights onClose={() => setView("landing")} onPick={(k) => { setEco(k); setView("landing"); }} /></div>;

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      {crumb}
      {/* hero */}
      <div style={{ textAlign: "center", padding: "14px 0 4px" }}>
        <div style={{ fontSize: 24, fontWeight: 700, letterSpacing: -0.5 }}>Search for open-source packages and CVEs</div>
        <div style={{ display: "inline-flex", alignItems: "center", gap: 12, color: C.sub, fontSize: 13, marginTop: 8 }}>
          Search npm &amp; PyPI packages, vulnerabilities, and project health
          <span style={{ ...s.capPill, cursor: "pointer" }} onClick={() => setView("insights")}>All from free open sources ›</span>
        </div>
      </div>
      {/* search row + autocomplete */}
      <div style={{ display: "flex", gap: 0, maxWidth: 820, margin: "18px auto 26px", position: "relative" }}>
        <button onClick={() => setEcoOpen(!ecoOpen)} style={s.ecoBtn}>
          <span style={{ display: "inline-flex", alignItems: "center", gap: 7 }}>
            <BrandIcon format={eco} />{CATALOG_ECOS.find((e) => e.key === eco)?.label || eco}</span>
          <span style={{ fontSize: 9, marginLeft: 6 }}>▾</span></button>
        {ecoOpen && (
          <div style={s.ecoMenu}>
            {CATALOG_ECOS.map((e) => (
              <button key={e.key} disabled={!e.live}
                onClick={() => { if (e.live) { setEco(e.key); setEcoOpen(false); } }}
                style={{ ...s.ecoItem, cursor: e.live ? "pointer" : "default", opacity: e.live ? 1 : 0.55 }}>
                <span style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <BrandIcon format={e.key} />{e.label}</span>
                {e.live ? <span style={{ color: C.allow, fontSize: 10 }}>● live</span>
                  : <span style={{ color: C.dim, fontSize: 10 }}>coming soon</span>}
              </button>
            ))}
          </div>
        )}
        <input value={q} onChange={(e) => setQ(e.target.value)} onFocus={() => ac.length && setAcOpen(true)}
          onKeyDown={(e) => e.key === "Enter" && runSearch()}
          placeholder="Search by package name…" style={s.catInput} />
        <button onClick={() => runSearch()} style={s.catSearchBtn}>⌕ Search</button>
        {acOpen && ac.length > 0 && (
          <div style={s.acMenu} onMouseLeave={() => setAcOpen(false)}>
            <div style={{ display: "flex", justifyContent: "space-between", padding: "8px 14px", borderBottom: `1px solid ${C.lineSoft}`, fontSize: 11.5 }}>
              <span style={{ fontWeight: 600 }}>📦 Packages</span>
              <a onClick={() => runSearch()} style={{ color: C.accent, cursor: "pointer" }}>See all</a>
            </div>
            {ac.map((h, i) => (
              <button key={i} onClick={() => openPkg(h.name)} style={s.acItem}>
                <span style={{ display: "flex", alignItems: "center", gap: 9 }}><BrandIcon format={h.ecosystem} />{h.name}</span>
                <span style={{ color: C.dim, fontSize: 11 }}>{h.latestVersion || ""}</span>
              </button>
            ))}
          </div>
        )}
      </div>

      {loading && view !== "landing" && <div style={s.kevEmpty}>Loading…</div>}
      {!loading && view === "landing" && <CatalogLanding eco={eco} setQ={setQ} search={() => runSearch()} onSample={(t) => { const at = t.lastIndexOf("@"); at > 0 ? openPkg(t.slice(0, at), t.slice(at + 1)) : runSearch(t); }} onInsights={() => setView("insights")} />}
      {!loading && view === "results" && <SearchResults data={results} eco={eco} onOpen={openPkg} />}
      {!loading && view === "package" && pkg && <PackageOverview pkg={pkg} onVersion={(v) => openPkg(pkg.name, v)} />}
    </div>
  );
}

// Search-results list (image 4): "N Results Found", sub-tabs, package table.
function SearchResults({ data, eco, onOpen }) {
  const [sub, setSub] = useState("packages");
  const rows = data?.results || [];
  const subTabs = [["packages", `Packages (${rows.length})`], ["ondemand", "On Demand Packages"], ["cves", "CVEs"]];
  return (
    <div style={{ animation: "fwfade .15s ease" }}>
      <div style={{ display: "flex", gap: 4, borderBottom: `1px solid ${C.line}`, marginBottom: 18 }}>
        {subTabs.map(([k, l]) => <button key={k} onClick={() => k === "packages" && setSub(k)}
          style={{ ...s.hTab, ...(sub === k ? s.hTabOn : {}), cursor: k === "packages" ? "pointer" : "default", opacity: k === "packages" ? 1 : 0.5 }}>
          {l}{k !== "packages" && <span style={{ fontSize: 9, marginLeft: 4 }}>soon</span>}</button>)}
      </div>
      <div style={s.card}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "16px 20px", borderBottom: `1px solid ${C.lineSoft}` }}>
          <div style={{ fontSize: 16, fontWeight: 600 }}>{rows.length} results found for “{data?.query}”</div>
          <span style={{ width: 32, height: 32, border: `1px solid ${C.line}`, borderRadius: 6, display: "grid", placeItems: "center", color: C.sub }}>⚙</span>
        </div>
        <table style={s.table}><thead><tr>
          {["Package name", "Type", "Description", "Latest version"].map((c) => <th key={c} style={s.th}>{c}</th>)}
        </tr></thead><tbody>
          {rows.length === 0 && <tr><td style={s.td} colSpan={4}>No packages matched.</td></tr>}
          {rows.map((r, i) => (
            <tr key={i} style={{ ...s.tr, cursor: "pointer" }} onClick={() => onOpen(r.name, r.latestVersion)}>
              <td style={{ ...s.td, fontWeight: 600, color: C.accentDim }}>{r.name}</td>
              <td style={s.td}><BrandIcon format={r.ecosystem} /></td>
              <td style={{ ...s.td, color: C.sub, fontSize: 12.5, maxWidth: 520 }}>{r.description || "—"}</td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{r.latestVersion || "—"}</td>
            </tr>
          ))}
        </tbody></table>
      </div>
    </div>
  );
}

// Security Insights overview (image 2) — the "fancy overview for selling".
function SecurityInsights({ onClose, onPick }) {
  const tiles = [
    { t: "CVEs — public resources", n: "OSV.dev", d: "Vulnerabilities aggregated from NVD, GHSA, PyPA, RustSec — OSV-format, multi-ecosystem.", icon: "🛡" },
    { t: "Known-exploited", n: "CISA KEV", d: "Confirmed exploited-in-the-wild CVEs. The hard-block signal.", icon: "◎" },
    { t: "Malicious packages", n: "OpenSSF", d: "Typosquats, dependency-confusion and malicious releases — caught even without a CVE.", icon: "☠" },
    { t: "Project health", n: "OpenSSF Scorecard", d: "18 automated security-health checks per repository, via deps.dev.", icon: "❤" },
  ];
  const ecos = [
    { k: "npm", label: "npm", live: true, pop: true }, { k: "PyPI", label: "PyPI", live: true, pop: true },
    { k: "NuGet", label: "NuGet", live: false }, { k: "Cargo", label: "Cargo", live: false },
    { k: "Go", label: "Go", live: false }, { k: "HuggingFace", label: "Hugging Face", live: false },
  ];
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", margin: "10px 0 16px" }}>
        <div style={{ fontSize: 20, fontWeight: 700 }}>Security Insights</div>
        <button onClick={onClose} style={{ background: "none", border: "none", fontSize: 20, color: C.sub, cursor: "pointer" }}>×</button>
      </div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit,minmax(240px,1fr))", gap: 16, marginBottom: 28 }}>
        {tiles.map((x, i) => (
          <div key={i} style={{ ...s.card, marginBottom: 0, padding: "18px 20px" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 8 }}>
              <span style={{ fontSize: 20 }}>{x.icon}</span>
              <div><div style={{ fontWeight: 700, fontSize: 14 }}>{x.t}</div>
                <div style={{ fontFamily: C.mono, fontSize: 11, color: C.accent }}>{x.n}</div></div>
            </div>
            <div style={{ color: C.sub, fontSize: 12, lineHeight: 1.5 }}>{x.d}</div>
          </div>
        ))}
      </div>
      <div style={{ fontSize: 17, fontWeight: 700, marginBottom: 14 }}>Supported package ecosystems</div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit,minmax(260px,1fr))", gap: 14 }}>
        {ecos.map((e) => (
          <div key={e.k} onClick={() => e.live && onPick(e.k)}
            style={{ ...s.card, marginBottom: 0, padding: "16px 18px", display: "flex", justifyContent: "space-between", alignItems: "center",
              cursor: e.live ? "pointer" : "default", opacity: e.live ? 1 : 0.55 }}>
            <span style={{ display: "flex", alignItems: "center", gap: 12 }}>
              <BrandIcon format={e.k} />
              <span style={{ fontWeight: 600, fontSize: 14 }}>{e.label}</span>
            </span>
            {e.pop ? <span style={{ fontSize: 10, color: C.accent, border: `1px solid ${C.accent}`, borderRadius: 4, padding: "2px 7px" }}>Live</span>
              : <span style={{ fontSize: 10, color: C.dim }}>coming soon</span>}
          </div>
        ))}
      </div>
    </div>
  );
}

function CatalogLanding({ eco, setQ, search, onSample, onInsights }) {
  const samples = eco === "PyPI"
    ? [["requests", "clean"], ["pyyaml", "vuln"], ["urllib3", "vuln"], ["flask", "clean"]]
    : [["express", "clean"], ["lodash@4.17.15", "vuln"], ["left-pad", "clean"], ["minimist@1.2.0", "vuln"]];
  const run = (term) => (onSample ? onSample(term) : (setQ(term), setTimeout(search, 0)));
  const cards = [
    { iconKind: "oss", t: "Centralized OSS Intelligence", d: "A single source of truth for open-source packages and their CVEs — research and vet packages before they enter your org. npm and PyPI live today; more coming.",
      foot: <span style={{ display: "flex", gap: 6, alignItems: "center", flexWrap: "wrap", justifyContent: "center" }}><Tag tone={C.allow}>npm</Tag><Tag tone={C.allow}>PyPI</Tag><span style={{ fontSize: 10, color: C.dim }}>NuGet · Cargo · Go · HF soon</span></span> },
    { iconKind: "sec", t: "Enriched Security & Remediation", d: "Deep vulnerability analysis with CVSS, KEV exploited-status, and the exact fixed-in version to upgrade to — actionable mitigation, not just a list.",
      foot: <span style={{ color: C.sub, fontSize: 11.5 }}>CVE example <a onClick={() => run("lodash@4.17.15")} style={{ color: C.accent, cursor: "pointer" }}>GHSA-29mw-wpgm-hmr9 ›</a></span> },
    { iconKind: "ctrl", t: "Custom Control & Policy", d: "Map packages to watches and policy rules to enforce fine-grained gate decisions, with violations attributed back to the watch that caught them.",
      foot: <span style={{ display: "flex", gap: 6, justifyContent: "center" }}><Tag tone={C.accent}>PROD-watch</Tag><Tag tone={C.warn}>License-watch</Tag></span> },
    { iconKind: "cur", t: "Curation Results in Advance", d: "Preview the gate's decision on any package before download — see if it would be allowed or blocked, and why, so teams close gaps proactively.",
      foot: <span style={{ display: "flex", gap: 12, fontSize: 11.5, justifyContent: "center" }}><span style={{ color: C.allow }}>✓ Allowed</span><span style={{ color: C.block }}>⊘ Blocked</span></span> },
  ];
  return (
    <>
      {/* Sample row: tabs (Packages/Repositories/CVEs) + Try Now, like JFrog */}
      <div style={{ display: "flex", alignItems: "center", gap: 18, margin: "0 0 6px" }}>
        <span style={{ fontSize: 14, fontWeight: 600 }}>Sample packages</span>
        <span style={{ ...s.hTabOn, fontSize: 13, paddingBottom: 6 }}>Packages</span>
        <span style={{ color: C.dim, fontSize: 13, cursor: "default" }} title="Coming soon">Repositories <span style={{ fontSize: 9, color: C.dim }}>soon</span></span>
        <span style={{ color: C.dim, fontSize: 13, cursor: "default" }} title="Coming soon">CVEs <span style={{ fontSize: 9, color: C.dim }}>soon</span></span>
        <span style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 6 }}>
          {samples.map(([term, kind]) => (
            <button key={term} onClick={() => run(term)} style={{ ...s.sampleChip,
              borderColor: kind === "vuln" ? C.block : C.line, color: kind === "vuln" ? C.block : C.ink }}>{term}</button>
          ))}
        </span>
      </div>
      <div style={{ textAlign: "center", color: C.ink, fontSize: 15, fontWeight: 600, margin: "20px 0 18px" }}>
        See how the Catalog helps you ship secure software faster
      </div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit,minmax(250px,1fr))", gap: 16 }}>
        {cards.map((c, i) => (
          <div key={i} style={{ ...s.featCard, alignItems: "center", textAlign: "center" }}>
            <FeatIcon kind={c.iconKind} />
            <div style={{ fontWeight: 600, fontSize: 15, margin: "14px 0 10px", lineHeight: 1.3 }}>{c.t}</div>
            <div style={{ color: C.sub, fontSize: 12.5, lineHeight: 1.55, flex: 1 }}>{c.d}</div>
            <div style={{ marginTop: 16, paddingTop: 14, borderTop: `1px solid ${C.lineSoft}`, width: "100%", display: "flex", justifyContent: "center" }}>{c.foot}</div>
          </div>
        ))}
      </div>
      <div style={{ textAlign: "center", color: C.dim, fontSize: 11.5, marginTop: 20 }}>
        Last catalog index updated just now · sourced from npm, PyPI, OSV.dev, CISA KEV &amp; OpenSSF — all free.
      </div>
    </>
  );
}
// Outline line-icons for the feature cards, matching JFrog's centered icon style.
function FeatIcon({ kind }) {
  const c = C.accent, p = { width: 30, height: 30, fill: "none", stroke: c, strokeWidth: 1.6, strokeLinecap: "round", strokeLinejoin: "round" };
  const wrap = (child) => (
    <span style={{ width: 56, height: 56, borderRadius: "50%", background: `${c}10`, display: "grid", placeItems: "center" }}>
      <svg viewBox="0 0 24 24" {...p}>{child}</svg></span>
  );
  if (kind === "oss") return wrap(<><path d="M21 16V8a2 2 0 0 0-1-1.7l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.7l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><path d="m3.3 7 8.7 5 8.7-5M12 22V12"/></>);
  if (kind === "sec") return wrap(<><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><circle cx="11" cy="11" r="3"/><path d="m13.5 13.5 2 2"/></>);
  if (kind === "ctrl") return wrap(<><circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M19 5l-2 2M7 17l-2 2"/></>);
  return wrap(<><circle cx="12" cy="12" r="9"/><path d="m9 12 2 2 4-4"/></>); // arrow/curation → check-circle
}

// Built-in SPDX license risk map (the licenses that matter), so the Licenses tab matches JFrog.
const SPDX = {
  MIT: { desc: "A short, permissive license requiring only preservation of copyright and license notices.", spdx: true,
    risk: { Copyright: "L", Copyleft: "L", Patent: "L", Royalty: "L" },
    perms: ["Distribution", "Modification", "Commercial use", "Private use"], lims: ["Liability", "Warranty"], conds: ["License and copyright notice"] },
  "Apache-2.0": { desc: "A permissive license with an express grant of patent rights from contributors.", spdx: true,
    risk: { Copyright: "L", Copyleft: "L", Patent: "L", Royalty: "L" },
    perms: ["Distribution", "Modification", "Commercial use", "Patent use", "Private use"], lims: ["Trademark use", "Liability", "Warranty"], conds: ["License and copyright notice", "State changes"] },
  "BSD-3-Clause": { desc: "A permissive license similar to MIT with a non-endorsement clause.", spdx: true,
    risk: { Copyright: "L", Copyleft: "L", Patent: "M", Royalty: "L" },
    perms: ["Distribution", "Modification", "Commercial use", "Private use"], lims: ["Liability", "Warranty"], conds: ["License and copyright notice"] },
  ISC: { desc: "A permissive license functionally equivalent to MIT/BSD-2.", spdx: true,
    risk: { Copyright: "L", Copyleft: "L", Patent: "L", Royalty: "L" },
    perms: ["Distribution", "Modification", "Commercial use", "Private use"], lims: ["Liability", "Warranty"], conds: ["License and copyright notice"] },
  "GPL-3.0": { desc: "A strong copyleft license — derivative works must be open-sourced under the same terms.", spdx: true,
    risk: { Copyright: "L", Copyleft: "H", Patent: "L", Royalty: "L" },
    perms: ["Distribution", "Modification", "Commercial use", "Patent use", "Private use"], lims: ["Liability", "Warranty"], conds: ["Disclose source", "License and copyright notice", "State changes", "Same license"] },
  "AGPL-3.0": { desc: "Network copyleft — even SaaS use triggers source-disclosure obligations.", spdx: true,
    risk: { Copyright: "L", Copyleft: "H", Patent: "L", Royalty: "L" },
    perms: ["Distribution", "Modification", "Commercial use", "Patent use", "Private use"], lims: ["Liability", "Warranty"], conds: ["Disclose source", "Network use is distribution", "Same license", "State changes"] },
};
function licenseInfo(name) {
  if (!name) return null;
  const key = Object.keys(SPDX).find((k) => name.toLowerCase().includes(k.toLowerCase().split("-")[0]) && name.toLowerCase().includes(k.toLowerCase().includes("gpl") ? "gpl" : k.toLowerCase().split("-")[0]));
  return SPDX[name] || SPDX[key] || { desc: `${name} — license metadata not in the built-in SPDX map.`, spdx: false, risk: {}, perms: [], lims: [], conds: [] };
}
const RISK_TONE = { H: C.block, M: C.warn, L: C.allow };

// Package detail — JFrog two-column layout: left info card + right tabbed content.
function PackageOverview({ pkg, onVersion }) {
  const [tab, setTab] = useState("vulnerabilities");
  const [verOpen, setVerOpen] = useState(false);
  const sc = pkg.scorecard;
  const vulns = pkg.vulnerabilities || [];
  const crit = vulns.filter((v) => v.severity === "Critical").length;
  const high = vulns.filter((v) => v.severity === "High").length;
  const med = vulns.filter((v) => v.severity === "Medium").length + vulns.filter((v) => v.severity === "Low").length;
  const approved = pkg.verdict === "Clean" || pkg.verdict === "Caution";
  const installCmd = pkg.ecosystem === "npm" ? `npm install ${pkg.name}@${pkg.version}` : `pip install ${pkg.name}==${pkg.version}`;
  const lic = licenseInfo(pkg.license);
  const tabs = [["vulnerabilities", "Vulnerabilities"], ["dependencies", "Dependencies"], ["openssf", "OpenSSF"], ["licenses", "Licenses"], ["oprisk", "Operational Risk"]];

  return (
    <div style={{ display: "grid", gridTemplateColumns: "340px 1fr", gap: 20, alignItems: "start", animation: "fwfade .2s ease" }}>
      {/* LEFT info card */}
      <div>
        <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 12 }}>
          <div style={{ flex: 1, position: "relative" }}>
            <button onClick={() => setVerOpen(!verOpen)} style={{ ...s.select, width: "100%", display: "flex", justifyContent: "space-between", alignItems: "center", cursor: "pointer" }}>
              <span>{pkg.version}{pkg.version === pkg.latestVersion ? "  ·  Latest" : ""}</span>
              <span style={{ fontSize: 9, color: C.sub }}>▾</span>
            </button>
            {verOpen && (
              <div style={{ position: "absolute", top: 38, left: 0, right: 0, zIndex: 30, background: C.surface,
                border: `1px solid ${C.line}`, borderRadius: 8, boxShadow: "0 12px 30px rgba(15,39,72,.18)", maxHeight: 280, overflow: "auto" }}>
                {(pkg.allVersions || []).slice(0, 200).map((v) => (
                  <button key={v} onClick={() => { setVerOpen(false); if (v !== pkg.version) onVersion(v); }}
                    style={{ display: "flex", justifyContent: "space-between", width: "100%", background: v === pkg.version ? C.surface2 : "none",
                      border: "none", padding: "8px 14px", cursor: "pointer", color: C.ink, fontFamily: C.mono, fontSize: 12, textAlign: "left" }}>
                    {v}{v === pkg.latestVersion && <span style={{ fontSize: 9, color: C.accent, border: `1px solid ${C.accent}`, borderRadius: 4, padding: "0 5px" }}>Latest</span>}
                  </button>
                ))}
                {(pkg.allVersions || []).length === 0 && <div style={{ padding: 12, color: C.sub, fontSize: 12 }}>No versions listed.</div>}
              </div>
            )}
          </div>
          <span style={{ fontSize: 12, color: C.accent, whiteSpace: "nowrap" }}>All versions ({pkg.versionCount ?? "—"})</span>
        </div>
        <div style={{ ...s.card, marginBottom: 0 }}>
          <div style={{ padding: "18px 18px 14px" }}>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
              <span style={{ display: "flex", alignItems: "center", gap: 10 }}>
                <BrandIcon format={pkg.ecosystem} />
                <span style={{ fontWeight: 700, fontSize: 16 }}>{pkg.name}</span>
              </span>
              <span style={{ fontFamily: C.mono, fontSize: 12, color: C.sub }}>{pkg.version}</span>
            </div>
            {pkg.description && <p style={{ margin: "8px 0 0", color: C.sub, fontSize: 12, lineHeight: 1.5 }}>{pkg.description}</p>}
          </div>
          {/* approval banner */}
          <div style={{ margin: "0 14px 14px", padding: "12px 14px", borderRadius: 8,
            background: approved ? "rgba(64,190,70,.1)" : "rgba(214,54,73,.08)", border: `1px solid ${approved ? C.allow : C.block}33` }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8, fontWeight: 600, fontSize: 13, color: approved ? C.allowDark || C.allow : C.block }}>
              {approved ? "✓ Approved for downloading" : "⊘ Blocked by policy"}</div>
            <div style={{ fontSize: 11.5, color: C.sub, marginTop: 3 }}>
              {approved ? "This package is allowed for download per the gate policy." : "This package would be blocked by the gate policy."}</div>
          </div>
          {/* key-value list */}
          <div style={{ padding: "0 4px 8px" }}>
            <KV k="Labels" v={<span style={{ color: C.dim, fontSize: 11.5 }}>+ Add label</span>} />
            <KV k="Published Date" v={pkg.recentVersions?.find?.((x) => x.version === pkg.version)?.published
              ? new Date(pkg.recentVersions.find((x) => x.version === pkg.version).published).toLocaleDateString() : "—"} />
            <KV k="No. of Versions" v={<a style={{ color: C.accent }}>{pkg.versionCount ?? "—"}</a>} />
            <KV k="Vulnerabilities" v={
              <span style={{ display: "flex", gap: 5 }}>
                <Cnt n={crit} c={C.block} /><Cnt n={high} c="#ef6a3d" /><Cnt n={med} c={C.warn} />
              </span>} />
            <KV k="Dependencies" v={`${pkg.dependencies?.length ?? 0}`} />
            <KV k="Licenses" v={pkg.license ? <Tag tone={C.sub}>{pkg.license}</Tag> : "—"} />
            <KV k="OpenSSF Score" v={sc?.overall != null
              ? <span style={{ fontWeight: 700, color: sc.overall >= 7 ? C.allow : sc.overall >= 4 ? C.warn : C.block }}>{sc.overall.toFixed(1)}/10</span>
              : <Tag tone={C.dim}>N/A</Tag>} last />
          </div>
          {/* Install instructions */}
          <div style={{ borderTop: `1px solid ${C.lineSoft}`, padding: "14px 16px" }}>
            <div style={{ fontSize: 11, fontWeight: 700, color: C.sub, textTransform: "uppercase", letterSpacing: 0.5, marginBottom: 8 }}>Install instructions</div>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8,
              background: C.surface2, border: `1px solid ${C.line}`, borderRadius: 6, padding: "8px 10px" }}>
              <code style={{ fontFamily: C.mono, fontSize: 11.5, color: C.ink, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{installCmd}</code>
              <button onClick={() => navigator.clipboard?.writeText(installCmd)} title="Copy"
                style={{ background: "none", border: "none", cursor: "pointer", color: C.sub, fontSize: 13 }}>⧉</button>
            </div>
          </div>
          {/* External links */}
          {(pkg.homepage || pkg.repository) && (
            <div style={{ borderTop: `1px solid ${C.lineSoft}`, padding: "14px 16px" }}>
              <div style={{ fontSize: 11, fontWeight: 700, color: C.sub, textTransform: "uppercase", letterSpacing: 0.5, marginBottom: 8 }}>External links</div>
              <div style={{ display: "flex", gap: 10 }}>
                {pkg.homepage && <a href={pkg.homepage} target="_blank" rel="noreferrer" title="Homepage"
                  style={{ width: 30, height: 30, border: `1px solid ${C.line}`, borderRadius: 6, display: "grid", placeItems: "center", color: C.sub, textDecoration: "none" }}>⌂</a>}
                {pkg.repository && <a href={pkg.repository.replace(/^git\+/, "").replace(/\.git$/, "")} target="_blank" rel="noreferrer" title="Repository"
                  style={{ width: 30, height: 30, border: `1px solid ${C.line}`, borderRadius: 6, display: "grid", placeItems: "center", color: C.sub, textDecoration: "none" }}>‹›</a>}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* RIGHT tabbed content */}
      <div>
        <div style={{ display: "flex", gap: 4, borderBottom: `1px solid ${C.line}`, marginBottom: 18 }}>
          {tabs.map(([k, l]) => <button key={k} onClick={() => setTab(k)} style={{ ...s.hTab, ...(tab === k ? s.hTabOn : {}) }}>{l}</button>)}
        </div>

        {tab === "vulnerabilities" && (
          <>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 16, marginBottom: 18 }}>
              <SumCard title="Non-Transitive Vulnerabilities" n={vulns.length} sevs={sevCountsObj(vulns)} />
              <SumCard title="Transitive Vulnerabilities" n={0} />
              <SumCard title="Enriched (EPSS + KEV)" n={vulns.filter(v => v.knownExploited || v.epss != null).length} enriched />
            </div>
            {vulns.length === 0
              ? <EmptyState title="No Vulnerabilities Found" sub="Great news! We haven't found any vulnerabilities for this version." />
              : <div style={s.card}>
                  <div style={{ padding: "14px 20px", borderBottom: `1px solid ${C.lineSoft}`, fontWeight: 600 }}>{vulns.length} Vulnerabilities</div>
                  <Table cols={["Severity", "ID", "Fix version", "CVSS v3", "CVSS v4", "KEV", "EPSS %"]}>
                  {vulns.map((v) => (
                    <tr key={v.id} style={s.tr}>
                      <td style={s.td}><SevIcon sev={v.severity} /></td>
                      <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5, color: C.accent }}>{(v.aliases || []).find(a => a.startsWith("CVE")) || v.id}</td>
                      <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5, color: v.fixedVersion ? C.allow : C.sub }}>{v.fixedVersion || "—"}</td>
                      <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: v.cvss != null ? C.block : C.sub }}>{v.cvss ?? "—"}</td>
                      <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: C.sub }}>N/A</td>
                      <td style={s.td}>{v.knownExploited ? <Tag tone={C.block}>KEV</Tag> : <span style={{ color: C.sub }}>—</span>}</td>
                      <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: C.sub }}>{v.epss != null ? `${(v.epss * 100).toFixed(2)}%` : "—"}</td>
                    </tr>
                  ))}
                </Table></div>}
          </>
        )}

        {tab === "dependencies" && (
          <div style={s.card}>
            <div style={{ padding: "14px 20px", borderBottom: `1px solid ${C.lineSoft}`, fontWeight: 600 }}>Dependencies ({pkg.dependencies?.length ?? 0})</div>
            {(pkg.dependencies?.length ?? 0) === 0
              ? <div style={{ padding: 24, textAlign: "center", color: C.sub, fontSize: 13 }}>No declared dependencies for this version.</div>
              : <div style={{ padding: 18, display: "flex", flexWrap: "wrap", gap: 7 }}>
                  {pkg.dependencies.slice(0, 80).map((d, i) => (
                    <span key={i} style={{ fontFamily: C.mono, fontSize: 11, padding: "4px 10px", background: C.surface2, border: `1px solid ${C.line}`, borderRadius: 6, color: C.ink }}>{d}</span>
                  ))}
                </div>}
          </div>
        )}

        {tab === "openssf" && (
          <div style={s.card}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "14px 20px", borderBottom: `1px solid ${C.lineSoft}` }}>
              <span style={{ display: "flex", alignItems: "center", gap: 10, fontWeight: 600 }}>
                OpenSSF Scorecard
                {sc?.overall != null && <span style={{ fontSize: 11, fontFamily: C.mono, color: sc.overall >= 7 ? C.allow : sc.overall >= 4 ? C.warn : C.block, border: `1px solid ${sc.overall >= 7 ? C.allow : sc.overall >= 4 ? C.warn : C.block}`, borderRadius: 4, padding: "1px 6px" }}>{sc.overall.toFixed(1)}/10</span>}
              </span>
              {sc?.repoUrl && <a href={sc.repoUrl} target="_blank" rel="noreferrer" style={{ color: C.accent, fontSize: 12, fontFamily: C.mono }}>{sc.repoUrl.replace("https://", "")} ›</a>}
            </div>
            {sc && (sc.checks || []).length > 0
              ? <Table cols={["Score", "Name", "Risk", "Description"]}>
                  {sc.checks.slice(0, 20).map((c) => {
                    const risk = scoreRisk(c.score);
                    return (
                      <tr key={c.name} style={s.tr}>
                        <td style={s.td}><ScoreChip score={c.score} /></td>
                        <td style={{ ...s.td, fontSize: 12.5, fontWeight: 500 }}>{c.name}</td>
                        <td style={s.td}><RiskIcon level={risk} /></td>
                        <td style={{ ...s.td, fontSize: 12, color: C.sub, maxWidth: 520, lineHeight: 1.45 }}>{c.reason || CHECK_DESC[c.name] || "—"}</td>
                      </tr>
                    );
                  })}
                </Table>
              : <EmptyState title="No Scorecard Published" sub={sc?.stars != null ? `Repository resolved (★ ${sc.stars.toLocaleString()}) but OpenSSF hasn't scored it yet.` : "No source repository resolved for this package."} />}
          </div>
        )}

        {tab === "licenses" && (
          <div>
            <div style={{ fontSize: 13, color: C.sub, marginBottom: 12 }}>{pkg.license ? "1 License" : "No license declared"}</div>
            {pkg.license && (
              <div style={s.card}>
                <div style={{ padding: "14px 20px", borderBottom: `1px solid ${C.lineSoft}`, display: "flex", alignItems: "center", gap: 8 }}>
                  <span style={{ width: 18, height: 18, borderRadius: 3, background: C.warn, color: "#fff", display: "grid", placeItems: "center", fontSize: 10, fontWeight: 700 }}>L</span>
                  <span style={{ fontWeight: 700 }}>{pkg.license}</span>
                </div>
                <div style={{ padding: 20 }}>
                  <p style={{ margin: "0 0 16px", color: C.sub, fontSize: 12.5, lineHeight: 1.6 }}>{lic.desc}</p>
                  <div style={{ display: "flex", gap: 40, marginBottom: 18, fontSize: 12.5 }}>
                    <span><b>SPDX License:</b> {lic.spdx ? "Yes" : "No"}</span>
                    <span><b>Deprecated:</b> No</span>
                  </div>
                  {Object.keys(lic.risk).length > 0 && (
                    <div style={{ display: "flex", gap: 30, marginBottom: 20, paddingBottom: 18, borderBottom: `1px solid ${C.lineSoft}` }}>
                      {Object.entries(lic.risk).map(([k, v]) => (
                        <span key={k} style={{ display: "flex", alignItems: "center", gap: 7, fontSize: 12.5 }} title={`${v === "H" ? "High" : v === "M" ? "Medium" : "Low"} ${k.toLowerCase()} risk`}>
                          <span style={{ width: 18, height: 18, borderRadius: "50%", border: `1.5px solid ${RISK_TONE[v]}`, color: RISK_TONE[v], display: "grid", placeItems: "center", fontSize: 10, fontWeight: 700 }}>L</span>
                          {k} Risk</span>
                      ))}
                    </div>
                  )}
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 24 }}>
                    <LicCol title="Permissions" items={lic.perms} ok />
                    <LicCol title="Limitations" items={lic.lims} bad />
                    <LicCol title="Conditions" items={lic.conds} info />
                  </div>
                  <div style={{ marginTop: 22 }}>
                    <a href={`https://spdx.org/licenses/${encodeURIComponent(pkg.license)}.html`} target="_blank" rel="noreferrer"
                      style={{ color: C.accent, fontSize: 12.5, fontWeight: 600 }}>↓ Download full license</a>
                  </div>
                </div>
              </div>
            )}
          </div>
        )}

        {tab === "oprisk" && <OpRiskTab risk={pkg.operationalRisk} />}
      </div>
    </div>
  );
}

// Operational Risk tab — JFrog Xray's operational-risk model (EOL/deprecated, version age,
// number of new versions, project release-cadence health) with per-factor severities.
const OPR_TONE = { High: C.block, Medium: "#ef6a3d", Low: C.warn, None: C.allow, Unknown: C.dim };
function OprBadge({ sev }) {
  const c = OPR_TONE[sev] || C.dim;
  return <span style={{ display: "inline-block", fontSize: 11, fontWeight: 700, color: c,
    border: `1px solid ${c}`, borderRadius: 4, padding: "2px 8px" }}>{sev === "None" ? "No risk" : sev}</span>;
}
function OpRiskTab({ risk }) {
  if (!risk) return <EmptyState title="No Operational Risk Data"
    sub="Operational-risk analysis is computed from registry release history (npm + PyPI)." />;
  const factors = [
    ["End-of-Life / Deprecated", risk.eol ? "High" : "None",
      risk.eol ? (risk.eolReason || "Version deprecated by maintainer") : "Not deprecated or yanked"],
    ["Version Age", risk.ageSeverity,
      risk.versionAgeMonths != null ? `${risk.versionAgeMonths} months since this version was released (${risk.releaseDate || "—"})` : "Release date unknown"],
    ["Number of New Versions", risk.newVersionsSeverity,
      risk.newerVersions != null ? `${risk.newerVersions} versions released after this one — latest is ${risk.latestVersion || "—"} (${risk.latestReleaseDate || "—"})` : "Unknown"],
    ["Project Health (release cadence)", risk.healthSeverity,
      `${risk.releasesLastYear ?? "—"} releases in the last 12 months (healthy ≥ 2/yr)`],
  ];
  return (
    <div>
      <div style={{ ...s.card, marginBottom: 18 }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "16px 20px" }}>
          <span style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <span style={{ fontWeight: 700, fontSize: 14 }}>Operational Risk</span>
            <OprBadge sev={risk.severity} />
          </span>
          <span style={{ fontSize: 12, color: C.sub }}>
            {risk.riskReason ? <>Risk reason: <b style={{ color: C.ink }}>{risk.riskReason}</b></> : "No operational risk identified"}
          </span>
        </div>
      </div>
      <div style={s.card}>
        <Table cols={["Risk factor", "Severity", "Detail"]}>
          {factors.map(([name, sev, detail]) => (
            <tr key={name} style={s.tr}>
              <td style={{ ...s.td, fontWeight: 600, fontSize: 12.5 }}>{name}</td>
              <td style={s.td}><OprBadge sev={sev} /></td>
              <td style={{ ...s.td, fontSize: 12, color: C.sub, lineHeight: 1.5 }}>{detail}</td>
            </tr>
          ))}
        </Table>
        <div style={{ padding: "12px 20px", borderTop: `1px solid ${C.lineSoft}`, fontSize: 11.5, color: C.sub }}>
          Severity model mirrors JFrog Xray's documented operational-risk calculation: EOL ⇒ High; unhealthy cadence ⇒ High;
          otherwise the worst of version-age (months ÷ 10) and new-versions (count ÷ 2) thresholds.
        </div>
      </div>
    </div>
  );
}
// OpenSSF check → short description (when the live reason is absent) + risk derivation.
const CHECK_DESC = {
  "Binary-Artifacts": "Determines if the project has generated executable (binary) artifacts in the source repository.",
  "Branch-Protection": "Determines if the default and release branches are protected with rules.",
  "CI-Tests": "Determines if the project runs tests before pull requests are merged.",
  "CII-Best-Practices": "Determines if the project has an OpenSSF (CII) Best Practices badge.",
  "Code-Review": "Determines if the project requires human code review before merging.",
  "Contributors": "Determines if the project has a set of contributors from multiple organizations.",
  "Dangerous-Workflow": "Determines if the project's GitHub Action workflows avoid dangerous patterns.",
  "Dependency-Update-Tool": "Determines if the project uses a dependency update tool.",
  "Fuzzing": "Determines if the project uses fuzzing.",
  "License": "Determines if the project has defined a license.",
  "Maintained": "Determines if the project is actively maintained.",
  "Packaging": "Determines if the project is published as a package.",
  "Pinned-Dependencies": "Determines if the project pins its build dependencies.",
  "SAST": "Determines if the project uses static analysis (SAST).",
  "Security-Policy": "Determines if the project has published a security policy.",
  "Signed-Releases": "Determines if the project cryptographically signs releases.",
  "Token-Permissions": "Determines if the project's workflows follow least-privilege.",
  "Vulnerabilities": "Determines if the project has open, unfixed vulnerabilities.",
};
// Risk from a check score: low score => higher risk.
function scoreRisk(score) {
  if (score == null) return null;
  if (score <= 2) return "H";
  if (score <= 6) return "M";
  return "L";
}
function ScoreChip({ score }) {
  const c = score == null ? C.sub : score >= 7 ? C.allow : score >= 4 ? C.warn : C.block;
  return <span style={{ display: "inline-block", minWidth: 40, textAlign: "center", fontFamily: C.mono, fontSize: 11, fontWeight: 700,
    color: c, border: `1px solid ${c}`, borderRadius: 4, padding: "2px 6px" }}>{score == null ? "—" : `${score}/10`}</span>;
}
function RiskIcon({ level }) {
  if (!level) return <span style={{ color: C.dim }}>—</span>;
  const map = { H: { c: C.block, t: "H" }, M: { c: C.warn, t: "M" }, L: { c: C.allow, t: "L" } };
  const x = map[level];
  return <span title={`${level === "H" ? "High" : level === "M" ? "Medium" : "Low"} risk`}
    style={{ width: 18, height: 18, borderRadius: "50%", background: x.c, color: "#fff", display: "inline-grid", placeItems: "center", fontSize: 10, fontWeight: 700 }}>{x.t}</span>;
}
function LicCol({ title, items, ok, bad, info }) {
  const mark = ok ? "✓" : bad ? "✕" : "i";
  const c = ok ? C.allow : bad ? C.block : C.info;
  return (
    <div>
      <div style={{ fontSize: 12, fontWeight: 700, color: c, marginBottom: 10 }}>{title}</div>
      {(items || []).length === 0 && <div style={{ color: C.dim, fontSize: 11.5 }}>—</div>}
      {(items || []).map((it, i) => (
        <div key={i} style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12.5, marginBottom: 8 }}>
          <span style={{ width: 16, height: 16, borderRadius: "50%", background: c, color: "#fff",
            display: "grid", placeItems: "center", fontSize: 9, fontWeight: 700, flexShrink: 0 }}>{mark}</span>
          <span style={{ color: C.ink }}>{it}</span></div>
      ))}
    </div>
  );
}
function KV({ k, v, last }) {
  return <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center",
    padding: "10px 14px", borderTop: `1px solid ${C.lineSoft}` }}>
    <span style={{ color: C.sub, fontSize: 12.5 }}>{k}</span>
    <span style={{ fontSize: 12.5, fontWeight: 500 }}>{v}</span></div>;
}
function Cnt({ n, c }) {
  return <span style={{ minWidth: 22, textAlign: "center", fontFamily: C.mono, fontSize: 11, fontWeight: 700,
    color: n > 0 ? c : C.dim, border: `1px solid ${n > 0 ? c : C.line}`, borderRadius: 4, padding: "1px 4px" }}>{n}</span>;
}
function sevCountsObj(vulns) {
  const o = { Critical: 0, High: 0, Medium: 0, Low: 0 };
  (vulns || []).forEach((v) => { if (o[v.severity] != null) o[v.severity]++; });
  return o;
}
// Summary card with the JFrog sliding-scale severity bar.
function SumCard({ title, n, sevs, enriched }) {
  const segs = sevs ? [["Critical", C.block], ["High", "#ef6a3d"], ["Medium", C.warn], ["Low", "#d9c441"]] : [];
  const total = sevs ? Object.values(sevs).reduce((a, b) => a + b, 0) : 0;
  return (
    <div style={{ ...s.card, marginBottom: 0, padding: "16px 18px" }}>
      <div style={{ fontSize: 13, color: C.sub, marginBottom: 8 }}>{title}</div>
      {n > 0
        ? <div style={{ fontSize: 24, fontWeight: 700, color: C.ink }}>{n}</div>
        : <div style={{ fontSize: 13, color: C.sub, marginTop: 4 }}>No vulnerabilities were found</div>}
      {/* sliding scale bar */}
      <div style={{ height: 6, borderRadius: 4, background: C.lineSoft, overflow: "hidden", marginTop: 12, display: "flex" }}>
        {enriched && n > 0 && <div style={{ width: "100%", background: C.allow }} />}
        {sevs && total > 0 && segs.map(([k, c]) => sevs[k] > 0 && (
          <div key={k} title={`${sevs[k]} ${k}`} style={{ width: `${(sevs[k] / total) * 100}%`, background: c }} />
        ))}
      </div>
    </div>
  );
}
// Severity as a JFrog-style colored icon (filled circle/triangle with letter).
function SevIcon({ sev }) {
  const map = {
    Critical: { c: C.block, t: "!" }, High: { c: "#e0533a", t: "H" },
    Medium: { c: C.warn, t: "M" }, Low: { c: "#d9c441", t: "L" }, None: { c: C.dim, t: "?" },
  };
  const x = map[sev] || map.None;
  return <span title={sev} style={{ width: 22, height: 22, borderRadius: "50%", background: x.c, color: "#fff",
    display: "inline-grid", placeItems: "center", fontSize: 11, fontWeight: 800 }}>{x.t}</span>;
}
function EmptyState({ title, sub }) {
  return <div style={{ ...s.card, padding: "48px 20px", textAlign: "center" }}>
    <div style={{ fontSize: 40, marginBottom: 10, opacity: 0.5 }}>🔍</div>
    <div style={{ fontSize: 16, fontWeight: 600 }}>{title}</div>
    <div style={{ color: C.sub, fontSize: 13, marginTop: 6, maxWidth: 360, margin: "6px auto 0" }}>{sub}</div></div>;
}
function MiniStat({ label, value, tone, mono }) {
  return (
    <div style={{ ...s.kpi, padding: "14px 16px" }}>
      <div style={{ fontSize: 17, fontWeight: 700, fontFamily: mono ? C.mono : C.sans, color: tone || C.ink, letterSpacing: -0.3,
        overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{value}</div>
      <div style={s.kpiLbl}>{label}</div>
    </div>
  );
}

// Live, searchable view of the CISA Known-Exploited Vulnerabilities catalogue the gate uses.
function KevCatalog() {
  const [q, setQ] = useState("");
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);
    const t = setTimeout(() => {
      api.getKev(q).then((d) => { if (active) { setData(d); setLoading(false); } })
        .catch(() => { if (active) { setData({ status: "Errored", total: 0, entries: [] }); setLoading(false); } });
    }, q ? 300 : 0); // debounce typing
    return () => { active = false; clearTimeout(t); };
  }, [q]);

  const entries = data?.entries || [];
  return (
    <Card title="Known-exploited vulnerabilities (CISA KEV)"
      desc="The live catalogue behind the 'known-exploited' gate rule. These CVEs are confirmed exploited in the wild — any package whose tree contains one is hard-blocked under SEC-VULN-02. Pulled directly from CISA, cached 24h.">
      <div style={s.kevBar}>
        <div style={s.kevSearch}>
          <span style={{ color: C.sub, fontSize: 13 }}>⌕</span>
          <input autoFocus value={q} onChange={(e) => setQ(e.target.value)}
            placeholder="Search CVE, vendor, product, or name…" style={s.kevInput} />
          {q && <button onClick={() => setQ("")} style={s.kevClear}>×</button>}
        </div>
        <div style={{ fontSize: 11.5, color: C.sub, display: "flex", gap: 14, alignItems: "center" }}>
          {data && <span><b style={{ color: C.ink, fontFamily: C.mono }}>{data.total?.toLocaleString?.() ?? data.total}</b> CVEs in catalogue</span>}
          {data && <Tag tone={data.status === "Ok" ? C.allow : C.warn}>{data.status}</Tag>}
        </div>
      </div>
      <div style={{ minHeight: 200 }}>
        {loading && <div style={s.kevEmpty}>Loading catalogue…</div>}
        {!loading && entries.length === 0 && <div style={s.kevEmpty}>{q ? `No KEV entries match “${q}”.` : "Catalogue unavailable."}</div>}
        {!loading && entries.length > 0 && (
          <Table cols={["CVE", "Vendor / Product", "Vulnerability", "Added", "Due", "Ransomware"]}>
            {entries.map((e) => <KevRow key={e.cveId} e={e} />)}
          </Table>
        )}
      </div>
      {!loading && entries.length > 0 && q && (
        <div style={{ padding: "8px 18px", fontSize: 11, color: C.sub }}>
          Showing {entries.length} match{entries.length === 1 ? "" : "es"} (max 100). Refine the search to narrow.
        </div>
      )}
    </Card>
  );
}
function KevRow({ e }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <tr style={{ ...s.tr, cursor: "pointer" }} onClick={() => setOpen(!open)}>
        <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5, color: C.accent }}>
          <span style={{ color: C.sub, marginRight: 6 }}>{open ? "▾" : "▸"}</span>{e.cveId}</td>
        <td style={{ ...s.td, fontSize: 12 }}>{e.vendorProject} / <span style={{ color: C.sub }}>{e.product}</span></td>
        <td style={{ ...s.td, fontSize: 12, maxWidth: 320, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{e.name}</td>
        <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: C.sub }}>{e.dateAdded}</td>
        <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: C.sub }}>{e.dueDate}</td>
        <td style={s.td}>{e.knownRansomware
          ? <Tag tone={C.block}>ransomware</Tag>
          : <span style={{ color: C.sub, fontSize: 11 }}>—</span>}</td>
      </tr>
      {open && (
        <tr><td colSpan={6} style={{ padding: "10px 18px 14px 40px", background: C.bg2, borderBottom: `1px solid ${C.line}` }}>
          <p style={{ margin: 0, fontSize: 12, lineHeight: 1.55, maxWidth: 760 }}>{e.shortDescription}</p>
          <a href={`https://nvd.nist.gov/vuln/detail/${e.cveId}`} target="_blank" rel="noreferrer"
            style={{ color: C.accent, fontSize: 11.5, display: "inline-block", marginTop: 6 }}>
            View {e.cveId} on NVD →</a>
        </td></tr>
      )}
    </>
  );
}

const RULE_TYPES = [
  { key: "CVEs", label: "CVEs", blurb: "Generate violations on CVEs by severity, CVSS, or known-exploited status." },
  { key: "Malicious", label: "Malicious packages", blurb: "Flag typosquats, dependency-confusion, and malicious releases (OpenSSF / Socket)." },
  { key: "License", label: "License", blurb: "Violations for components carrying a prohibited licence." },
];
const SEVERITIES = ["Low", "Medium", "High", "Critical"];
const newRule = () => ({ name: "", type: "CVEs", minSeverity: "High", knownExploitedOnly: false, block: true, notify: true });

// Policy-name fallback for watches persisted before PolicyName existed (matches the JFrog demo names).
const POLICY_NAME_FALLBACK = {
  "PROD-watch": "Block-Promotion-On-High-Vulnerability",
  "Security-watch": "Security_policy_1",
  "License-watch": "license-policy",
};
const polName = (w) => w.policyName || POLICY_NAME_FALLBACK[w.name] || `${w.name}-policy`;
const polType = (w) => w.policyType || (w.rules?.some((r) => r.type === "License") && !w.rules?.some((r) => r.type === "CVEs") ? "License" : "Security");

// JFrog-style green "Enabled" check.
function EnabledCheck({ on }) {
  return <span title={on ? "Enabled" : "Disabled"} style={{ width: 18, height: 18, borderRadius: "50%",
    border: `1.5px solid ${on ? C.accent : C.line}`, color: on ? C.accent : C.dim,
    display: "inline-grid", placeItems: "center", fontSize: 11, fontWeight: 700 }}>✓</span>;
}

// "View Resources (n)" link → JFrog-style Resources dialog (a popover gets clipped by the
// table card's rounded-corner overflow, so this opens a proper centered modal instead).
function ViewResources({ items, label }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <a style={s.linkGreen} onClick={() => setOpen(true)}>{label || `View Resources (${items.length})`}</a>
      {open && (
        <div style={s.modalScrim} onClick={(e) => { e.stopPropagation(); setOpen(false); }}>
          <div style={{ ...s.modal, width: "min(440px,96vw)" }} onClick={(e) => e.stopPropagation()}>
            <div style={s.modalHead}><b>Resources</b><button style={s.modalX} onClick={() => setOpen(false)}>×</button></div>
            <div style={{ padding: "16px 20px 20px" }}>
              {items.map((it, i) => (
                <div key={i} style={{ display: "flex", alignItems: "center", gap: 10, padding: "9px 0",
                  borderBottom: i < items.length - 1 ? `1px solid ${C.lineSoft}` : "none" }}>
                  <span style={{ width: 26, height: 26, borderRadius: 6, background: C.surface2, border: `1px solid ${C.line}`,
                    display: "grid", placeItems: "center", fontSize: 12, flexShrink: 0 }}>▦</span>
                  <span style={{ fontFamily: C.mono, fontSize: 12 }}>{it}</span>
                </div>
              ))}
            </div>
            <div style={s.modalFoot}><button style={s.btnGhost} onClick={() => setOpen(false)}>Close</button></div>
          </div>
        </div>
      )}
    </>
  );
}

// JFrog breadcrumb header: All Projects › Xray › Page [› leaf]
function Crumb({ trail }) {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 7, fontSize: 12.5, marginBottom: 16 }}>
      <span style={{ width: 18, height: 18, borderRadius: 4, background: C.accent, display: "grid", placeItems: "center", color: "#fff", fontSize: 10 }}>◇</span>
      {trail.map((t, i) => {
        const last = i === trail.length - 1;
        return (
          <span key={i} style={{ display: "flex", alignItems: "center", gap: 7 }}>
            {t.onClick && !last
              ? <a style={s.linkGreen} onClick={t.onClick}>{t.label}</a>
              : <span style={{ color: last ? C.ink : C.sub, fontWeight: last ? 700 : 400 }}>{t.label}</span>}
            {!last && <span style={{ color: C.dim }}>›</span>}
          </span>
        );
      })}
    </div>
  );
}

// ── Watches & Policies (JFrog) ───────────────────────────────────────────────
// Two sub-tabs: Watches (curated list) and Policies (step-by-step wizard editor).
function WatchesPolicies({ policy, setPolicy, onViewKev, save, saving }) {
  const [sub, setSub] = useState("watches");
  const [wizard, setWizard] = useState(null); // { wi } existing | { wi:null } new
  const watches = policy.watches || [];

  const setWatch = (i, patch) => setPolicy((p) => ({ ...p,
    watches: p.watches.map((w, j) => j === i ? { ...w, ...patch } : w) }));

  if (wizard) return (
    <PolicyWizard
      watch={wizard.wi != null ? watches[wizard.wi] : null}
      onCancel={() => setWizard(null)}
      onViewKev={onViewKev}
      saving={saving}
      onSave={async (w) => {
        setPolicy((p) => ({ ...p, watches: wizard.wi != null
          ? p.watches.map((x, j) => j === wizard.wi ? w : x)
          : [...p.watches, w] }));
        setWizard(null);
      }}
    />
  );

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "Xray" }, { label: "Watches & Policies" }]} />
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 4 }}>
        <div style={{ display: "flex", gap: 22 }}>
          {[["watches", "Watches"], ["policies", "Policies"]].map(([k, l]) => (
            <button key={k} onClick={() => setSub(k)} style={{ ...s.jfTab, ...(sub === k ? s.jfTabOn : {}) }}>{l}</button>
          ))}
        </div>
        <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
          {sub === "policies" && <button style={s.add} onClick={() => setWizard({ wi: null })}>+ New Policy</button>}
          <button onClick={save} disabled={saving} style={s.btnGhost}>{saving ? "Signing…" : "Commit & sign policy"}</button>
        </div>
      </div>

      {sub === "watches" && (
        <div style={s.card}>
          <table style={s.table}><thead><tr>
            {["Name", "Description", "Resources", "Assigned Policies", "Enabled"].map((c) => <th key={c} style={s.th}>{c}</th>)}
          </tr></thead><tbody>
            {watches.map((w, wi) => (
              <tr key={wi} style={s.tr}>
                <td style={s.td}><a style={s.linkDark} onClick={() => setWizard({ wi })}>{w.name}</a></td>
                <td style={{ ...s.td, color: C.sub, maxWidth: 320 }}>{w.description}</td>
                <td style={s.td}><ViewResources items={(w.ecosystems?.length) ? w.ecosystems : ["All ecosystems (repositories + builds)"]} label={`View Resources (${w.ecosystems?.length || 1})`} /></td>
                <td style={s.td}><a style={s.linkGreen} onClick={() => setWizard({ wi })}>1 | {polName(w)}</a></td>
                <td style={s.td}><Switch on={w.enabled} onChange={(v) => setWatch(wi, { enabled: v })} /></td>
              </tr>
            ))}
          </tbody></table>
        </div>
      )}

      {sub === "policies" && (
        <div style={s.card}>
          <table style={s.table}><thead><tr>
            {["Name", "Policy Type", "Rules", "Applied On", "Actions"].map((c) => <th key={c} style={s.th}>{c}</th>)}
          </tr></thead><tbody>
            {watches.map((w, wi) => (
              <tr key={wi} style={s.tr}>
                <td style={s.td}><a style={s.linkDark} onClick={() => setWizard({ wi })}>{polName(w)}</a></td>
                <td style={s.td}><Tag tone={polType(w) === "License" ? C.warn : C.accent}>{polType(w)}</Tag></td>
                <td style={{ ...s.td, color: C.sub }}>{w.rules.length} Rule{w.rules.length === 1 ? "" : "s"}{w.rules[0] ? ` · ${w.rules[0].name}` : ""}</td>
                <td style={s.td}><span style={{ fontFamily: C.mono, fontSize: 11.5 }}>{w.name}</span></td>
                <td style={s.td}><button style={s.miniBtn} onClick={() => setWizard({ wi })}>Edit</button></td>
              </tr>
            ))}
          </tbody></table>
        </div>
      )}
      <Callout>A policy is a named rule-set; a watch applies it to a resource scope. Both live in the
        signed policy document — <b>Commit &amp; sign</b> to version and seal changes.</Callout>
    </div>
  );
}

// ── Step-by-step policy editor (carbon of JFrog's policy wizard) ─────────────
// 1 Policy Details → 2 Policy Rules List → 3 Apply on Scope, with the numbered
// circle/dashed-line stepper down the left and Cancel / Save Policy at bottom right.
function PolicyWizard({ watch, onCancel, onSave, onViewKev, saving }) {
  const fresh = watch == null;
  const [w, setW] = useState(() => watch ? JSON.parse(JSON.stringify(watch)) : {
    name: "", description: "", ecosystems: [], enabled: true,
    policyName: "", policyType: "Security", rules: [],
  });
  const [step, setStep] = useState(1);          // current open section
  const [ruleEd, setRuleEd] = useState(null);   // { ri, rule } | null
  const set = (patch) => setW((x) => ({ ...x, ...patch }));
  const name = w.policyName || (watch ? polName(watch) : "");
  const type = w.policyType || (watch ? polType(watch) : "Security");
  const done1 = !!(name || "").trim();
  const canSave = done1 && w.rules.length > 0 && (w.name || "").trim();

  const saveRule = (ri, rule) => set({ rules: ri == null ? [...w.rules, rule] : w.rules.map((r, k) => k === ri ? rule : r) });

  // Plain function (NOT a component) so inputs inside keep identity/focus across re-renders.
  const stepSection = (n, ok, title, summary, children) => (
    <div key={n} style={{ display: "grid", gridTemplateColumns: "44px 1fr", gap: 0, marginBottom: 4 }}>
      <div style={{ display: "flex", flexDirection: "column", alignItems: "center" }}>
        <div style={{ ...s.stepCircle, ...(step === n ? s.stepCircleOn : ok ? s.stepCircleDone : {}) }}>
          {ok && step !== n ? "✓" : n}</div>
        {n < 3 && <div style={s.stepLine} />}
      </div>
      <div style={{ ...s.card, marginBottom: 16 }}>
        <button style={s.stepHead} onClick={() => setStep(step === n ? 0 : n)}>
          <span style={{ display: "flex", gap: 14, alignItems: "baseline" }}>
            <b style={{ fontSize: 13.5 }}>{title}</b>
            {step !== n && summary && <span style={{ color: C.sub, fontSize: 12.5 }}>{summary}</span>}
          </span>
          <span style={{ color: C.sub, fontSize: 11 }}>{step === n ? "⌃" : "⌄"}</span>
        </button>
        {step === n && <div style={{ padding: "4px 22px 20px" }}>{children}</div>}
      </div>
    </div>
  );

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "Xray" },
        { label: "Watches & Policies", onClick: onCancel }, { label: fresh ? "New Policy" : name }]} />

      {stepSection(1, done1, "Policy Details", done1 ? `${name} · ${type}` : "", <>
        <label style={s.fieldLbl}>* Policy Name</label>
        <input style={{ ...s.formInput, width: 340 }} placeholder="e.g. Block-Promotion-On-High-Vulnerability"
          value={name} onChange={(e) => set({ policyName: e.target.value })} />
        <div style={{ marginTop: 16 }}>
          <label style={s.fieldLbl}>Add Description <span style={{ color: C.dim, fontWeight: 400, textTransform: "none" }}>(Optional)</span></label>
          <input style={{ ...s.formInput, width: 480 }} placeholder="What this policy enforces"
            value={w.description} onChange={(e) => set({ description: e.target.value })} />
        </div>
        <div style={{ marginTop: 18 }}>
          <div style={{ fontSize: 12.5, fontWeight: 600, marginBottom: 10 }}>Select Policy Type</div>
          <div style={{ display: "flex", gap: 26, alignItems: "center" }}>
            {[["Security", "🛡", true], ["License", "⚖", true], ["Operational Risk", "🔧", false]].map(([t, ic, on]) => (
              <label key={t} title={on ? "" : "Operational risk is enforced globally via control SEC-OPR-01 (Policy controls)"}
                style={{ display: "flex", alignItems: "center", gap: 7, fontSize: 12.5, cursor: on ? "pointer" : "not-allowed", opacity: on ? 1 : 0.45 }}>
                <span style={{ ...s.radio, ...(type === t ? s.radioOn : {}) }}
                  onClick={() => on && set({ policyType: t })} />
                <span>{ic}</span>{t}
              </label>
            ))}
          </div>
        </div>
        <button style={{ ...s.add, marginTop: 20 }} disabled={!done1} onClick={() => setStep(2)}>Next ›</button>
      </>)}

      {stepSection(2, w.rules.length > 0, "Policy Rules List",
        w.rules.length ? `${w.rules.length} Rule${w.rules.length === 1 ? "" : "s"}   ${w.rules[0].name}` : "No rules yet", <>
        {w.rules.map((r, ri) => (
          <div key={ri} style={s.ruleRow}>
            <div style={{ display: "flex", alignItems: "center", gap: 10, minWidth: 0 }}>
              <Tag tone={r.type === "License" ? C.warn : r.type === "Malicious" ? C.block : C.accent}>{r.type}</Tag>
              <span style={{ fontFamily: C.mono, fontSize: 11.5, fontWeight: 600 }}>{r.name || "(unnamed rule)"}</span>
              <span style={{ color: C.sub, fontSize: 11.5 }}>
                IF {r.type === "CVEs" ? (r.knownExploitedOnly ? "known-exploited (KEV)" : `severity ≥ ${r.minSeverity}`)
                  : r.type === "License" ? "prohibited licence" : "malicious package"}
                {" → "}<b style={{ color: r.block ? C.block : C.sub }}>{r.block ? "block" : "no block"}</b>
                {r.notify && <span style={{ color: C.sub }}>, notify</span>}
              </span>
            </div>
            <div style={{ display: "flex", gap: 6 }}>
              <button style={s.miniBtn} onClick={() => setRuleEd({ ri, rule: { ...r } })}>Edit</button>
              <button style={{ ...s.miniBtn, color: C.block }} onClick={() => set({ rules: w.rules.filter((_, k) => k !== ri) })}>Remove</button>
            </div>
          </div>
        ))}
        <button style={{ ...s.addRuleBtn, marginTop: 10 }} onClick={() => setRuleEd({ ri: null, rule: newRule() })}>+ New Rule</button>
        <div><button style={{ ...s.add, marginTop: 16 }} disabled={w.rules.length === 0} onClick={() => setStep(3)}>Next ›</button></div>
      </>)}

      {stepSection(3, !!(w.name || "").trim(), "Apply on Scope",
        (w.name || "").trim() ? `1 Watches   ${w.name}` : "Choose the watch this policy applies to", <>
        <label style={s.fieldLbl}>* Watch Name</label>
        <input style={{ ...s.formInput, width: 340 }} placeholder={name ? `${name}-watch` : "e.g. PROD-watch"}
          value={w.name} onChange={(e) => set({ name: e.target.value })} />
        <div style={{ marginTop: 16 }}>
          <label style={s.fieldLbl}>Resource scope — ecosystems <span style={{ color: C.dim, fontWeight: 400, textTransform: "none" }}>(none selected = all)</span></label>
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap", marginTop: 6 }}>
            {ECOS.map((e) => {
              const on = (w.ecosystems || []).includes(e);
              return <button key={e} onClick={() => set({ ecosystems: on ? w.ecosystems.filter((x) => x !== e) : [...(w.ecosystems || []), e] })}
                style={{ ...s.chipBtn, ...(on ? s.chipBtnOn : {}) }}>{e}</button>;
            })}
          </div>
        </div>
        <label style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12.5, color: C.sub, marginTop: 16 }}>
          Watch enabled <Switch on={w.enabled} onChange={(v) => set({ enabled: v })} />
        </label>
      </>)}

      <div style={{ display: "flex", justifyContent: "flex-end", gap: 12, padding: "8px 0 30px" }}>
        <button style={s.btnGhost} onClick={onCancel}>Cancel</button>
        <button style={{ ...s.add, opacity: canSave ? 1 : 0.5 }} disabled={!canSave || saving}
          onClick={() => onSave({ ...w, policyName: name, policyType: type })}>
          {saving ? "Saving…" : "Save Policy"}</button>
      </div>

      {ruleEd && (
        <RuleModal initial={ruleEd.rule}
          onCancel={() => setRuleEd(null)}
          onSave={(rule) => { saveRule(ruleEd.ri, rule); setRuleEd(null); }}
          onViewKev={() => { setRuleEd(null); onViewKev && onViewKev(); }} />
      )}
    </div>
  );
}

// Guided IF (condition) → THEN (actions) rule editor, modeled on JFrog's policy-rule dialog.
function RuleModal({ initial, onCancel, onSave, onViewKev }) {
  const [r, setR] = useState(initial);
  const set = (patch) => setR((x) => ({ ...x, ...patch }));
  return (
    <div style={s.modalScrim} onClick={onCancel}>
      <div style={s.modal} onClick={(e) => e.stopPropagation()}>
        <div style={s.modalHead}>
          <span style={{ fontSize: 14, fontWeight: 600 }}>{initial.name ? "Edit policy rule" : "Create policy rule"}</span>
          <button style={s.modalX} onClick={onCancel}>×</button>
        </div>
        <div style={{ padding: "16px 18px" }}>
          <label style={s.fieldLbl}>Rule name</label>
          <input value={r.name} placeholder="e.g. Block-high-vuln" onChange={(e) => set({ name: e.target.value })}
            style={{ ...s.formInput, width: "100%", flex: "none", marginBottom: 16 }} />

          <div style={s.ifThen}>
            {/* IF column */}
            <div style={s.ifCol}>
              <div style={s.colTag}>IF — condition is met</div>
              <label style={s.fieldLbl}>Rule type</label>
              <div style={{ display: "flex", flexDirection: "column", gap: 6, marginBottom: 14 }}>
                {RULE_TYPES.map((t) => (
                  <button key={t.key} onClick={() => set({ type: t.key })}
                    style={{ ...s.typeCard, ...(r.type === t.key ? s.typeCardOn : {}) }}>
                    <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                      <span style={{ ...s.radio, ...(r.type === t.key ? s.radioOn : {}) }} />
                      <span style={{ fontWeight: 600, fontSize: 12.5 }}>{t.label}</span>
                    </div>
                    <div style={{ color: C.sub, fontSize: 11, marginTop: 3, lineHeight: 1.4 }}>{t.blurb}</div>
                  </button>
                ))}
              </div>

              {r.type === "CVEs" && (
                <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                  <label style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12 }}>
                    <input type="checkbox" checked={r.knownExploitedOnly}
                      onChange={(e) => set({ knownExploitedOnly: e.target.checked })} />
                    Known-exploited (KEV) only
                  </label>
                  {r.knownExploitedOnly && onViewKev && (
                    <button type="button" onClick={onViewKev}
                      style={{ alignSelf: "flex-start", background: "none", border: "none", padding: 0,
                        color: C.accent, fontSize: 11.5, cursor: "pointer", textDecoration: "underline" }}>
                      → Browse the live KEV catalogue this rule matches
                    </button>
                  )}
                  {!r.knownExploitedOnly && (
                    <div>
                      <label style={s.fieldLbl}>Minimum severity</label>
                      <select value={r.minSeverity} onChange={(e) => set({ minSeverity: e.target.value })} style={s.select}>
                        {SEVERITIES.map((sv) => <option key={sv}>{sv}</option>)}
                      </select>
                    </div>
                  )}
                </div>
              )}
              {r.type !== "CVEs" && (
                <div style={{ color: C.sub, fontSize: 11.5, lineHeight: 1.5 }}>
                  {r.type === "License" ? "Triggers on any component whose licence is on the prohibited list (LEG-LIC-01)."
                    : "Triggers on any component flagged by the malicious-package feeds (OpenSSF / Socket)."}
                </div>
              )}
            </div>

            {/* THEN column */}
            <div style={s.thenCol}>
              <div style={s.colTag}>THEN — do these actions</div>
              <ActionGroup title="Generate">
                <ActionCheck checked disabled label="Generate violation"
                  hint="Always on — the decision is recorded in the ledger." />
              </ActionGroup>
              <ActionGroup title="Notify">
                <ActionCheck checked={r.notify} onChange={(v) => set({ notify: v })} label="Notify / emit event"
                  hint="Surface in Violations and fire the ITSM webhook if configured." />
              </ActionGroup>
              <ActionGroup title="Block">
                <ActionCheck checked={r.block} onChange={(v) => set({ block: v })} label="Block the artifact"
                  hint="Fail enforce (403) and hold promotion when this condition matches." />
              </ActionGroup>
            </div>
          </div>
        </div>
        <div style={s.modalFoot}>
          <button style={s.btnGhost} onClick={onCancel}>Cancel</button>
          <button style={s.btnPrimary} disabled={!r.name.trim()} onClick={() => onSave(r)}>Save rule</button>
        </div>
      </div>
    </div>
  );
}
function ActionGroup({ title, children }) {
  return (
    <div style={{ marginBottom: 14 }}>
      <div style={{ fontSize: 11, fontWeight: 600, color: C.head, marginBottom: 6 }}>{title}</div>
      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>{children}</div>
    </div>
  );
}
function ActionCheck({ checked, onChange, disabled, label, hint }) {
  return (
    <label style={{ display: "flex", gap: 8, alignItems: "flex-start", opacity: disabled ? 0.7 : 1,
      cursor: disabled ? "default" : "pointer" }}>
      <input type="checkbox" checked={checked} disabled={disabled}
        onChange={(e) => onChange && onChange(e.target.checked)} style={{ marginTop: 2 }} />
      <span>
        <div style={{ fontSize: 12 }}>{label}</div>
        {hint && <div style={{ fontSize: 10.5, color: C.sub, lineHeight: 1.4 }}>{hint}</div>}
      </span>
    </label>
  );
}

// ── Watch Violations (JFrog) ─────────────────────────────────────────────────
// List view: curated watches table (Name / Resources / Policies / Violations "Calculate" /
// Project Name / Enabled). Clicking a watch opens the per-finding violations drill-down.
function WatchViolations({ policy, setPolicy, rows }) {
  const [sel, setSel] = useState(null);          // watch name | null
  const [calc, setCalc] = useState({});           // name -> count (resolved on "Calculate")
  const [q, setQ] = useState("");
  const watches = (policy.watches || []).filter((w) => !q || w.name.toLowerCase().includes(q.toLowerCase()));
  const setWatch = (name, patch) => setPolicy((p) => ({ ...p,
    watches: p.watches.map((w) => w.name === name ? { ...w, ...patch } : w) }));

  const calculate = (name) => {
    api.getViolationsDetailed(name).then((d) => setCalc((c) => ({ ...c, [name]: d.count })))
      .catch(() => setCalc((c) => ({ ...c, [name]: rows.filter((v) => v.watch === name).length })));
  };

  if (sel) return <WatchViolationsDetail watch={watches.find((w) => w.name === sel) || { name: sel }} onBack={() => setSel(null)} />;

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "Xray" }, { label: "Watch Violations" }]} />
      <div style={{ display: "flex", justifyContent: "flex-end", gap: 8, marginBottom: 12 }}>
        <div style={s.kevSearch}><span style={{ color: C.sub, fontSize: 13 }}>⌕</span>
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder=""
            style={{ border: "none", outline: "none", background: "transparent", fontSize: 12.5, fontFamily: C.sans, width: 160 }} /></div>
        <button style={s.iconBtn} title="Settings">⚙</button>
      </div>
      <div style={s.card}>
        <table style={s.table}><thead><tr>
          {["Name", "Resources", "Policies", "Violations", "Project Name", "Enabled"].map((c) => <th key={c} style={s.th}>{c}</th>)}
        </tr></thead><tbody>
          {watches.map((w) => (
            <tr key={w.name} style={s.tr}>
              <td style={s.td}><a style={s.linkDark} onClick={() => setSel(w.name)}>{w.name}</a></td>
              <td style={s.td}><ViewResources items={(w.ecosystems?.length) ? w.ecosystems : ["All ecosystems (repositories + builds)"]} label={`View Resources (${w.ecosystems?.length || 1})`} /></td>
              <td style={{ ...s.td, fontSize: 12.5 }}>1 | {polName(w)}</td>
              <td style={s.td}>
                {calc[w.name] != null
                  ? <a style={{ ...s.linkDark, fontWeight: 600 }} onClick={() => setSel(w.name)}>{calc[w.name]}</a>
                  : <a style={s.linkDark} onClick={() => calculate(w.name)}>Calculate</a>}</td>
              <td style={{ ...s.td, fontWeight: 600 }}>All</td>
              <td style={s.td}><span onClick={() => setWatch(w.name, { enabled: !w.enabled })} style={{ cursor: "pointer" }}><EnabledCheck on={w.enabled} /></span></td>
            </tr>
          ))}
        </tbody></table>
      </div>
    </div>
  );
}

// Per-watch violations drill-down — one row per finding, JFrog columns.
function WatchViolationsDetail({ watch, onBack }) {
  const [data, setData] = useState(null);
  const [filters, setFilters] = useState(false);
  const [sevs, setSevs] = useState([]); // active severity filters
  useEffect(() => {
    api.getViolationsDetailed(watch.name).then(setData).catch(() => setData({ count: 0, rows: [] }));
  }, [watch.name]);

  const rows = (data?.rows || []).filter((r) => sevs.length === 0 || sevs.includes(r.severity));
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
        <Crumb trail={[{ label: "All Projects" }, { label: "Xray" },
          { label: "Watch Violations", onClick: onBack }, { label: watch.name }]} />
        <button style={{ ...s.btnGhost, display: "flex", gap: 7, alignItems: "center" }} onClick={() => setFilters((f) => !f)}>⫩ Filters</button>
      </div>

      {filters && (
        <div style={{ display: "flex", gap: 8, marginBottom: 12, alignItems: "center" }}>
          <span style={{ fontSize: 11.5, color: C.sub }}>Severity:</span>
          {["Critical", "High", "Medium", "Low"].map((sv) => {
            const on = sevs.includes(sv);
            return <button key={sv} onClick={() => setSevs(on ? sevs.filter((x) => x !== sv) : [...sevs, sv])}
              style={{ ...s.chipBtn, ...(on ? s.chipBtnOn : {}) }}>{sv}</button>;
          })}
        </div>
      )}

      {!data ? <div style={s.kevEmpty}>Loading violations…</div> : (
        <>
          <div style={{ marginBottom: 10 }}>
            <b style={{ fontSize: 15 }}>{rows.length} Violations</b>
            <span style={{ color: C.sub, fontSize: 12, marginLeft: 8 }}>
              - Displaying results according to your user permissions and filters. This Watch might include additional violations.</span>
          </div>
          <div style={s.card}>
            <table style={s.table}><thead><tr>
              {["ID", "Severity", "Type", "Violated Resources", "Component", "Impacted Artifact", "Updated", "Policies"].map((c) => <th key={c} style={s.th}>{c}</th>)}
            </tr></thead><tbody>
              {rows.length === 0 && <tr><td style={s.td} colSpan={8}>No violations recorded for this watch yet — they appear when the gate blocks or quarantines a matching package.</td></tr>}
              {rows.map((v, i) => (
                <tr key={i} style={s.tr}>
                  <td style={{ ...s.td, whiteSpace: "nowrap" }}>
                    <span style={{ fontFamily: C.mono, fontSize: 11.5 }}>{String(v.id).length > 18 ? String(v.id).slice(0, 16) + "…" : v.id}</span>
                    {v.knownExploited && <span title="Known exploited (CISA KEV)" style={s.kevBadge}>KEV</span>}</td>
                  <td style={{ ...s.td, whiteSpace: "nowrap" }}>
                    <span style={{ display: "inline-flex", alignItems: "center", gap: 7 }}>
                      <SevIcon sev={v.severity} /><span style={{ fontSize: 12.5 }}>{v.severity}</span></span></td>
                  <td style={s.td}>{v.type}</td>
                  <td style={s.td}><ViewResources items={[v.impactedArtifact]} label="View Resources (1)" /></td>
                  <td style={{ ...s.td, fontSize: 12.5 }}>{v.component}</td>
                  <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: C.sub, maxWidth: 200, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}
                    title={v.impactedArtifact}>{v.impactedArtifact}</td>
                  <td style={{ ...s.td, color: C.sub, fontSize: 11.5, whiteSpace: "nowrap" }}>
                    {new Date(v.updated).toLocaleString(undefined, { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" })}</td>
                  <td style={{ ...s.td, fontSize: 11.5, maxWidth: 140, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}
                    title={v.policy || ""}>{v.policy ? `1 | ${v.policy}` : "—"}</td>
                </tr>
              ))}
            </tbody></table>
          </div>
        </>
      )}
    </div>
  );
}

// ── On-Demand Scanning (JFrog) ───────────────────────────────────────────────
// Source code / Binary tabs, scan count, filter, results table, empty state.
// "New Scan" runs any package through the real gate engine (row: Scanning → Done).
function OnDemandScanning() {
  const [tab, setTab] = useState("binary");
  const [data, setData] = useState(null);
  const [q, setQ] = useState("");
  const [adding, setAdding] = useState(false);

  const load = () => api.odsList().then(setData).catch(() => setData({ count: 0, scans: [] }));
  useEffect(() => { load(); }, []);
  // poll while any scan is still running
  useEffect(() => {
    if (!data?.scans?.some((x) => x.status === "Scanning")) return;
    const t = setInterval(load, 3000);
    return () => clearInterval(t);
  }, [data]);

  const scans = (data?.scans || []).filter((x) => !q || x.fileName.toLowerCase().includes(q.toLowerCase()));
  const startScan = async (pkg) => { setAdding(false); await api.odsScan(pkg).catch(() => {}); load(); };

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "Xray" }, { label: "On-Demand Scanning" }]} />
      <div style={{ display: "flex", gap: 22, borderBottom: `1px solid ${C.line}`, marginBottom: 16 }}>
        {[["source", "Source code scans"], ["binary", "Binary scans"]].map(([k, l]) => (
          <button key={k} onClick={() => setTab(k)} style={{ ...s.jfTab, ...(tab === k ? s.jfTabOn : {}) }}>{l}</button>
        ))}
      </div>

      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12 }}>
        <b style={{ fontSize: 14 }}>{tab === "binary" ? scans.length : 0} On Demand Scans</b>
        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          {tab === "binary" && <button style={s.add} onClick={() => setAdding(true)}>+ New Scan</button>}
          <div style={s.kevSearch}><span style={{ color: C.sub, fontSize: 13 }}>⌕</span>
            <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Filter"
              style={{ border: "none", outline: "none", background: "transparent", fontSize: 12.5, fontFamily: C.sans, width: 140 }} /></div>
          <button style={s.iconBtn} title="Settings">⚙</button>
        </div>
      </div>

      <div style={s.card}>
        <table style={s.table}><thead><tr>
          {["File Name", "Top Security Severity", "Security Issues", "Violations", "Scan Date"].map((c) => <th key={c} style={s.th}>{c}</th>)}
        </tr></thead><tbody>
          {(tab === "source" || scans.length === 0) && (
            <tr><td colSpan={5} style={{ padding: "56px 20px", textAlign: "center" }}>
              <div style={{ fontSize: 44, marginBottom: 12, opacity: 0.55 }}>🔍</div>
              <div style={{ fontSize: 15, fontWeight: 600 }}>No Results were found</div>
              <div style={{ color: C.sub, fontSize: 12.5, marginTop: 5 }}>
                {tab === "source" ? "Source-code scanning is not configured for this instance" : "There are no on demand scans"}</div>
            </td></tr>
          )}
          {tab === "binary" && scans.map((x) => (
            <tr key={x.id} style={s.tr}>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 12 }}>{x.fileName}
                <span style={{ color: C.dim, fontSize: 10.5, marginLeft: 8 }}>{x.ecosystem}</span></td>
              <td style={s.td}>
                {x.status === "Scanning"
                  ? <span style={{ color: C.info, fontWeight: 600, animation: "fwpulse 1.2s infinite" }}>Scanning…</span>
                  : x.status === "Failed" ? <span style={{ color: C.block }}>Failed</span>
                  : x.topSeverity === "None" ? <span style={{ color: C.sub }}>—</span>
                  : <span style={{ display: "inline-flex", alignItems: "center", gap: 7 }}>
                      <SevIcon sev={x.topSeverity} /><span style={{ fontSize: 12.5 }}>{x.topSeverity}</span></span>}</td>
              <td style={{ ...s.td, fontWeight: 600 }}>{x.status === "Done" ? x.securityIssues : "—"}</td>
              <td style={{ ...s.td, fontWeight: 600, color: x.violations > 0 ? C.block : C.ink }}>{x.status === "Done" ? x.violations : "—"}</td>
              <td style={{ ...s.td, color: C.sub, fontSize: 11.5, whiteSpace: "nowrap" }}>
                {new Date(x.scanDate).toLocaleString(undefined, { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" })}</td>
            </tr>
          ))}
        </tbody></table>
      </div>
      <div style={{ display: "flex", justifyContent: "flex-end", padding: "12px 4px" }}>
        <span style={{ fontSize: 11.5, border: `1px solid ${C.accent}`, color: C.accentDim, borderRadius: 4, padding: "2px 8px" }}>1</span>
      </div>

      {adding && <NewScanModal onCancel={() => setAdding(false)} onScan={startScan} />}
    </div>
  );
}

function NewScanModal({ onCancel, onScan }) {
  const [f, setF] = useState({ ecosystem: "npm", name: "", version: "" });
  const valid = f.name.trim() && f.version.trim();
  return (
    <div style={s.modalScrim} onClick={onCancel}>
      <div style={{ ...s.modal, width: "min(460px,96vw)" }} onClick={(e) => e.stopPropagation()}>
        <div style={s.modalHead}><b>New on-demand scan</b><button style={s.modalX} onClick={onCancel}>×</button></div>
        <div style={{ padding: 20, display: "grid", gap: 14 }}>
          <div>
            <label style={s.fieldLbl}>Ecosystem</label>
            <select style={s.select} value={f.ecosystem} onChange={(e) => setF({ ...f, ecosystem: e.target.value })}>
              {ECOS.map((e) => <option key={e}>{e}</option>)}
            </select>
          </div>
          <div>
            <label style={s.fieldLbl}>Package name</label>
            <input style={{ ...s.formInput, width: "100%", fontFamily: C.mono }} placeholder="e.g. lodash"
              value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} />
          </div>
          <div>
            <label style={s.fieldLbl}>Version</label>
            <input style={{ ...s.formInput, width: "100%", fontFamily: C.mono }} placeholder="e.g. 4.17.21"
              value={f.version} onChange={(e) => setF({ ...f, version: e.target.value })} />
          </div>
          <div style={{ fontSize: 11, color: C.sub }}>Runs the full gate engine — vulnerability feeds, policy controls and content scan — and records the decision in the ledger.</div>
        </div>
        <div style={s.modalFoot}>
          <button style={s.btnGhost} onClick={onCancel}>Cancel</button>
          <button style={{ ...s.add, opacity: valid ? 1 : 0.5 }} disabled={!valid} onClick={() => onScan(f)}>Scan</button>
        </div>
      </div>
    </div>
  );
}

// ── AI Catalog (JFrog AI Catalog parity) ─────────────────────────────────────
// Registry: the org's approved-model allow-list (signed policy, gate-enforced via SEC-AIML-02).
// Discovery: live Hugging Face Hub search with risk scoring (format/license/gating/adoption).
// Detection: shadow-AI sweep of the Nexus repositories.
const fmtN = (n) => n >= 1e9 ? (n / 1e9).toFixed(1) + "B" : n >= 1e6 ? (n / 1e6).toFixed(1) + "M"
  : n >= 1e3 ? (n / 1e3).toFixed(1) + "k" : String(n ?? 0);
const riskTone = (r) => r === "High" ? C.block : r === "Medium" ? C.warn : C.allow;
const fmtBadge = (f) => f === "safetensors" ? { t: "✓ safetensors", c: C.allow }
  : f === "pickle" ? { t: "⚠ pickle", c: C.block }
  : f === "mixed" ? { t: "◐ mixed weights", c: C.warn }
  : f === "gguf" ? { t: "gguf", c: C.info } : f === "onnx" ? { t: "onnx", c: C.info }
  : f === "openvino (raw)" ? { t: "✓ openvino (raw)", c: C.allow }
  : f === "raw weights" ? { t: "✓ raw weights", c: C.allow }
  : f === "hdf5" ? { t: "hdf5", c: C.info } : f === "zip archive" ? { t: "zip", c: C.sub }
  : { t: f || "unknown", c: C.sub };

function AiCatalog({ initialTab = "registry", setTab: setNavTab }) {
  const [tab, setTab] = useState(initialTab);
  const [sel, setSel] = useState(null); // model id for detail view
  const [reg, setReg] = useState(null);
  const loadReg = () => api.aiRegistry().then(setReg).catch(() => setReg({ enforce: false, count: 0, models: [] }));
  useEffect(() => { loadReg(); }, []);
  useEffect(() => { setTab(initialTab); setSel(null); }, [initialTab]);

  if (sel) return <AiModelDetail id={sel} onBack={() => { setSel(null); loadReg(); }} onChanged={loadReg} />;

  const crumbLeaf = tab === "discovery" ? "Discover Models" : tab === "detection" ? "Shadow AI" : "Model Registry";
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "AI/ML" }, { label: crumbLeaf }]} />
      <div style={{ display: "flex", gap: 22, borderBottom: `1px solid ${C.line}`, marginBottom: 16 }}>
        {[["registry", `Approved Registry${reg ? ` (${reg.count})` : ""}`, "airegistry"],
          ["discovery", "Discover Models", "aidiscovery"],
          ["detection", "Shadow AI Detection", "aidetection"]].map(([k, l, navKey]) => (
          <button key={k} onClick={() => { setTab(k); setNavTab && setNavTab(navKey); }} style={{ ...s.jfTab, ...(tab === k ? s.jfTabOn : {}) }}>{l}</button>
        ))}
      </div>
      {tab === "registry" && <AiRegistry reg={reg} reload={loadReg} onPick={setSel} goDiscover={() => { setTab("discovery"); setNavTab && setNavTab("aidiscovery"); }} />}
      {tab === "discovery" && <AiDiscovery onPick={setSel} onAllowed={loadReg} />}
      {tab === "detection" && <AiDetection />}
    </div>
  );
}

// ── Evolution — GitHub tickets drive automated PRs via the evolution engine (PR-only) ──
const EVO_STATUS = {
  queued: { c: "#6e7479", t: "Queued" }, running: { c: "#1f7fd1", t: "Running" },
  tests: { c: "#1f7fd1", t: "Testing" }, "pr-open": { c: "#40be46", t: "PR open" },
  skipped: { c: "#6e7479", t: "No change" }, failed: { c: "#d63649", t: "Failed" },
};
function Evolution() {
  const [status, setStatus] = useState(null);
  const [tickets, setTickets] = useState(null);
  const [runs, setRuns] = useState([]);
  const [openRun, setOpenRun] = useState(null);
  const [busy, setBusy] = useState(null);
  const load = () => {
    api.evoStatus().then(setStatus).catch(() => setStatus({ enabled: false }));
    api.evoTickets().then(setTickets).catch(() => setTickets({ enabled: false, tickets: [] }));
    api.evoRuns().then((r) => setRuns(r.runs || [])).catch(() => {});
  };
  useEffect(() => { load(); const t = setInterval(() => api.evoRuns().then((r) => setRuns(r.runs || [])).catch(() => {}), 3000); return () => clearInterval(t); }, []);
  const evolve = async (n) => { setBusy(n); await api.evolve(n).catch(() => {}); setBusy(null); load(); };
  const activeTickets = new Set(tickets?.activeTickets || []);

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "Mutation" }]} />
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 14 }}>
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: 9, fontSize: 18, fontWeight: 700 }}>
            <Icon name="brain" size={20} color={C.accent} /> Mutation</div>
          <p style={{ color: C.sub, fontSize: 12.5, marginTop: 4, maxWidth: 720 }}>
            The <b>mutation</b> loop fixes bugs and broken code. GitHub tickets and tester comments drive
            automated fixes via the <b>/mutate</b> cycle. Triggering here dispatches the <b>same GitHub
            Actions workflow</b> a <code style={s.code}>mutation</code>-labelled issue fires. Every change is a
            <b> pull request for human review</b> — nothing auto-merges.
            <br/><span style={{ color: C.dim }}>(Forward-looking landscape research is the separate <b>Evolution</b>
            task — it only updates the backlog, never product code.)</span></p>
        </div>
        {status && <div style={{ textAlign: "right" }}>
          <Tag tone={status.enabled ? C.allow : C.sub}>{status.enabled ? "Enabled" : "Disabled"}</Tag>
          <div style={{ fontSize: 11, color: C.sub, marginTop: 4 }}>PR-only · human merges</div>
        </div>}
      </div>

      {/* status strip */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(4,1fr)", gap: 14, marginBottom: 16 }}>
        <MiniStat label="Target repo" value={status?.repo || "—"} mono />
        <MiniStat label="Trigger" value={status?.engineConfigured ? "workflow ready" : "not configured"} tone={status?.engineConfigured ? C.accentDim : C.warn} />
        <MiniStat label="Open tickets" value={tickets?.tickets?.length ?? "—"} />
        <MiniStat label="Active runs" value={status?.activeRuns ?? 0} tone={status?.activeRuns ? C.info : C.ink} />
      </div>

      {status && !status.enabled && (
        <Callout>Mutation is <b>disabled</b>. Set <code style={s.code}>EVOLUTION_ENABLED=true</code> and
          <code style={s.code}>EVOLUTION_REPO=owner/repo</code> on the API, and make sure <code style={s.code}>gh</code> is
          authenticated in the API container. The actual fixes run via the repo's
          <code style={s.code}>.github/workflows/mutation.yml</code> — this dashboard just lists tickets
          and dispatches that workflow. PR-only.</Callout>
      )}

      {status?.enabled && (<>
        <SubHead>Tickets labelled "{status.label}" — pick one to fix</SubHead>
        <div style={s.card}>
          <table style={s.table}><thead><tr>
            {["#", "Title", "Author", "Comments", "Updated", ""].map((c, i) => <th key={i} style={s.th}>{c}</th>)}
          </tr></thead><tbody>
            {(!tickets?.tickets || tickets.tickets.length === 0) && (
              <tr><td colSpan={6} style={{ padding: "40px 20px", textAlign: "center", color: C.sub }}>
                No open tickets labelled <b>{status.label}</b> in {status.repo || "the target repo"}. Open an issue and add that label to queue it.</td></tr>
            )}
            {(tickets?.tickets || []).map((t) => (
              <tr key={t.number} style={s.tr}>
                <td style={{ ...s.td, fontFamily: C.mono }}>#{t.number}</td>
                <td style={s.td}><a href={t.url} target="_blank" rel="noreferrer" style={s.linkDark}>{t.title}</a></td>
                <td style={{ ...s.td, color: C.sub }}>{t.author}</td>
                <td style={s.td}>{t.comments > 0 ? <span style={{ color: C.info, fontWeight: 600 }}>{t.comments} 💬</span> : "—"}</td>
                <td style={{ ...s.td, color: C.sub, fontSize: 11.5 }}>{new Date(t.updatedAt).toLocaleDateString()}</td>
                <td style={{ ...s.td, textAlign: "right" }}>
                  {activeTickets.has(t.number)
                    ? <span style={{ color: C.info, fontWeight: 600, fontSize: 11.5 }}>in progress…</span>
                    : <button style={s.add} disabled={busy === t.number || !status.engineConfigured} onClick={() => evolve(t.number)}>
                        {busy === t.number ? "Starting…" : "▸ Mutate"}</button>}</td>
              </tr>
            ))}
          </tbody></table>
        </div>
      </>)}

      <SubHead>Mutation runs</SubHead>
      <div style={s.card}>
        <table style={s.table}><thead><tr>
          {["Run", "Ticket", "Status", "Stage", "Tests", "PR", "Started"].map((c) => <th key={c} style={s.th}>{c}</th>)}
        </tr></thead><tbody>
          {runs.length === 0 && <tr><td colSpan={7} style={{ padding: "36px 20px", textAlign: "center", color: C.sub }}>No runs yet.</td></tr>}
          {runs.map((r) => {
            const st = EVO_STATUS[r.status] || { c: C.sub, t: r.status };
            return (
              <React.Fragment key={r.id}>
                <tr style={{ ...s.tr, cursor: "pointer" }} onClick={() => setOpenRun(openRun === r.id ? null : r.id)}>
                  <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11 }}>{r.id}</td>
                  <td style={s.td}>#{r.ticket} <span style={{ color: C.sub }}>{r.ticketTitle?.slice(0, 40)}</span></td>
                  <td style={s.td}><span style={{ color: st.c, fontWeight: 700, fontSize: 12 }}>● {st.t}</span></td>
                  <td style={{ ...s.td, color: C.sub, fontFamily: C.mono, fontSize: 11 }}>{r.stage}</td>
                  <td style={s.td}>{r.status === "pr-open" || r.status === "failed"
                    ? (r.testsPassed ? <span style={{ color: C.accentDim }}>✅</span> : <span style={{ color: C.warn }}>⚠</span>) : "—"}</td>
                  <td style={s.td}>{r.prUrl ? <a href={r.prUrl} target="_blank" rel="noreferrer" style={s.linkGreen}>PR ↗</a> : "—"}</td>
                  <td style={{ ...s.td, color: C.sub, fontSize: 11 }}>{new Date(r.startedAt).toLocaleTimeString()}</td>
                </tr>
                {openRun === r.id && (
                  <tr><td colSpan={7} style={{ padding: "12px 18px", background: C.bg2 }}>
                    <pre style={{ ...s.codeBlock, maxHeight: 300, whiteSpace: "pre-wrap" }}>{r.log || "(no log)"}</pre>
                  </td></tr>
                )}
              </React.Fragment>
            );
          })}
        </tbody></table>
      </div>
      <Callout><b>Safety:</b> PR-only — the engine writes to a branch and opens a pull request; a human reviews and merges.
        If tests don't pass, it opens a <b>draft</b> PR flagged for review. It never pushes to the default branch and never auto-merges.</Callout>
    </div>
  );
}

// ── AI/ML Overview — the section landing dashboard with live stats ──────────────
function AimlOverview({ setTab }) {
  const [reg, setReg] = useState(null);
  const [det, setDet] = useState(null);
  const [llm, setLlm] = useState(null);
  const [engine, setEngine] = useState(null);
  useEffect(() => {
    api.aiRegistry().then(setReg).catch(() => setReg({ count: 0, enforce: false, models: [] }));
    api.aiDetect().then(setDet).catch(() => setDet({ count: 0, shadow: 0, artifacts: [] }));
    api.llmRecords().then(setLlm).catch(() => setLlm({ stats: { total: 0, blocked: 0, dlpHits: {} } }));
    api.llmEngine().then(setEngine).catch(() => {});
    const t = setInterval(() => api.llmRecords().then(setLlm).catch(() => {}), 4000);
    return () => clearInterval(t);
  }, []);
  const st = llm?.stats || { total: 0, blocked: 0, dlpHits: {} };
  const dlpTotal = Object.values(st.dlpHits || {}).reduce((a, b) => a + b, 0);
  const shadow = det?.shadow || 0;
  const enforce = reg?.enforce;
  const pfState = engine?.privacyFilterState || (engine?.privacyFilterReady ? "ready" : "down");
  const pfLabel = { ready: "on-prem model ready", loading: "model loading…", unsupported: "Groq fallback active", error: "Groq fallback active", down: "Groq fallback" }[pfState] || "Groq fallback";

  const Tile = ({ icon, color, label, value, sub, onClick }) => (
    <div style={{ ...s.card, marginBottom: 0, padding: "16px 18px", cursor: onClick ? "pointer" : "default", borderTop: `3px solid ${color}` }} onClick={onClick}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, color: C.sub, fontSize: 12 }}><Icon name={icon} size={15} color={color} /> {label}</div>
      <div style={{ fontSize: 26, fontWeight: 700, marginTop: 6, color: C.ink }}>{value}</div>
      {sub && <div style={{ fontSize: 11.5, color: C.sub, marginTop: 3 }}>{sub}</div>}
    </div>
  );

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "AI/ML" }, { label: "Overview" }]} />
      <div style={{ fontSize: 18, fontWeight: 700, marginBottom: 4 }}>AI/ML Security Overview</div>
      <p style={{ color: C.sub, fontSize: 12.5, marginBottom: 16 }}>Live posture across model governance and LLM traffic — approved models, shadow AI in your repos, and every prompt crossing the gateway.</p>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(4,1fr)", gap: 14, marginBottom: 18 }}>
        <Tile icon="cube" color={C.accent} label="Approved models" value={reg?.count ?? "—"}
          sub={enforce ? "registry enforced" : "advisory mode"} onClick={() => setTab("airegistry")} />
        <Tile icon="alert" color={shadow > 0 ? C.block : C.accent} label="Shadow AI in repos" value={shadow}
          sub={shadow > 0 ? "unapproved models found" : "none detected"} onClick={() => setTab("aidetection")} />
        <Tile icon="gateway" color={C.info} label="LLM calls intercepted" value={fmtN(st.total)}
          sub={`${st.blocked} blocked`} onClick={() => setTab("llmgateway")} />
        <Tile icon="user" color={dlpTotal > 0 ? C.warn : C.accent} label="DLP detections" value={dlpTotal}
          sub="PII / cards / secrets / code" onClick={() => setTab("llmgateway")} />
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14, marginBottom: 16 }}>
        <div style={{ ...s.card, marginBottom: 0, padding: "16px 18px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
            <b style={{ fontSize: 13 }}>Model governance</b>
            <a style={s.linkGreen} onClick={() => setTab("airegistry")}>Open registry →</a>
          </div>
          <Row k="Approved on registry" v={reg?.count ?? 0} />
          <Row k="Registry enforcement" v={enforce ? "ON — non-approved blocked" : "OFF — advisory"} vc={enforce ? C.accentDim : C.sub} />
          <Row k="Shadow AI artifacts" v={shadow} vc={shadow > 0 ? C.block : C.accentDim} />
          <Row k="Weight verification" v="byte-level (magic + opcode)" last />
        </div>
        <div style={{ ...s.card, marginBottom: 0, padding: "16px 18px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
            <b style={{ fontSize: 13 }}>LLM Gateway &amp; DLP</b>
            <a style={s.linkGreen} onClick={() => setTab("llmgateway")}>Open gateway →</a>
          </div>
          <Row k="Calls intercepted" v={fmtN(st.total)} />
          <Row k="Blocked (data exfiltration)" v={st.blocked} vc={st.blocked > 0 ? C.block : C.accentDim} />
          <Row k="PII engine" v={pfLabel} vc={pfState === "ready" ? C.accentDim : C.sub} />
          <Row k="DLP categories" v="PII · cards · secrets · code" last />
        </div>
      </div>

      <BarChart title="DLP detections by category" data={Object.entries(DLP_META).map(([k, m]) => ({ label: m.label, value: (st.dlpHits || {})[k] || 0, color: m.c, icon: m.icon }))} />
    </div>
  );
}
function Row({ k, v, vc, last }) {
  return <div style={{ display: "flex", justifyContent: "space-between", padding: "8px 0", borderBottom: last ? "none" : `1px solid ${C.lineSoft}`, fontSize: 12.5 }}>
    <span style={{ color: C.sub }}>{k}</span><b style={{ color: vc || C.ink }}>{v}</b></div>;
}

function AiRegistry({ reg, reload, onPick, goDiscover }) {
  if (!reg) return <div style={s.kevEmpty}>Loading registry…</div>;
  const toggle = async (v) => { await api.aiEnforce(v).catch(() => {}); reload(); };
  return (
    <>
      <div style={{ ...s.card, padding: "14px 20px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <div>
          <b style={{ fontSize: 13 }}>Registry enforcement</b>
          <span style={{ ...s.kevBadge, marginLeft: 8 }}>SEC-AIML-02</span>
          <div style={{ color: C.sub, fontSize: 12, marginTop: 3 }}>
            {reg.enforce
              ? "ON — the gate blocks any HuggingFace model that is not on this approved list."
              : "OFF — models pass on format/hash controls only. Approve models below, then switch on."}
          </div>
        </div>
        <Switch on={reg.enforce} onChange={toggle} />
      </div>
      <div style={s.card}>
        <table style={s.table}><thead><tr>
          {["Model", "License", "Risk", "Weights", "Approved by", "Approved", ""].map((c, i) => <th key={i} style={s.th}>{c}</th>)}
        </tr></thead><tbody>
          {reg.models.length === 0 && (
            <tr><td colSpan={7} style={{ padding: "50px 20px", textAlign: "center" }}>
              <div style={{ fontSize: 40, marginBottom: 10, opacity: 0.55 }}>✦</div>
              <div style={{ fontSize: 15, fontWeight: 600 }}>No approved models yet</div>
              <div style={{ color: C.sub, fontSize: 12.5, marginTop: 5 }}>
                Find models in <a style={s.linkGreen} onClick={goDiscover}>Discovery</a> and allow them into your organization.</div>
            </td></tr>
          )}
          {reg.models.map((m) => {
            const live = m.live;
            const f = fmtBadge(live?.weightFormat);
            return (
              <tr key={m.id} style={s.tr}>
                <td style={s.td}><a style={s.linkDark} onClick={() => onPick(m.id)}>{m.id}</a>
                  {live?.task && <div style={{ color: C.sub, fontSize: 11, marginTop: 2 }}>{live.task}</div>}</td>
                <td style={s.td}>
                  <span style={{ fontFamily: C.mono, fontSize: 11.5 }}>{live?.license || m.approvedLicense || "—"}</span>
                  {m.licenseDrift && <div style={{ color: C.warn, fontSize: 10.5, fontWeight: 700 }} title={`Approved as ${m.approvedLicense}`}>⚠ license changed since approval</div>}</td>
                <td style={s.td}>{live
                  ? <span style={{ color: riskTone(live.risk), fontWeight: 700, fontSize: 12 }}>● {live.risk}</span>
                  : <span style={{ color: C.dim }}>—</span>}</td>
                <td style={s.td}><span style={{ color: f.c, fontSize: 11.5, fontWeight: 600 }}>{f.t}</span></td>
                <td style={{ ...s.td, color: C.sub }}>{m.approvedBy || "—"}</td>
                <td style={{ ...s.td, color: C.sub, fontSize: 11.5, whiteSpace: "nowrap" }}>
                  {new Date(m.approvedAt).toLocaleDateString(undefined, { day: "2-digit", month: "short", year: "numeric" })}</td>
                <td style={{ ...s.td, textAlign: "right" }}>
                  <button style={s.remove} onClick={async () => { await api.aiDisallow(m.id).catch(() => {}); reload(); }}>Remove</button></td>
              </tr>
            );
          })}
        </tbody></table>
      </div>
    </>
  );
}

function AiDiscovery({ onPick, onAllowed }) {
  const [q, setQ] = useState("");
  const [sort, setSort] = useState("downloads");
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    let on = true;
    setLoading(true);
    const t = setTimeout(() => {
      api.aiDiscover(q, sort).then((d) => { if (on) { setData(d); setLoading(false); } })
        .catch(() => { if (on) { setData({ models: [], error: "unreachable" }); setLoading(false); } });
    }, q ? 350 : 0);
    return () => { on = false; clearTimeout(t); };
  }, [q, sort]);

  // Approval always goes through the model detail view, where byte-level weight
  // verification must complete first — no model is allowed on an extension guess.
  const review = (m, e) => { e.stopPropagation(); onPick(m.id); };

  return (
    <>
      <div style={{ display: "flex", gap: 10, marginBottom: 16 }}>
        <div style={{ ...s.kevSearch, maxWidth: 520 }}>
          <span style={{ color: C.sub, fontSize: 13 }}>⌕</span>
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search the Hugging Face Hub — e.g. llama, whisper, bge-small…"
            style={{ border: "none", outline: "none", background: "transparent", fontSize: 12.5, fontFamily: C.sans, width: "100%" }} />
        </div>
        <select style={s.select} value={sort} onChange={(e) => setSort(e.target.value)}>
          <option value="downloads">Most downloaded</option>
          <option value="likes">Most liked</option>
          <option value="updated">Recently updated</option>
        </select>
      </div>
      {loading && <div style={s.kevEmpty}>Querying the Hugging Face Hub…</div>}
      {!loading && data?.error && <div style={s.kevEmpty}>Hub unreachable: {data.error}</div>}
      {!loading && !data?.error && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(330px, 1fr))", gap: 14 }}>
          {(data?.models || []).map((m) => {
            const f = fmtBadge(m.weightFormat);
            return (
              <div key={m.id} style={{ ...s.card, marginBottom: 0, padding: "14px 16px", cursor: "pointer",
                borderLeft: `3px solid ${riskTone(m.risk)}` }} onClick={() => onPick(m.id)}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 8 }}>
                  <div style={{ minWidth: 0 }}>
                    <div style={{ fontSize: 11, color: C.sub }}>{m.author}</div>
                    <div style={{ fontWeight: 700, fontSize: 13.5, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}
                      title={m.id}>{m.id.split("/").pop()}</div>
                  </div>
                  {m.allowed
                    ? <span style={{ color: C.accentDim, fontWeight: 700, fontSize: 11.5, whiteSpace: "nowrap" }}>✓ Approved</span>
                    : <button style={{ ...s.miniBtn, color: C.accentDim, borderColor: C.accent }} onClick={(e) => review(m, e)}
                        title="Opens the model card — approval requires byte-level weight verification">Review &amp; allow</button>}
                </div>
                <div style={{ display: "flex", gap: 6, flexWrap: "wrap", margin: "10px 0 8px" }}>
                  {m.task && <Tag tone={C.info}>{m.task}</Tag>}
                  <Tag tone={m.license ? C.sub : C.warn}>{m.license || "no license"}</Tag>
                  <span style={{ color: f.c, fontSize: 11, fontWeight: 700, alignSelf: "center" }}>{f.t}</span>
                </div>
                <div style={{ display: "flex", gap: 14, color: C.sub, fontSize: 11.5 }}>
                  <span title="Downloads (30d)">⤓ {fmtN(m.downloads)}</span>
                  <span title="Likes">♥ {fmtN(m.likes)}</span>
                  <span style={{ color: riskTone(m.risk), fontWeight: 700 }}>● {m.risk} risk</span>
                  {m.updated && <span>{new Date(m.updated).toLocaleDateString(undefined, { month: "short", year: "numeric" })}</span>}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </>
  );
}

function AiModelDetail({ id, onBack, onChanged }) {
  const [d, setD] = useState(null);
  const [err, setErr] = useState(null);
  const [job, setJob] = useState(null);           // background verification job snapshot
  const [evicted, setEvicted] = useState(null);   // bytes freed after eviction
  const load = () => api.aiModel(id).then((r) => r.error ? setErr(r.error) : setD(r)).catch(() => setErr("Hub unreachable"));
  useEffect(() => { load(); }, [id]);
  // 100%-accuracy pass runs as a BACKGROUND JOB so the UI never blocks on multi-GB downloads:
  // start once, then poll live per-file progress (head → download% → scan → verdict).
  useEffect(() => {
    let on = true, timer;
    setJob(null); setEvicted(null);
    api.aiVerifyStart(id).then(() => {
      const poll = () => api.aiVerifyStatus(id).then((j) => {
        if (!on) return;
        setJob(j);
        if (j.status === "running") timer = setTimeout(poll, 1200);
      }).catch(() => {});
      poll();
    }).catch(() => {});
    return () => { on = false; clearTimeout(timer); };
  }, [id]);
  const verifying = !job || job.status === "running";
  const verdictFor = (name) => job?.files?.find((v) => v.name === name)?.verdict;
  const progFor = (name) => job?.files?.find((v) => v.name === name);
  const malicious = job?.summary?.malicious || [];
  const cachedMB = job?.cachedBytes ? Math.round(job.cachedBytes / (1024 * 1024)) : 0;
  const evict = async () => { const r = await api.aiVerifyEvict(id).catch(() => null); if (r) setEvicted(Math.round((r.freedBytes || 0) / (1024 * 1024))); };
  const [consumed, setConsumed] = useState(null);
  const consume = async () => { const r = await api.aiConsume(id).catch(() => null); if (r?.consumed) setConsumed(r); };

  const askAi = () => window.dispatchEvent(new CustomEvent("pkgfw-askai", {
    detail: `Assess the AI model "${id}" for enterprise use: weight format risk (${d?.model?.weightFormat}), license "${d?.model?.license || "none"}", and whether our policy controls (SEC-AIML-01/02) would allow it. Recommend approve or reject for the registry.` }));

  if (err) return <div style={{ animation: "fwfade .2s ease" }}>
    <Crumb trail={[{ label: "AI/ML" }, { label: "AI Catalog", onClick: onBack }, { label: id }]} />
    <div style={s.kevEmpty}>{err}</div></div>;
  if (!d) return <div style={s.kevEmpty}>Loading model card…</div>;
  const m = d.model;
  const f = fmtBadge(m.weightFormat);
  const weightFiles = d.files.filter((x) => x.format !== "config" && x.format !== "other");
  const toggleAllow = async () => {
    if (m.allowed) await api.aiDisallow(m.id).catch(() => {});
    else await api.aiAllow(m.id, m.license, "").catch(() => {});
    onChanged(); load();
  };

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "AI/ML" }, { label: "AI Catalog", onClick: onBack }, { label: m.id }]} />
      <div style={{ display: "grid", gridTemplateColumns: "340px 1fr", gap: 20, alignItems: "start" }}>
        {/* left card */}
        <div style={{ ...s.card, padding: "20px 20px 16px" }}>
          <div style={{ display: "flex", gap: 12, alignItems: "center", marginBottom: 14 }}>
            <div style={{ width: 44, height: 44, borderRadius: 10, background: C.brand, color: "#fff",
              display: "grid", placeItems: "center", fontSize: 18, fontWeight: 700 }}>{(m.author || m.id)[0].toUpperCase()}</div>
            <div style={{ minWidth: 0 }}>
              <div style={{ fontWeight: 700, fontSize: 14, overflowWrap: "anywhere" }}>{m.id.split("/").pop()}</div>
              <div style={{ color: C.sub, fontSize: 12 }}>{m.author}</div>
            </div>
          </div>
          <KV k="Risk" v={<span style={{ color: riskTone(m.risk), fontWeight: 700 }}>● {m.risk}</span>} />
          <KV k="Weights" v={<span style={{ color: f.c, fontWeight: 600 }}>{f.t}</span>} />
          <KV k="License" v={m.license || "none declared"} />
          <KV k="Task" v={m.task || "—"} />
          <KV k="Library" v={m.library || "—"} />
          <KV k="Downloads" v={fmtN(m.downloads)} />
          <KV k="Likes" v={fmtN(m.likes)} />
          <KV k="Gated" v={m.gated ? "Yes — access terms apply" : "No"} />
          <KV k="Updated" v={m.updated ? new Date(m.updated).toLocaleDateString(undefined, { day: "2-digit", month: "short", year: "numeric" }) : "—"} />
          <div style={{ display: "flex", flexDirection: "column", gap: 8, marginTop: 16 }}>
            {m.allowed
              ? <button style={s.remove} onClick={toggleAllow}>Remove from registry</button>
              : malicious.length > 0
                ? <div style={{ border: `1px solid ${C.block}`, color: C.block, borderRadius: 8, padding: "9px 12px", fontSize: 12, fontWeight: 600, textAlign: "center" }}>
                    ⛔ Approval blocked — dangerous pickle opcodes confirmed</div>
                : <button style={{ ...s.add, opacity: verifying ? 0.5 : 1 }} disabled={verifying} onClick={toggleAllow}
                    title={verifying ? "Byte-level weight verification must complete before approval" : ""}>
                    {verifying ? "Verifying weights…" : "Allow — add to registry"}</button>}
            {/* Consume: only available once approved — pulls it into the approved repo developers use. */}
            {m.allowed && (consumed
              ? <div style={{ border: `1px solid ${C.accent}`, background: "rgba(64,190,70,.08)", color: C.accentDim, borderRadius: 8, padding: "9px 12px", fontSize: 11.5, fontWeight: 600, textAlign: "center" }}>
                  ✓ Pulled into <code style={{ fontFamily: C.mono }}>{consumed.repo}</code> as <code style={{ fontFamily: C.mono }}>{consumed.file}</code>
                  {consumed.quarantinedPickle && <div style={{ color: C.warn, marginTop: 4 }}>⚠ pickle file quarantined — promoted the {consumed.format} equivalent only</div>}
                  <div style={{ color: C.sub, fontWeight: 400, marginTop: 3 }}>now in Shadow AI Detection (Approved)</div></div>
              : <button style={s.add} onClick={consume}>⤓ Pull into repository</button>)}
            <button style={s.btnGhost} onClick={askAi}>✦ Ask AI about this model</button>
            <a href={`https://huggingface.co/${m.id}`} target="_blank" rel="noreferrer"
              style={{ ...s.btnGhost, textAlign: "center", textDecoration: "none", color: C.ink }}>Open on Hugging Face ↗</a>
          </div>
        </div>

        {/* right: risk + files + tags */}
        <div>
          <div style={{ ...s.card, padding: "16px 20px" }}>
            <b style={{ fontSize: 13 }}>Risk assessment</b>
            <div style={{ marginTop: 10, display: "flex", flexDirection: "column", gap: 7 }}>
              {m.riskReasons.map((r, i) => (
                <div key={i} style={{ display: "flex", gap: 9, alignItems: "flex-start", fontSize: 12.5 }}>
                  <span style={{ width: 7, height: 7, borderRadius: "50%", background: riskTone(m.risk), marginTop: 5, flexShrink: 0 }} />
                  {r}</div>
              ))}
              {malicious.map((h, i) => (
                <div key={`mal${i}`} style={{ display: "flex", gap: 9, alignItems: "flex-start", fontSize: 12.5, color: C.block, fontWeight: 600 }}>
                  <span style={{ width: 7, height: 7, borderRadius: "50%", background: C.block, marginTop: 5, flexShrink: 0 }} />
                  {h}</div>
              ))}
            </div>
            {job?.files?.some((v) => v.verdict?.format === "pickle") && (
              <div style={{ marginTop: 12, padding: "10px 12px", background: "rgba(64,190,70,.07)", border: `1px solid rgba(64,190,70,.25)`, borderRadius: 8, fontSize: 11.5 }}>
                <b style={{ color: C.accentDim }}>Safe import is possible.</b> When this model is pulled through the firewall, the gate can <b>quarantine the pickle file(s) and promote only the safetensors/ONNX equivalents</b> — most repos (including this one) ship both. If only pickle exists, the file is opcode-scanned and blocked on any dangerous import. You don't have to reject the whole model.</div>
            )}
            <div style={{ marginTop: 12, paddingTop: 10, borderTop: `1px solid ${C.lineSoft}`, fontSize: 11.5,
              color: verifying ? C.info : job ? (job.summary.unconfirmed > 0 ? C.warn : C.accentDim) : C.sub }}>
              {verifying ? <span style={{ animation: "fwpulse 1.2s infinite" }}>● Background verification running — {job ? `${job.summary.done}/${job.summary.total} files done` : "starting"}. Multi-GB weights stream to cache and are opcode-scanned; you can navigate away and come back.</span>
                : job ? (job.summary.unconfirmed > 0
                    ? `● Verification: ${job.summary.total - job.summary.unconfirmed}/${job.summary.total} files confirmed, ${job.summary.unconfirmed} inconclusive — review before approval`
                    : `● Verification complete: all ${job.summary.total} weight files confirmed byte-level (${job.summary.pickleConfirmed} pickle)`)
                : "● Verification unavailable — Hub unreachable"}
            </div>
            {!verifying && job && (
              <div style={{ marginTop: 10, padding: "9px 12px", borderRadius: 8, fontSize: 11.5,
                background: m.allowed ? "rgba(64,190,70,.08)" : "rgba(31,127,209,.06)",
                border: `1px solid ${m.allowed ? "rgba(64,190,70,.25)" : C.line}` }}>
                {m.allowed
                  ? <><b style={{ color: C.accentDim }}>● Approved &amp; ready to consume.</b> This model is on the Registry — when pulled through the firewall it is promoted out of quarantine to the approved repo developers consume from.</>
                  : <><b>● Verified, awaiting approval.</b> Scanned and held — not yet consumable. Click <b>Allow — add to registry</b> to promote it; until then it stays in quarantine.</>}
              </div>
            )}
            {!verifying && (cachedMB > 0 || evicted != null) && (
              <div style={{ marginTop: 8, fontSize: 11, display: "flex", justifyContent: "space-between", alignItems: "center", color: C.sub }}>
                <span>{evicted != null ? `✓ Cache evicted — ${evicted} MB freed` : `${cachedMB} MB of weights cached for scanning`}</span>
                {evicted == null && cachedMB > 0 && <button style={s.linkBtn} onClick={evict}>Delete cached files</button>}
              </div>
            )}
          </div>
          <div style={{ ...s.card, padding: "16px 20px" }}>
            <b style={{ fontSize: 13 }}>Weight files ({weightFiles.length})</b>
            <div style={{ marginTop: 8 }}>
              {weightFiles.length === 0 && <div style={{ color: C.sub, fontSize: 12.5 }}>No recognizable weight files in this repository.</div>}
              {weightFiles.slice(0, 24).map((x, i) => {
                const v = verdictFor(x.name);
                const p = progFor(x.name);
                const fb = fmtBadge(v ? v.format : x.format);
                // live status text: download %, scanning, or final verdict method
                let status, sc = C.dim;
                if (v) { status = v.confirmed ? `confirmed · ${v.method}` : "inconclusive"; sc = v.confirmed ? C.accentDim : C.warn; }
                else if (p?.stage === "downloading") { status = `downloading ${p.percent}%${p.totalBytes ? ` · ${Math.round(p.totalBytes / 1048576)}MB` : ""}`; sc = C.info; }
                else if (p?.stage === "scanning") { status = "scanning…"; sc = C.info; }
                else if (p?.stage === "head") { status = "checking signature…"; sc = C.info; }
                else if (verifying) { status = "queued…"; sc = C.dim; }
                else { status = "by extension"; }
                return <div key={i} style={{ padding: "6px 0", borderBottom: `1px solid ${C.lineSoft}`, fontSize: 12 }}>
                  <div style={{ display: "flex", justifyContent: "space-between" }}>
                    <span style={{ fontFamily: C.mono, overflowWrap: "anywhere" }}>{x.name}</span>
                    <span style={{ whiteSpace: "nowrap", marginLeft: 12, textAlign: "right" }} title={v?.detail || ""}>
                      <span style={{ color: fb.c, fontWeight: 600 }}>{fb.t}</span>
                      <span style={{ fontSize: 10, marginLeft: 6, color: sc }}>{status}</span></span>
                  </div>
                  {p?.stage === "downloading" && p.percent > 0 && (
                    <div style={{ height: 3, borderRadius: 2, background: C.lineSoft, marginTop: 4, overflow: "hidden" }}>
                      <div style={{ width: `${p.percent}%`, height: "100%", background: C.info }} /></div>
                  )}
                </div>;
              })}
              {weightFiles.length > 24 && <div style={{ color: C.sub, fontSize: 11.5, marginTop: 6 }}>+ {weightFiles.length - 24} more files</div>}
            </div>
          </div>
          <div style={{ ...s.card, padding: "16px 20px" }}>
            <b style={{ fontSize: 13 }}>Tags</b>
            <div style={{ display: "flex", gap: 6, flexWrap: "wrap", marginTop: 10 }}>
              {d.tags.slice(0, 28).map((t) => <Tag key={t} tone={C.sub}>{t}</Tag>)}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function AiDetection() {
  const [data, setData] = useState(null);
  const [running, setRunning] = useState(false);
  const run = () => {
    setRunning(true);
    api.aiDetect().then(setData).catch(() => setData({ count: 0, shadow: 0, artifacts: [] })).finally(() => setRunning(false));
  };
  useEffect(() => { run(); }, []);
  const arts = data?.artifacts || [];
  const shadow = arts.filter((a) => a.status === "Shadow AI");
  const approved = arts.filter((a) => a.status !== "Shadow AI");

  const tableFor = (rows) => (
    <div style={s.card}>
      <table style={s.table}><thead><tr>
        {["Repository", "Component", "Version", "File", "Format"].map((c) => <th key={c} style={s.th}>{c}</th>)}
      </tr></thead><tbody>
        {rows.map((a, i) => {
          const fb = fmtBadge(a.format);
          return (
            <tr key={i} style={s.tr}>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{a.repo}</td>
              <td style={s.td}><b>{a.name}</b></td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{a.version}</td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11, color: C.sub }}>{a.fileName || "—"}</td>
              <td style={s.td}><span style={{ color: fb.c, fontSize: 11.5, fontWeight: 600 }}>{fb.t}</span></td>
            </tr>
          );
        })}
      </tbody></table>
    </div>
  );

  return (
    <>
      {/* What Detection is — so it's never a mystery empty screen */}
      <Callout>
        <b>Detection finds models already inside your repositories — "shadow AI".</b> It sweeps every Nexus
        repository for model weight files (<code style={s.code}>.safetensors .bin .pt .gguf .onnx .ckpt</code>…),
        then cross-references each against your approved <b>Registry</b>. Anything not approved is flagged
        as shadow AI so you can review or remove it. <b>Re-run sweep</b> re-lists the repositories now (use it
        after pushing a model, or after approving one — its status flips from Shadow AI to Approved).
      </Callout>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", margin: "14px 0 12px" }}>
        <div style={{ display: "flex", gap: 16, alignItems: "center" }}>
          <b style={{ fontSize: 14 }}>{data ? `${arts.length} model artifacts in repositories` : "Sweeping…"}</b>
          {shadow.length > 0 && <span style={{ display: "flex", alignItems: "center", gap: 5, color: C.block, fontWeight: 700, fontSize: 12.5 }}><Icon name="alert" size={14} color={C.block} /> {shadow.length} shadow AI</span>}
          {arts.length > 0 && shadow.length === 0 && <span style={{ display: "flex", alignItems: "center", gap: 5, color: C.accentDim, fontWeight: 700, fontSize: 12.5 }}><Icon name="check" size={14} color={C.accentDim} /> all approved</span>}
        </div>
        <div style={{ display: "flex", gap: 8 }}>
          <button style={{ ...s.btnGhost, display: "flex", alignItems: "center", gap: 6 }}
            onClick={async () => { await api.aiConsumeShadow("evil-corp/leaked-llm").catch(() => {}); run(); }}
            title="Simulate an unapproved model landing in a repository">
            <Icon name="alert" size={14} color={C.block} /> Simulate shadow AI</button>
          <button style={{ ...s.btnGhost, display: "flex", alignItems: "center", gap: 6 }} onClick={run} disabled={running}>
            <Icon name="scan" size={14} /> {running ? "Sweeping…" : "Re-run sweep"}</button>
        </div>
      </div>

      {data && arts.length === 0 && (
        <div style={{ ...s.card, padding: "50px 20px", textAlign: "center" }}>
          <Icon name="search" size={40} color={C.dim} style={{ marginBottom: 10 }} />
          <div style={{ fontSize: 15, fontWeight: 600 }}>No model artifacts in your repositories</div>
          <div style={{ color: C.sub, fontSize: 12.5, marginTop: 5, maxWidth: 460, margin: "5px auto 0" }}>
            Clean — no shadow AI. Your Nexus repos currently hold no model weight files. Push a model (or run it
            through the gate) and it appears here; if it's not on the Registry it's flagged shadow AI.</div>
        </div>
      )}

      {shadow.length > 0 && (<>
        <div style={{ display: "flex", alignItems: "center", gap: 7, fontSize: 12.5, fontWeight: 700, color: C.block, margin: "4px 0 8px" }}>
          <Icon name="alert" size={15} color={C.block} /> Shadow AI — in repositories but NOT approved ({shadow.length})</div>
        {tableFor(shadow)}
      </>)}
      {approved.length > 0 && (<>
        <div style={{ display: "flex", alignItems: "center", gap: 7, fontSize: 12.5, fontWeight: 700, color: C.accentDim, margin: "16px 0 8px" }}>
          <Icon name="check" size={15} color={C.accentDim} /> Approved — on the Registry ({approved.length})</div>
        {tableFor(approved)}
      </>)}
    </>
  );
}

// Xray-style Reports: Vulnerabilities / Violations / Legal (licenses) / Operational Risk,
// generated server-side from the decision ledger (+ live registry enrichment), CSV-exportable.
const REPORT_TYPES = [
  { key: "vulnerabilities", label: "Vulnerabilities", icon: "⚠", desc: "Every vulnerability the gate has seen across evaluated packages — severity, CVSS, EPSS, KEV, fix version." },
  { key: "violations", label: "Violations", icon: "⊘", desc: "Policy violations (Block / Quarantine) with triggered controls and waiver status." },
  { key: "licenses", label: "Legal · Licenses", icon: "§", desc: "Due-diligence license report for evaluated packages — declared license, prohibited matches, unknowns." },
  { key: "operational", label: "Operational Risk", icon: "⏲", desc: "EOL / deprecated, version age, newer versions, project health for evaluated packages." },
];
function Reports() {
  const [type, setType] = useState(null);
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(false);
  const run = async (k) => {
    setType(k); setLoading(true); setData(null);
    try { setData(await api.getReport(k)); } catch { setData({ rows: [], error: true }); }
    setLoading(false);
  };
  const rows = data?.rows || [];
  const cols = rows.length > 0 ? Object.keys(rows[0]) : [];
  const fmt = (v) => v === null || v === undefined ? "—"
    : typeof v === "boolean" ? (v ? "Yes" : "No")
    : typeof v === "number" ? (Number.isInteger(v) ? v : v.toFixed(3))
    : String(v).match(/^\d{4}-\d{2}-\d{2}T/) ? new Date(v).toLocaleString() : String(v);
  const tone = (c, v) => /severity|risk|decision/i.test(c) ? (OPR_TONE[v] || sevTone?.(v) || C.ink)
    : c === "prohibited" && v === true ? C.block : C.ink;
  return (
    <Card title="Reports" desc="Aggregated views over the signed decision ledger — the same four report types JFrog Xray ships (Vulnerabilities, Violations, Legal, Operational), exportable as CSV.">
      <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 14, marginBottom: 20 }}>
        {REPORT_TYPES.map((r) => (
          <button key={r.key} onClick={() => run(r.key)}
            style={{ textAlign: "left", cursor: "pointer", background: type === r.key ? "rgba(64,190,70,.07)" : C.surface,
              border: `1px solid ${type === r.key ? C.accent : C.line}`, borderRadius: 10, padding: "14px 16px" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8, fontWeight: 700, fontSize: 13, marginBottom: 6 }}>
              <span style={{ color: type === r.key ? C.accent : C.sub }}>{r.icon}</span>{r.label}</div>
            <div style={{ fontSize: 11.5, color: C.sub, lineHeight: 1.45 }}>{r.desc}</div>
          </button>
        ))}
      </div>
      {!type && <EmptyState title="Pick a report type" sub="Reports are generated live from the decision ledger — no scheduling required at this scale." />}
      {loading && <div style={{ padding: 30, textAlign: "center", color: C.sub, fontSize: 13 }}>Generating report…</div>}
      {type && !loading && data && (
        <div style={s.card}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "12px 18px", borderBottom: `1px solid ${C.lineSoft}` }}>
            <span style={{ fontWeight: 600, fontSize: 13 }}>
              {REPORT_TYPES.find((r) => r.key === type)?.label} · {rows.length} rows
              {data.generatedAt && <span style={{ color: C.sub, fontWeight: 400, fontSize: 11.5 }}> — generated {new Date(data.generatedAt).toLocaleString()}</span>}
            </span>
            <a href={api.reportCsvUrl(type)} download
              style={{ color: "#fff", background: C.accent, borderRadius: 6, padding: "6px 14px", fontSize: 12, fontWeight: 600, textDecoration: "none" }}>↓ Download CSV</a>
          </div>
          {rows.length === 0
            ? <EmptyState title="No rows" sub="Run some evaluations through the gate first — reports are derived from the decision ledger." />
            : <div style={{ overflowX: "auto" }}>
                <Table cols={cols}>
                  {rows.slice(0, 300).map((r, i) => (
                    <tr key={i} style={s.tr}>
                      {cols.map((c) => (
                        <td key={c} style={{ ...s.td, fontSize: 11.5, color: tone(c, r[c]),
                          fontFamily: /package|version|vulnerability|resource|cves|controls/i.test(c) ? C.mono : C.sans,
                          maxWidth: 320, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}
                          title={r[c] != null ? String(r[c]) : ""}>{fmt(r[c])}</td>
                      ))}
                    </tr>
                  ))}
                </Table>
              </div>}
        </div>
      )}
    </Card>
  );
}

// A single vulnerability finding with an expandable JFrog-style detail panel.
function FindingRow({ f }) {
  const [open, setOpen] = useState(false);
  const refs = f.references || [];
  const byType = (t) => refs.filter((r) => r.type === t);
  const refGroups = [["Advisory", byType("Advisory")], ["Exploit (PoC)", byType("Exploit")],
    ["Patch", byType("Patch")], ["Report", byType("Report")],
    ["Other", refs.filter((r) => !["Advisory", "Exploit", "Patch", "Report"].includes(r.type))]];
  return (
    <div style={{ borderBottom: `1px solid ${C.lineSoft}`, padding: "6px 0" }}>
      <div onClick={() => setOpen(!open)} style={{ display: "flex", alignItems: "center", gap: 10, cursor: "pointer" }}>
        <span style={{ color: C.sub, fontSize: 10 }}>{open ? "▾" : "▸"}</span>
        <span style={{ fontFamily: C.mono, fontSize: 11.5, minWidth: 190 }}>{f.id}</span>
        <span style={{ color: sevTone(f.severity), fontWeight: 600, fontSize: 11.5, minWidth: 64 }}>{f.severity}</span>
        {f.reachability && <ReachBadge r={f.reachability} detail={f.reachabilityDetail} />}
        {f.fixedVersion && <span style={{ fontSize: 11 }}>
          <span style={{ color: C.sub }}>fix: </span>
          <span style={{ fontFamily: C.mono, color: C.allow, fontWeight: 600 }}>{f.fixedVersion}</span></span>}
      </div>
      {open && (
        <div style={{ padding: "10px 0 4px 20px", fontSize: 11.5 }}>
          {f.summary && <p style={{ margin: "0 0 8px", color: C.ink, lineHeight: 1.5, maxWidth: 720 }}>{f.summary}</p>}
          <table style={{ borderCollapse: "collapse", marginBottom: 8 }}><tbody>
            {f.aliases?.length > 0 && <DetailRow k="Aliases" v={f.aliases.join(", ")} mono />}
            {f.cvssVector && <DetailRow k="CVSS vector" v={f.cvssVector} mono />}
            {f.cwes?.length > 0 && <DetailRow k="CWE" v={f.cwes.join(", ")} mono />}
            {f.publishedAt && <DetailRow k="Published" v={new Date(f.publishedAt).toLocaleDateString()} />}
            {f.fixedVersion && <DetailRow k="Fixed in" v={f.fixedVersion} mono />}
            {f.reachabilityDetail && <DetailRow k="Reachability" v={f.reachabilityDetail} />}
          </tbody></table>
          {refs.length > 0 && (
            <div>
              {refGroups.filter(([, g]) => g.length > 0).map(([label, g]) => (
                <div key={label} style={{ marginBottom: 6 }}>
                  <div style={{ fontSize: 10, color: C.sub, textTransform: "uppercase", letterSpacing: 0.5, marginBottom: 2 }}>{label}</div>
                  {g.map((r, i) => (
                    <div key={i}><a href={r.url} target="_blank" rel="noreferrer"
                      style={{ color: C.accent, fontSize: 11, wordBreak: "break-all" }}>{r.url}</a></div>
                  ))}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
function DetailRow({ k, v, mono }) {
  return (
    <tr>
      <td style={{ padding: "2px 14px 2px 0", color: C.sub, verticalAlign: "top", whiteSpace: "nowrap" }}>{k}</td>
      <td style={{ padding: "2px 0", fontFamily: mono ? C.mono : C.sans, fontSize: 11 }}>{v}</td>
    </tr>
  );
}

function LedgerRow({ e }) {
  const [open, setOpen] = useState(false);
  const cov = e.coverage;
  const incomplete = cov && cov.allRequiredConclusive === false;
  return (
    <>
      <tr style={{ ...s.tr, cursor: "pointer" }} onClick={() => setOpen(!open)}>
        <td style={{ ...s.td, fontFamily: C.mono }}>
          <span style={{ color: C.sub, marginRight: 6 }}>{open ? "\u25be" : "\u25b8"}</span>
          {e.package.ecosystem}:{e.package.name}@{e.package.version}</td>
        <td style={s.td}><Decision d={e.decision} /></td>
        <td style={{ ...s.td, color: C.sub, fontFamily: C.mono }}>{e.componentsEvaluated ?? 1}</td>
        <td style={s.td}>
          {cov
            ? <span style={{ color: incomplete ? C.warn : C.allow, fontSize: 11, fontFamily: C.mono }}>
                {incomplete ? "INCOMPLETE" : "complete"}</span>
            : <span style={{ color: C.sub, fontSize: 11 }}>\u2014</span>}</td>
        <td style={{ ...s.td, color: C.sub, fontSize: 11, whiteSpace: "nowrap" }}>
          {new Date(e.timestamp).toLocaleString()}</td>
      </tr>
      {open && (
        <tr>
          <td colSpan={5} style={{ padding: 0, background: C.bg2, borderBottom: `1px solid ${C.line}` }}>
            <div style={{ padding: "14px 18px 18px 40px" }}>
              {/* Rationale leads \u2014 every decision carries an explanation (AI when keyed, deterministic otherwise). */}
              <Drawer label="Why this decision">
                {e.researchRationale
                  ? <pre style={s.rationale}>{e.researchRationale}</pre>
                  : <p style={{ margin: 0, color: C.sub, fontSize: 12, lineHeight: 1.5 }}>
                      No written rationale on this entry. Enable <b>SEC-AUD-03</b> (Intelligence sources tab) to
                      record an explanation per decision; set <code style={s.code}>ANTHROPIC_API_KEY</code> for AI-written prose.</p>}
              </Drawer>
              <Drawer label="Triggered controls">
                <span style={{ fontFamily: C.mono, fontSize: 11 }}>
                  {(e.triggeredRules || []).join("   \u00b7   ") || "none"}</span>
              </Drawer>
              {(e.findings || []).length > 0 && (
                <Drawer label={`Findings (${e.findings.length})`}>
                  {e.findings.map((f, i) => <FindingRow key={i} f={f} />)}
                </Drawer>
              )}
              {cov && (
                <Drawer label="Source coverage">
                  <table style={{ width: "100%", borderCollapse: "collapse" }}>
                    <tbody>
                      {cov.sources.map((c) => (
                        <tr key={c.source}>
                          <td style={s.covCell}><b>{c.source}</b>{c.required && <Tag tone={C.accent}>required</Tag>}</td>
                          <td style={{ ...s.covCell, color: covTone(c.status) }}>{c.status}</td>
                          <td style={{ ...s.covCell, color: C.sub }}>{c.findingCount} findings</td>
                          <td style={{ ...s.covCell, color: C.sub }}>{c.detail || ""}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {cov.gaps && cov.gaps.length > 0 && (
                    <div style={{ marginTop: 8, color: C.warn, fontSize: 11.5 }}>
                      Gaps: {cov.gaps.join("  \u00b7  ")}</div>)}
                </Drawer>
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}
function Drawer({ label, children }) {
  return (
    <div style={{ marginBottom: 12 }}>
      <div style={{ fontSize: 10.5, color: C.sub, textTransform: "uppercase", letterSpacing: 0.6,
        marginBottom: 5, fontWeight: 600 }}>{label}</div>
      {children}
    </div>
  );
}
function covTone(st) {
  if (st === "Ok" || st === "Empty") return C.allow;
  if (st === "NotConfigured" || st === "Skipped") return C.sub;
  return C.block;
}
function sevTone(sev) {
  return sev === "Critical" || sev === "High" ? C.block
    : sev === "Medium" ? C.warn : C.sub;
}
function ReachBadge({ r, detail }) {
  const tone = r === "Reachable" ? C.block : r === "NotReachable" ? C.allow : C.sub;
  const label = r === "NotReachable" ? "not reachable" : r === "Reachable" ? "reachable" : "reach: unknown";
  return <span title={detail || ""} style={{ fontFamily: C.mono, fontSize: 10, padding: "1px 5px",
    border: `1px solid ${tone}`, color: tone, borderRadius: 2 }}>{label}</span>;
}

// ── helpers ─────────────────────────────────────────────────────────────────
function computeStats(a) {
  return {
    total: a.length,
    blocked: a.filter((e) => e.decision === "Block").length,
    allowed: a.filter((e) => e.decision === "Allow").length,
    quarantined: a.filter((e) => e.decision === "Quarantine").length,
    components: a.reduce((n, e) => n + (e.componentsEvaluated ?? 1), 0),
  };
}
function countControls(p) {
  let n = 5; // fixed severity/age/depth controls
  if (p.weights.safetensorsOnly) n++; if (p.weights.blockPickle) n++; if (p.weights.requireHashPin) n++;
  return n;
}

// ── LLM Gateway (AI/ML) — LiteLLM-style proxy with full DLP visibility ─────────
// Transparent proxy for OpenAI / Anthropic / Groq. Every call is recorded WITH a redacted
// transcript of what crossed the wire and per-category DLP findings (PII/POPIA-GDPR, payment
// cards, secrets, proprietary code). Click a row to see exactly what was sent and what was caught.
const DLP_META = {
  PII: { label: "PII (POPIA/GDPR)", icon: "user", c: "#7c5cff" },
  PaymentCard: { label: "Payment cards", icon: "card", c: "#d63649" },
  Secret: { label: "Secrets", icon: "key", c: "#d99016" },
  SourceCode: { label: "Proprietary code", icon: "code", c: "#1f7fd1" },
};
// One call record row, expandable to show DLP findings + original vs redacted transcript.
function LlmRecordRow({ r, open, onToggle }) {
  return (
    <React.Fragment>
      <tr style={{ ...s.tr, cursor: "pointer" }} onClick={onToggle}>
        <td style={{ ...s.td, color: C.sub, fontSize: 11.5, whiteSpace: "nowrap" }}>{new Date(r.timestamp).toLocaleString(undefined, { month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" })}</td>
        <td style={s.td}><Tag tone={C.info}>{r.provider}</Tag></td>
        <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{r.model || "—"}</td>
        <td style={{ ...s.td, color: C.sub }}>{r.actor}</td>
        <td style={s.td}>{r.decision === "Blocked"
          ? <span style={{ display: "inline-flex", alignItems: "center", gap: 5, color: C.block, fontWeight: 700 }} title={r.reason || ""}><Icon name="alert" size={13} color={C.block} /> Blocked</span>
          : <span style={{ display: "inline-flex", alignItems: "center", gap: 5, color: C.accentDim, fontWeight: 700 }}><Icon name="check" size={13} color={C.accentDim} /> Allowed</span>}</td>
        <td style={s.td}>
          {(r.dlp || []).length === 0 ? <span style={{ color: C.sub }}>clean</span>
            : <span style={{ display: "flex", gap: 5, flexWrap: "wrap" }}>
                {[...new Set((r.dlp || []).map((d) => d.category))].map((cat) => (
                  <span key={cat} title={(r.dlp.filter((d) => d.category === cat).map((d) => `${d.rule}×${d.count} (${d.method})`).join(", "))}
                    style={{ fontSize: 10, fontWeight: 700, color: DLP_META[cat]?.c || C.sub, border: `1px solid ${DLP_META[cat]?.c || C.line}`, borderRadius: 3, padding: "1px 5px", display: "inline-flex", alignItems: "center", gap: 3 }}>
                    <Icon name={DLP_META[cat]?.icon} size={11} color={DLP_META[cat]?.c} /> {cat}</span>
                ))}</span>}
        </td>
        <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11 }}>{r.tokensIn != null ? `${r.tokensIn}/${r.tokensOut ?? 0}` : "—"}</td>
        <td style={{ ...s.td, color: C.sub, fontSize: 11 }}>{open ? "▾" : "▸"}</td>
      </tr>
      {open && (
        <tr><td colSpan={8} style={{ padding: 0, background: C.bg2 }}>
          <div style={{ padding: "14px 20px" }}>
            {r.reason && <div style={{ display: "flex", alignItems: "center", gap: 6, color: C.block, fontSize: 12, fontWeight: 600, marginBottom: 10 }}><Icon name="alert" size={14} color={C.block} /> {r.reason}</div>}
            {(r.dlp || []).length > 0 && (
              <div style={{ marginBottom: 12 }}>
                <div style={{ fontSize: 11, fontWeight: 700, color: C.sub, textTransform: "uppercase", marginBottom: 6 }}>DLP findings</div>
                {r.dlp.map((d, i) => (
                  <div key={i} style={{ display: "flex", gap: 10, alignItems: "center", fontSize: 12, padding: "3px 0" }}>
                    <span style={{ color: DLP_META[d.category]?.c || C.sub, fontWeight: 700, minWidth: 150, display: "inline-flex", alignItems: "center", gap: 5 }}><Icon name={DLP_META[d.category]?.icon} size={12} color={DLP_META[d.category]?.c} /> {d.rule}</span>
                    <span style={{ fontSize: 9.5, color: d.method === "openai-privacy-filter" ? C.accentDim : C.dim, border: `1px solid ${d.method === "openai-privacy-filter" ? C.accent : C.line}`, borderRadius: 3, padding: "0 4px" }} title="detection engine">{d.method}</span>
                    <span style={{ color: C.sub }}>×{d.count}</span>
                    <span style={{ fontFamily: C.mono, fontSize: 11 }}>{d.sample}</span>
                    <span style={{ color: sevTone(d.severity), fontSize: 10, fontWeight: 700 }}>{d.severity}</span>
                  </div>
                ))}
              </div>
            )}
            {/* Original vs redacted, side by side — exactly what was attempted vs what would leave */}
            <div style={{ display: "grid", gridTemplateColumns: r.original ? "1fr 1fr" : "1fr", gap: 12 }}>
              {r.original && <div>
                <div style={{ fontSize: 11, fontWeight: 700, color: C.block, textTransform: "uppercase", marginBottom: 6 }}>Original (attempted)</div>
                <pre style={{ ...s.codeBlock, maxHeight: 240, whiteSpace: "pre-wrap", background: "#2a1416", border: `1px solid rgba(214,54,73,.3)` }}>{r.original}</pre>
              </div>}
              <div>
                <div style={{ fontSize: 11, fontWeight: 700, color: C.accentDim, textTransform: "uppercase", marginBottom: 6 }}>Redacted (what would leave)</div>
                <pre style={{ ...s.codeBlock, maxHeight: 240, whiteSpace: "pre-wrap" }}>{r.preview || "(transcript capture is off — enable it under DLP & Policy)"}</pre>
              </div>
            </div>
          </div>
        </td></tr>
      )}
    </React.Fragment>
  );
}

// Tiny inline bar chart (no chart lib): [{label,value,color}] → horizontal bars.
function BarChart({ data, title }) {
  const max = Math.max(1, ...data.map((d) => d.value));
  return (
    <div style={{ ...s.card, marginBottom: 0, padding: "16px 18px" }}>
      <div style={{ fontSize: 12.5, fontWeight: 600, marginBottom: 12 }}>{title}</div>
      {data.length === 0 && <div style={{ color: C.sub, fontSize: 12 }}>No data yet.</div>}
      {data.map((d, i) => (
        <div key={i} style={{ marginBottom: 9 }}>
          <div style={{ display: "flex", justifyContent: "space-between", fontSize: 11.5, marginBottom: 3 }}>
            <span style={{ display: "flex", alignItems: "center", gap: 6, color: C.sub }}>{d.icon && <Icon name={d.icon} size={12} color={d.color} />}{d.label}</span>
            <b style={{ color: d.color }}>{d.value}</b>
          </div>
          <div style={{ height: 7, borderRadius: 4, background: C.lineSoft, overflow: "hidden" }}>
            <div style={{ width: `${(d.value / max) * 100}%`, height: "100%", background: d.color }} /></div>
        </div>
      ))}
    </div>
  );
}

function LlmGateway({ policy, setPolicy, save, saving }) {
  const [data, setData] = useState(null);
  const [engine, setEngine] = useState(null);
  const [tab, setTab] = useState("overview");
  const [open, setOpen] = useState(null);
  const [filter, setFilter] = useState({ provider: "", decision: "", category: "", q: "" });
  const llm = policy.llm || {};
  const setLlm = (patch) => setPolicy((p) => ({ ...p, llm: { ...(p.llm || {}), ...patch } }));
  const load = () => api.llmRecords().then(setData).catch(() => setData({ stats: { total: 0, blocked: 0, byProvider: {}, dlpHits: {} }, records: [] }));
  useEffect(() => { load(); api.llmEngine().then(setEngine).catch(() => {}); }, []);
  useEffect(() => { const t = setInterval(load, 4000); return () => clearInterval(t); }, []);

  const st = data?.stats || { total: 0, blocked: 0, byProvider: {}, dlpHits: {}, tokensIn: 0, tokensOut: 0 };
  const records = data?.records || [];
  const dlpHits = st.dlpHits || {};
  const totalDlp = Object.values(dlpHits).reduce((a, b) => a + b, 0);
  const blockRate = st.total ? Math.round((st.blocked / st.total) * 100) : 0;

  const tabs = [["overview", "Overview"], ["audit", "Audit log"], ["blocked", "Blocked & quarantined"], ["dlp", "DLP & Policy"], ["connect", "Connect"]];
  const filtered = records.filter((r) =>
    (!filter.provider || r.provider === filter.provider) &&
    (!filter.decision || r.decision === filter.decision) &&
    (!filter.category || (r.dlp || []).some((d) => d.category === filter.category)) &&
    (!filter.q || JSON.stringify(r).toLowerCase().includes(filter.q.toLowerCase())));
  const blocked = records.filter((r) => r.decision === "Blocked");

  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
        <Crumb trail={[{ label: "All Projects" }, { label: "AI/ML" }, { label: "LLM Gateway" }]} />
        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          {engine && (() => {
            const st = engine.privacyFilterState || (engine.privacyFilterReady ? "ready" : "down");
            const label = { ready: "OpenAI Privacy Filter — on-prem model active", loading: "model loading…", "cpu-unsupported": "model needs GPU · Groq fallback active", unsupported: "model pending runtime · Groq fallback active", error: "model error · Groq fallback active", down: "Groq fallback" }[st] || "Groq fallback";
            const ok = st === "ready";
            return <span style={{ display: "inline-flex", alignItems: "center", gap: 6, fontSize: 11.5, fontWeight: 600,
              color: ok ? C.accentDim : st === "loading" ? C.warn : C.sub,
              border: `1px solid ${ok ? C.accent : C.line}`, borderRadius: 6, padding: "4px 10px" }}>
              <Icon name="shield" size={13} color={ok ? C.accentDim : C.sub} /> Privacy Filter: {label}</span>;
          })()}
        </div>
      </div>

      <div style={{ display: "flex", gap: 22, borderBottom: `1px solid ${C.line}`, marginBottom: 16 }}>
        {tabs.map(([k, l]) => (
          <button key={k} onClick={() => setTab(k)} style={{ ...s.jfTab, ...(tab === k ? s.jfTabOn : {}) }}>
            {l}{k === "blocked" && blocked.length > 0 ? ` (${blocked.length})` : ""}</button>
        ))}
      </div>

      {tab === "overview" && (<>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(4,1fr)", gap: 14, marginBottom: 14 }}>
          <MiniStat label="Calls intercepted" value={st.total} />
          <MiniStat label="Blocked" value={`${st.blocked} · ${blockRate}%`} tone={st.blocked > 0 ? C.block : C.ink} />
          <MiniStat label="DLP detections" value={totalDlp} tone={totalDlp > 0 ? C.warn : C.ink} />
          <MiniStat label="Tokens (in/out)" value={`${fmtN(st.tokensIn || 0)}/${fmtN(st.tokensOut || 0)}`} mono />
        </div>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 14, marginBottom: 14 }}>
          <BarChart title="DLP detections by category" data={Object.entries(DLP_META).map(([k, m]) => ({ label: m.label, value: dlpHits[k] || 0, color: m.c, icon: m.icon }))} />
          <BarChart title="Calls by provider" data={Object.entries(st.byProvider || {}).map(([k, v]) => ({ label: k, value: v, color: C.info }))} />
        </div>
        <BarChart title="Decisions" data={[
          { label: "Allowed", value: st.total - st.blocked, color: C.accent },
          { label: "Blocked", value: st.blocked, color: C.block },
        ]} />
        <Callout>
          <b>How it protects you.</b> Every call is intercepted, recorded, and DLP-scanned before it leaves the
          network. PII is detected by the on-prem <b>OpenAI Privacy Filter</b> model (no data leaves for scanning);
          payment cards by Luhn; secrets and proprietary code by pattern + AI. Blocked items never reach the provider.
        </Callout>
      </>)}

      {tab === "audit" && (
        <div style={s.card}>
          <div style={{ display: "flex", gap: 8, padding: "12px 16px", borderBottom: `1px solid ${C.lineSoft}`, flexWrap: "wrap", alignItems: "center" }}>
            <input placeholder="Search…" value={filter.q} onChange={(e) => setFilter({ ...filter, q: e.target.value })} style={{ ...s.formInput, width: 180 }} />
            <select style={s.select} value={filter.provider} onChange={(e) => setFilter({ ...filter, provider: e.target.value })}>
              <option value="">All providers</option><option>openai</option><option>anthropic</option><option>groq</option></select>
            <select style={s.select} value={filter.decision} onChange={(e) => setFilter({ ...filter, decision: e.target.value })}>
              <option value="">All decisions</option><option>Allowed</option><option>Blocked</option></select>
            <select style={s.select} value={filter.category} onChange={(e) => setFilter({ ...filter, category: e.target.value })}>
              <option value="">All DLP</option>{Object.entries(DLP_META).map(([k, m]) => <option key={k} value={k}>{m.label}</option>)}</select>
            <span style={{ flex: 1 }} />
            <a href={api.llmExportUrl()} style={{ ...s.btnGhost, textDecoration: "none", color: C.ink, display: "inline-flex", alignItems: "center", gap: 6 }}><Icon name="download" size={13} /> Export CSV</a>
          </div>
          <table style={s.table}><thead><tr>
            {["Time", "Provider", "Model", "Actor", "Decision", "DLP findings", "Tokens", ""].map((c, i) => <th key={i} style={s.th}>{c}</th>)}
          </tr></thead><tbody>
            {filtered.length === 0 && (
              <tr><td colSpan={8} style={{ padding: "48px 20px", textAlign: "center" }}>
                <Icon name="search" size={36} color={C.dim} style={{ marginBottom: 8 }} />
                <div style={{ fontSize: 14, fontWeight: 600 }}>{records.length === 0 ? "No LLM calls intercepted yet" : "No calls match the filters"}</div>
                {records.length === 0 && <div style={{ color: C.sub, fontSize: 12.5, marginTop: 5 }}>Point an SDK at the gateway (see <a style={s.linkGreen} onClick={() => setTab("connect")}>Connect</a>).</div>}
              </td></tr>
            )}
            {filtered.map((r) => <LlmRecordRow key={r.id} r={r} open={open === r.id} onToggle={() => setOpen(open === r.id ? null : r.id)} />)}
          </tbody></table>
        </div>
      )}

      {tab === "blocked" && (
        <div style={s.card}>
          <div style={{ padding: "12px 18px", borderBottom: `1px solid ${C.lineSoft}`, fontSize: 12.5, color: C.sub }}>
            Everything the gateway stopped before it reached a provider — grouped for compliance review. Click a row for the blocked content and the rule that caught it.</div>
          <table style={s.table}><thead><tr>
            {["Time", "Provider", "Model", "Actor", "Blocked reason", "DLP findings", "", ""].map((c, i) => <th key={i} style={s.th}>{c}</th>)}
          </tr></thead><tbody>
            {blocked.length === 0 && (
              <tr><td colSpan={8} style={{ padding: "48px 20px", textAlign: "center" }}>
                <Icon name="check" size={36} color={C.accentDim} style={{ marginBottom: 8 }} />
                <div style={{ fontSize: 14, fontWeight: 600 }}>Nothing blocked</div>
                <div style={{ color: C.sub, fontSize: 12.5, marginTop: 5 }}>No outbound call has violated a DLP or provider policy.</div>
              </td></tr>
            )}
            {blocked.map((r) => (
              <React.Fragment key={r.id}>
                <tr style={{ ...s.tr, cursor: "pointer" }} onClick={() => setOpen(open === r.id ? null : r.id)}>
                  <td style={{ ...s.td, color: C.sub, fontSize: 11.5, whiteSpace: "nowrap" }}>{new Date(r.timestamp).toLocaleString(undefined, { month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit" })}</td>
                  <td style={s.td}><Tag tone={C.info}>{r.provider}</Tag></td>
                  <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{r.model || "—"}</td>
                  <td style={{ ...s.td, color: C.sub }}>{r.actor}</td>
                  <td style={{ ...s.td, color: C.block, fontSize: 11.5, fontWeight: 600 }}>{r.reason}</td>
                  <td style={s.td}>{[...new Set((r.dlp || []).map((d) => d.category))].map((cat) => (
                    <span key={cat} style={{ fontSize: 10, fontWeight: 700, color: DLP_META[cat]?.c, marginRight: 5, display: "inline-flex", alignItems: "center", gap: 3 }}>
                      <Icon name={DLP_META[cat]?.icon} size={11} color={DLP_META[cat]?.c} />{cat}</span>))}</td>
                  <td style={s.td}></td>
                  <td style={{ ...s.td, color: C.sub, fontSize: 11 }}>{open === r.id ? "▾" : "▸"}</td>
                </tr>
                {open === r.id && (
                  <tr><td colSpan={8} style={{ padding: "14px 20px", background: C.bg2 }}>
                    <div style={{ display: "grid", gridTemplateColumns: r.original ? "1fr 1fr" : "1fr", gap: 12 }}>
                      {r.original && <div>
                        <div style={{ fontSize: 11, fontWeight: 700, color: C.block, textTransform: "uppercase", marginBottom: 6 }}>Blocked content (attempted)</div>
                        <pre style={{ ...s.codeBlock, maxHeight: 240, whiteSpace: "pre-wrap", background: "#2a1416", border: `1px solid rgba(214,54,73,.3)` }}>{r.original}</pre></div>}
                      <div>
                        <div style={{ fontSize: 11, fontWeight: 700, color: C.accentDim, textTransform: "uppercase", marginBottom: 6 }}>Redacted</div>
                        <pre style={{ ...s.codeBlock, maxHeight: 240, whiteSpace: "pre-wrap" }}>{r.preview || "(capture off)"}</pre></div>
                    </div>
                  </td></tr>
                )}
              </React.Fragment>
            ))}
          </tbody></table>
        </div>
      )}

      {tab === "dlp" && (
        <div style={s.card}>
          <div style={{ padding: "8px 4px" }}>
            <SubHead>PII detection engine</SubHead>
            {(() => {
              const stt = engine?.privacyFilterState || (engine?.privacyFilterReady ? "ready" : "down");
              const map = {
                ready: { c: C.accentDim, t: "on-prem model loaded — scanning locally via ONNX, no data leaves the network" },
                loading: { c: C.warn, t: "downloading model (first run, ~2.8 GB)…" },
                "cpu-unsupported": { c: C.warn, t: "model requires a GPU on this host (MoE op) — running on the Groq + regex fallback, which is active and effective" },
                unsupported: { c: C.warn, t: "model not yet runnable in this build — running on the Groq + regex fallback" },
                error: { c: C.block, t: "sidecar error — running on the Groq + regex fallback" },
                down: { c: C.sub, t: "sidecar not reachable — running on the Groq + regex fallback" },
              };
              const m = map[stt] || map.down;
              return (
                <div style={{ padding: "4px 22px 10px", fontSize: 12, color: C.sub }}>
                  <b>OpenAI Privacy Filter</b> — the 1.4B-param token-classification model (Apache-2.0,
                  <code style={s.code}>openai/privacy-filter</code>) is wired in as a local sidecar so PII is scanned
                  <b> without leaving your network</b>. Current engine status: <b style={{ color: m.c }}>{m.t}</b>.
                  {stt === "unsupported" && <div style={{ marginTop: 4, color: C.dim }}>Only the config+tokenizer (~53 MB) are cached; the model weights load once the runtime supports the architecture. Active PII coverage today comes from the Groq classifier + Luhn/regex.</div>}
                </div>
              );
            })()}
            <Table cols={["Control", "Rule", "Setting"]}>
              <Ctl id="SEC-LLM-02" rule="Use on-prem OpenAI Privacy Filter for PII (primary engine)"><Switch on={llm.usePrivacyFilter} onChange={(v) => setLlm({ usePrivacyFilter: v })} /></Ctl>
              <Ctl id="SEC-LLM-02" rule="Use AI model (Groq) as PII/code fallback"><Switch on={llm.useAiDlp} onChange={(v) => setLlm({ useAiDlp: v })} /></Ctl>
              <Ctl id="SEC-LLM-00" rule="Capture transcript of every call (original + redacted)"><Switch on={llm.captureTranscripts} onChange={(v) => setLlm({ captureTranscripts: v })} /></Ctl>
            </Table>
            <SubHead>Gateway &amp; providers</SubHead>
            <Table cols={["Control", "Rule", "Setting"]}>
              <Ctl id="SEC-LLM-00" rule="LLM gateway enabled"><Switch on={llm.enabled} onChange={(v) => setLlm({ enabled: v })} /></Ctl>
              <Ctl id="SEC-LLM-01" rule="Allow OpenAI"><Switch on={llm.allowOpenAI} onChange={(v) => setLlm({ allowOpenAI: v })} /></Ctl>
              <Ctl id="SEC-LLM-01" rule="Allow Anthropic"><Switch on={llm.allowAnthropic} onChange={(v) => setLlm({ allowAnthropic: v })} /></Ctl>
              <Ctl id="SEC-LLM-01" rule="Allow Groq"><Switch on={llm.allowGroq} onChange={(v) => setLlm({ allowGroq: v })} /></Ctl>
              <Ctl id="SEC-LLM-01" rule="Blocked models (deny-list)"><Chips tags={llm.blockedModels || []} onChange={(v) => setLlm({ blockedModels: v })} /></Ctl>
            </Table>
            <SubHead>Outbound DLP — data exfiltration controls (SEC-LLM-02)</SubHead>
            <div style={{ padding: "4px 22px 8px", color: C.sub, fontSize: 12 }}>
              <b>Scan</b> = detect &amp; record. <b>Block</b> = also reject the call so the data never leaves.</div>
            <table style={s.table}><thead><tr>
              {["Category", "What it catches", "Scan", "Block"].map((c) => <th key={c} style={s.th}>{c}</th>)}
            </tr></thead><tbody>
              {[["PII", "Pii", "Names, addresses, SA ID, email, phone, IBAN — POPIA/GDPR"],
                ["Cards", "Cards", "Credit-card numbers (Luhn-validated)"],
                ["Secrets", "Secrets", "API keys, tokens, private keys"],
                ["Code", "Code", "Proprietary / confidential source code"]].map(([cat, key, desc]) => (
                <tr key={cat} style={s.tr}>
                  <td style={s.td}><b style={{ display: "inline-flex", alignItems: "center", gap: 6 }}><Icon name={DLP_META[cat === "Cards" ? "PaymentCard" : cat === "Code" ? "SourceCode" : cat]?.icon} size={13} color={DLP_META[cat === "Cards" ? "PaymentCard" : cat === "Code" ? "SourceCode" : cat]?.c} /> {cat}</b></td>
                  <td style={{ ...s.td, color: C.sub, fontSize: 12 }}>{desc}</td>
                  <td style={s.td}><Switch on={!!llm[`scan${key}`]} onChange={(v) => setLlm({ [`scan${key}`]: v })} /></td>
                  <td style={s.td}><Switch on={!!llm[`block${key}`]} onChange={(v) => setLlm({ [`block${key}`]: v })} /></td>
                </tr>
              ))}
            </tbody></table>
            <div style={{ padding: "12px 22px" }}>
              <button onClick={save} disabled={saving} style={s.save}>{saving ? "Signing…" : "Commit & sign policy"}</button>
            </div>
          </div>
        </div>
      )}

      {tab === "connect" && (
        <div style={s.card}>
          <div style={{ padding: "18px 22px" }}>
            <b style={{ fontSize: 13 }}>OpenAI-compatible endpoint — drop-in base URL</b>
            <p style={{ color: C.sub, fontSize: 12.5, marginTop: 6 }}>
              The gateway speaks the standard <b>OpenAI API spec</b> at <code style={s.code}>/v1</code> — the same paths LiteLLM and any
              OpenAI SDK use (<code style={s.code}>/v1/chat/completions</code>, <code style={s.code}>/v1/embeddings</code>, <code style={s.code}>/v1/models</code>).
              Point any client's base URL here, keep your own key, route the provider via the model name
              (<code style={s.code}>anthropic/…</code>, <code style={s.code}>groq/…</code>, else OpenAI). No vendor-specific paths.</p>
            <div style={{ marginTop: 12 }}>
              <div style={{ fontSize: 11.5, fontWeight: 700, color: C.sub, marginBottom: 6 }}>OpenAI SDK (Python) — works for every provider</div>
              <pre style={s.codeBlock}>{`from openai import OpenAI
client = OpenAI(
    base_url="http://localhost:5000/v1",   # ← only change
    api_key="sk-…your provider key…",
)
client.chat.completions.create(model="gpt-4o", messages=[...])
client.chat.completions.create(model="anthropic/claude-3-5-sonnet", messages=[...])
client.chat.completions.create(model="groq/llama-3.3-70b-versatile", messages=[...])`}</pre>
              <div style={{ fontSize: 11.5, fontWeight: 700, color: C.sub, margin: "14px 0 6px" }}>curl</div>
              <pre style={s.codeBlock}>{`curl http://localhost:5000/v1/chat/completions \\
  -H "Authorization: Bearer $OPENAI_API_KEY" \\
  -H "Content-Type: application/json" \\
  -d '{"model":"gpt-4o","messages":[{"role":"user","content":"hi"}]}'`}</pre>
              <div style={{ fontSize: 11.5, fontWeight: 700, color: C.sub, margin: "14px 0 6px" }}>LiteLLM config</div>
              <pre style={s.codeBlock}>{`model_list:
  - model_name: "*"
    litellm_params:
      model: "openai/*"
      api_base: "http://localhost:5000/v1"`}</pre>
              <div style={{ fontSize: 11, color: C.sub, marginTop: 10 }}>Optional escape hatch: force a provider with <code style={s.code}>/api/llm/{`{provider}`}/…</code>.</div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── AppTrust: Applications ────────────────────────────────────────────────────
function Applications() {
  const [data, setData] = useState(null);
  const [sel, setSel] = useState(null);
  useEffect(() => { api.apps().then(setData).catch(() => setData({ applications: [] })); }, []);
  if (sel) return <AppInsights appKey={sel} onBack={() => setSel(null)} />;
  const critTone = (c) => c === "High" ? C.block : c === "Low" ? C.allow : C.warn;
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "AppTrust" }, { label: "Applications" }]} />
      {!data ? <div style={s.kevEmpty}>Loading applications…</div> : (
        <div style={s.card}>
          <table style={s.table}><thead><tr>
            {["Application", "Project", "Criticality", "Packages", "Trusted Releases", "Critical CVEs", "Owners"].map((c) => <th key={c} style={s.th}>{c}</th>)}
          </tr></thead><tbody>
            {data.applications.map((a) => (
              <tr key={a.key} style={s.tr}>
                <td style={s.td}><a style={s.linkDark} onClick={() => setSel(a.key)}>{a.name}</a>
                  <div style={{ color: C.sub, fontSize: 11, marginTop: 2 }}>{a.type} · team:{a.team}</div></td>
                <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{a.project}</td>
                <td style={s.td}><span style={{ color: critTone(a.criticality), fontWeight: 700, fontSize: 12 }}>● {a.criticality}</span></td>
                <td style={s.td}>{a.packages}</td>
                <td style={{ ...s.td, fontWeight: 600, color: a.trustedReleases > 0 ? C.accentDim : C.sub }}>{a.trustedReleases}</td>
                <td style={{ ...s.td, fontWeight: 600, color: a.criticalCves > 0 ? C.block : C.ink }}>{a.criticalCves}</td>
                <td style={{ ...s.td, color: C.sub, fontSize: 11.5 }}>{a.owners}</td>
              </tr>
            ))}
          </tbody></table>
        </div>
      )}
    </div>
  );
}

function AppInsights({ appKey, onBack }) {
  const [d, setD] = useState(null);
  useEffect(() => { api.app(appKey).then(setD).catch(() => setD(null)); }, [appKey]);
  if (!d) return <div style={s.kevEmpty}>Loading…</div>;
  const a = d.application, ins = d.insights;
  const critTone = (c) => c === "High" ? C.block : c === "Low" ? C.allow : C.warn;
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "AppTrust" }, { label: "Applications", onClick: onBack }, { label: a.name }]} />
      <div style={{ fontSize: 17, fontWeight: 700, marginBottom: 14, display: "flex", alignItems: "center", gap: 8 }}>
        ▦ Insights — {a.name}</div>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 18, alignItems: "start" }}>
        <div style={{ ...s.card, padding: "18px 20px" }}>
          <b style={{ fontSize: 13 }}>Application Details</b>
          <div style={{ display: "flex", gap: 10, alignItems: "center", margin: "10px 0" }}>
            <Tag tone={C.info}>{a.type}</Tag><Tag tone={C.sub}>team:{a.team}</Tag>
            <span style={{ color: critTone(a.criticality), fontWeight: 700, fontSize: 12 }}>● {a.criticality}</span>
          </div>
          <KV k="Project Key" v={a.project} />
          <KV k="Application Key" v={a.key} />
          <KV k="Owners" v={a.owners} />
          <KV k="Description" v={a.description} />
          <KV k="Bound packages" v={a.packages.join(", ") || "—"} />
        </div>
        <div style={{ ...s.card, padding: "18px 20px", borderLeft: `3px solid ${ins.newlyDetectedCriticalCves.length ? C.block : C.accent}` }}>
          <b style={{ fontSize: 13 }}>Post-Release | Newly Detected Critical CVEs</b>
          {ins.newlyDetectedCriticalCves.length === 0 ? (
            <div style={{ marginTop: 12 }}>
              <div style={{ fontWeight: 700, fontSize: 14 }}>{ins.trustedReleases > 0 ? "No new critical CVEs" : "No Trusted Releases Found"}</div>
              <div style={{ color: C.sub, fontSize: 12.5, marginTop: 5 }}>
                {ins.trustedReleases > 0
                  ? "All bound packages are clear of critical CVEs in the latest evaluations."
                  : "No versions have passed the gate yet. Once a version becomes trusted, AppTrust monitors it for critical CVEs."}</div>
            </div>
          ) : (
            <div style={{ marginTop: 10 }}>
              {ins.newlyDetectedCriticalCves.slice(0, 8).map((c, i) => (
                <div key={i} style={{ display: "flex", justifyContent: "space-between", padding: "7px 0", borderBottom: `1px solid ${C.lineSoft}`, fontSize: 12 }}>
                  <span><b style={{ color: C.block }}>{c.cve}</b> {c.knownExploited && <span style={s.kevBadge}>KEV</span>}
                    <div style={{ color: C.sub, fontFamily: C.mono, fontSize: 11 }}>{c.resource}</div></span>
                  <span style={{ color: C.sub, whiteSpace: "nowrap" }}>{c.fixedVersion ? `→ ${c.fixedVersion}` : "no fix"}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(3,1fr)", gap: 14, marginTop: 16 }}>
        <MiniStat label="Trusted releases" value={ins.trustedReleases} tone={C.accentDim} />
        <MiniStat label="Blocked versions" value={ins.blockedVersions} tone={ins.blockedVersions > 0 ? C.block : C.ink} />
        <MiniStat label="Evaluations" value={ins.evaluated} />
      </div>
    </div>
  );
}

// ── AppTrust: Unified Policies (read-only governance map over the signed policy) ──
function UnifiedPolicies({ policy, setTab }) {
  const rows = [
    ...(policy.watches || []).map((w) => ({ name: polName(w), type: polType(w), scope: w.name, rules: w.rules.length })),
    { name: "Model-Registry-Enforcement", type: "AI/ML", scope: "HuggingFace models", rules: policy.enforceModelAllowList ? "enforced" : "advisory" },
    { name: "LLM-Gateway", type: "AI/ML", scope: "OpenAI/Anthropic/Groq", rules: (policy.llm?.enabled ? "active" : "disabled") },
  ];
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "AppTrust" }, { label: "Unified Policies" }]} />
      <p style={{ color: C.sub, fontSize: 12.5, marginBottom: 14 }}>Every governing policy across Xray, the AI Catalog and the LLM Gateway, unified. All live in the one signed policy document.</p>
      <div style={s.card}>
        <table style={s.table}><thead><tr>
          {["Policy", "Type", "Scope", "Rules"].map((c) => <th key={c} style={s.th}>{c}</th>)}
        </tr></thead><tbody>
          {rows.map((r, i) => (
            <tr key={i} style={s.tr}>
              <td style={s.td}><a style={s.linkDark} onClick={() => setTab(r.type === "AI/ML" ? "aicatalog" : "watches")}>{r.name}</a></td>
              <td style={s.td}><Tag tone={r.type === "License" ? C.warn : r.type === "AI/ML" ? C.info : C.accent}>{r.type}</Tag></td>
              <td style={{ ...s.td, fontFamily: C.mono, fontSize: 11.5 }}>{r.scope}</td>
              <td style={{ ...s.td, color: C.sub }}>{r.rules}</td>
            </tr>
          ))}
        </tbody></table>
      </div>
    </div>
  );
}

// ── AppTrust: Waivers (the approved-exceptions register, AppTrust framing) ────────
function Waivers({ policy, setPolicy }) {
  return (
    <div style={{ animation: "fwfade .2s ease" }}>
      <Crumb trail={[{ label: "All Projects" }, { label: "AppTrust" }, { label: "Waivers" }]} />
      <p style={{ color: C.sub, fontSize: 12.5, marginBottom: 14 }}>Time-boxed, attributed waivers that override a policy decision for a specific component. Each is part of the signed policy and shows in the decision ledger.</p>
      <Exceptions policy={policy} setPolicy={setPolicy} />
    </div>
  );
}

// ── styles ────────────────────────────────────────────────────────────────────
const s = {
  topbar: { display: "flex", justifyContent: "space-between", alignItems: "center",
    background: C.navbar, color: "#fff", padding: "0 24px", height: 56,
    position: "sticky", top: 0, zIndex: 5, boxShadow: "0 1px 8px rgba(0,0,0,.2)" },
  logo: { width: 30, height: 30, borderRadius: 7, display: "grid", placeItems: "center",
    fontSize: 16, fontWeight: 800, color: C.navbar, background: "#fff" },
  product: { fontWeight: 700, fontSize: 15, color: "#fff" },
  env: { fontSize: 11, color: "rgba(255,255,255,.6)", marginTop: 1 },
  policyVer: { fontSize: 12, fontFamily: C.mono, color: "#fff" },
  sig: { fontSize: 10, color: "rgba(255,255,255,.55)", fontFamily: C.mono },
  appTab: { fontSize: 13, color: "rgba(255,255,255,.7)", padding: "4px 6px", cursor: "pointer" },
  appTabOn: { fontSize: 13, color: "#fff", fontWeight: 600, padding: "4px 6px", cursor: "pointer", borderBottom: `2px solid ${C.accent}` },
  globalSearch: { display: "flex", alignItems: "center", gap: 6, border: `1px solid rgba(255,255,255,.2)`, borderRadius: 6, padding: "5px 10px", background: "rgba(255,255,255,.08)" },
  askAi: { background: "linear-gradient(90deg,#d6f15e,#7fd957)", color: "#143a14", border: "none", borderRadius: 16, padding: "5px 12px", fontSize: 12, fontWeight: 600, cursor: "pointer" },
  // --- Ask AI slide-out panel ---
  aiPanel: { position: "fixed", top: 0, right: 0, bottom: 0, width: "min(420px,96vw)", background: C.surface,
    borderLeft: `1px solid ${C.line}`, boxShadow: "-12px 0 40px rgba(0,0,0,.12)", zIndex: 60,
    display: "flex", flexDirection: "column", animation: "fwslide .18s ease" },
  aiHead: { display: "flex", justifyContent: "space-between", alignItems: "center", padding: "14px 16px", borderBottom: `1px solid ${C.lineSoft}` },
  aiBeta: { fontSize: 10, fontWeight: 700, color: C.accentDim, background: "rgba(64,190,70,.12)", borderRadius: 4, padding: "1px 6px" },
  aiBanner: { fontSize: 12, color: "#8a5a00", background: "rgba(217,144,22,.1)", borderBottom: `1px solid rgba(217,144,22,.25)`, padding: "9px 16px" },
  aiLink: { color: C.accentDim, fontWeight: 600, cursor: "pointer", textDecoration: "underline" },
  aiBody: { flex: 1, overflow: "auto", padding: "8px 16px" },
  aiCard: { textAlign: "left", background: C.surface, border: `1px solid ${C.line}`, borderRadius: 12, padding: "14px 16px",
    cursor: "pointer", width: "100%", transition: "border-color .12s" },
  aiUser: { background: C.brand, color: "#fff", borderRadius: "12px 12px 4px 12px", padding: "9px 13px", fontSize: 13, whiteSpace: "pre-wrap", lineHeight: 1.5 },
  aiAsst: { background: C.surface2, color: C.ink, border: `1px solid ${C.line}`, borderRadius: "12px 12px 12px 4px", padding: "9px 13px", fontSize: 13, whiteSpace: "pre-wrap", lineHeight: 1.55 },
  aiDots: { color: C.dim, letterSpacing: 2, animation: "fwpulse 1.2s infinite" },
  aiInputBar: { display: "flex", gap: 8, alignItems: "center", padding: "12px 16px 6px", borderTop: `1px solid ${C.lineSoft}` },
  aiInput: { flex: 1, border: `1px solid ${C.line}`, borderRadius: 10, padding: "10px 13px", fontSize: 13, fontFamily: C.sans, outline: "none" },
  aiSend: { width: 36, height: 36, borderRadius: 9, border: "none", background: C.brand, color: "#fff", fontSize: 16, cursor: "pointer" },
  linkBtn: { background: "none", border: "none", color: C.accentDim, fontSize: 11, fontWeight: 600, cursor: "pointer", padding: 0 },
  codeBlock: { background: "#0e1726", color: "#d6e2f0", borderRadius: 8, padding: "12px 14px", fontFamily: C.mono,
    fontSize: 11.5, lineHeight: 1.6, overflowX: "auto", margin: 0, whiteSpace: "pre" },
  // --- JFrog Xray governance pages (watches / violations / on-demand / policy wizard) ---
  jfTab: { background: "none", border: "none", borderBottom: "2.5px solid transparent", padding: "9px 2px 10px",
    fontSize: 13, fontWeight: 600, color: C.sub, cursor: "pointer", fontFamily: C.sans },
  jfTabOn: { color: C.ink, borderBottomColor: C.accent },
  linkDark: { color: C.ink, textDecoration: "underline", cursor: "pointer", fontWeight: 500 },
  linkGreen: { color: C.accentDim, textDecoration: "underline", cursor: "pointer", fontWeight: 500 },
  resPop: { position: "absolute", top: "calc(100% + 6px)", left: 0, zIndex: 30, background: C.surface,
    border: `1px solid ${C.line}`, borderRadius: 10, boxShadow: "0 12px 34px rgba(0,0,0,.14)", padding: "12px 16px", minWidth: 220 },
  iconBtn: { background: C.surface, border: `1px solid ${C.line}`, borderRadius: 7, width: 32, height: 32,
    fontSize: 14, color: C.sub, cursor: "pointer" },
  kevBadge: { marginLeft: 7, fontSize: 8.5, fontWeight: 800, color: C.accentDim, border: `1px solid ${C.accent}`,
    borderRadius: 3, padding: "1px 4px", verticalAlign: "middle", letterSpacing: 0.4 },
  chipBtn: { background: C.surface, border: `1px solid ${C.line}`, borderRadius: 14, padding: "4px 12px",
    fontSize: 11.5, fontWeight: 600, color: C.sub, cursor: "pointer", fontFamily: C.sans },
  chipBtnOn: { borderColor: C.accent, color: C.accentDim, background: "rgba(64,190,70,.08)" },
  stepCircle: { width: 34, height: 34, borderRadius: "50%", border: `2px solid ${C.line}`, background: C.surface,
    display: "grid", placeItems: "center", fontSize: 13, fontWeight: 700, color: C.sub, flexShrink: 0 },
  stepCircleOn: { borderColor: C.accent, color: C.ink },
  stepCircleDone: { borderColor: C.accent, background: C.accent, color: "#fff" },
  stepLine: { flex: 1, width: 0, minHeight: 28, borderLeft: `2px dashed ${C.line}`, margin: "4px 0" },
  stepHead: { display: "flex", justifyContent: "space-between", alignItems: "center", width: "100%",
    background: "none", border: "none", padding: "15px 22px", cursor: "pointer", fontFamily: C.sans, textAlign: "left" },
  aiNewChat: { background: C.surface2, border: `1px solid ${C.line}`, borderRadius: 7, padding: "4px 10px", fontSize: 11.5, fontWeight: 600, color: C.sub, cursor: "pointer" },
  // markdown inside assistant bubbles
  mdCode: { fontFamily: C.mono, fontSize: 11.5, background: "rgba(22,36,58,.07)", color: C.brand, borderRadius: 4, padding: "1px 5px" },
  mdList: { margin: "4px 0", paddingLeft: 20 },
  mdTable: { borderCollapse: "collapse", width: "100%", fontSize: 11.5 },
  mdTh: { textAlign: "left", padding: "5px 8px", borderBottom: `2px solid ${C.line}`, color: C.sub, fontWeight: 600, whiteSpace: "nowrap" },
  mdTd: { padding: "5px 8px", borderBottom: `1px solid ${C.lineSoft}`, verticalAlign: "top" },
  avatar: { width: 28, height: 28, borderRadius: "50%", background: "#6b3fa0", color: "#fff", display: "grid", placeItems: "center", fontSize: 12, fontWeight: 600 },
  projectSel: { display: "flex", justifyContent: "space-between", alignItems: "center", border: `1px solid rgba(255,255,255,.18)`,
    borderRadius: 6, padding: "9px 11px", margin: "0 4px 12px", fontSize: 13, fontWeight: 600, color: "#fff", cursor: "pointer", background: "rgba(255,255,255,.06)" },
  kpis: { display: "grid", gridTemplateColumns: "repeat(6,1fr)", gap: 12, padding: "20px 28px 6px" },
  kpi: { background: C.surface, border: `1px solid ${C.line}`,
    borderRadius: 14, padding: "16px 18px", position: "relative", overflow: "hidden", transition: ".15s",
    boxShadow: "0 2px 12px rgba(15,39,72,.04)" },
  kpiVal: { fontSize: 26, fontWeight: 600, fontFamily: C.mono, letterSpacing: -1 },
  kpiLbl: { fontSize: 10.5, color: C.sub, marginTop: 4, textTransform: "uppercase", letterSpacing: 0.7 },
  kpiHint: { marginLeft: 5, color: C.accent, fontSize: 10, opacity: 0.7 },
  filterBar: { display: "flex", alignItems: "center", gap: 8, padding: "11px 22px",
    borderBottom: `1px solid ${C.lineSoft}`, fontSize: 12, background: C.bg2 },
  filterChip: { display: "inline-flex", alignItems: "center", gap: 4, fontFamily: C.mono, fontSize: 11,
    fontWeight: 600, border: `1px solid ${C.line}`, borderRadius: 4, padding: "2px 4px 2px 8px", textTransform: "uppercase" },
  filterX: { background: "none", border: "none", cursor: "pointer", color: "inherit", fontSize: 14, padding: 0, lineHeight: 1 },
  violTab: { background: C.surface2, border: `1px solid ${C.line}`, borderRadius: 6, padding: "5px 11px",
    cursor: "pointer", fontSize: 11.5, color: C.sub, fontFamily: C.sans },
  violTabOn: { background: C.accent, color: C.name === "dark" ? "#06140f" : "#fff", borderColor: C.accent, fontWeight: 600 },
  hTab: { background: "none", border: "none", padding: "9px 14px", fontSize: 13, color: C.sub,
    cursor: "pointer", fontFamily: C.sans, borderBottom: "2px solid transparent", marginBottom: -1 },
  hTabOn: { color: C.accent, fontWeight: 600, borderBottom: `2px solid ${C.accent}` },
  artNav: { display: "flex", justifyContent: "space-between", alignItems: "center", width: "100%",
    textAlign: "left", background: "none", border: "none", padding: "10px 16px", cursor: "pointer",
    color: C.sub, fontSize: 12.5, fontFamily: C.sans, borderLeft: "2px solid transparent" },
  artNavOn: { background: C.name === "dark" ? "rgba(61,214,163,.1)" : `${C.accent}10`, color: C.accent,
    fontWeight: 600, borderLeft: `2px solid ${C.accent}` },
  cveScrim: { position: "fixed", inset: 0, background: "rgba(4,7,11,.4)", zIndex: 60, display: "flex", justifyContent: "flex-end" },
  cvePanel: { width: "min(560px,94vw)", height: "100%", background: C.surface, borderLeft: `1px solid ${C.line}`,
    boxShadow: "-12px 0 40px rgba(0,0,0,.4)", display: "flex", flexDirection: "column", animation: "fwfade .15s ease" },
  impactNode: { border: `1px solid ${C.line}`, borderRadius: 8, padding: "10px 12px", fontFamily: C.mono,
    fontSize: 11.5, background: C.bg2 },
  watchCard: { border: `1px solid ${C.line}`, borderRadius: 13, margin: "0 22px 16px", overflow: "hidden",
    background: C.surface, boxShadow: "0 4px 18px rgba(0,0,0,.22)" },
  watchHead: { display: "flex", justifyContent: "space-between", alignItems: "center",
    padding: "12px 16px", background: C.bg2, borderBottom: `1px solid ${C.lineSoft}` },
  watchName: { fontWeight: 600, fontFamily: C.mono, fontSize: 12.5, color: C.ink },
  scopePill: { fontFamily: C.mono, fontSize: 10, color: C.sub, background: C.lineSoft,
    padding: "3px 9px", borderRadius: 20 },
  ruleRow: { display: "flex", justifyContent: "space-between", alignItems: "center", gap: 12,
    padding: "10px 12px", border: `1px solid ${C.lineSoft}`, borderRadius: 9, background: C.bg2 },
  miniBtn: { background: "none", border: `1px solid ${C.line}`, borderRadius: 6, padding: "4px 10px",
    fontSize: 11, cursor: "pointer", color: C.ink, fontFamily: C.sans },
  addRuleBtn: { alignSelf: "flex-start", marginTop: 2, background: "none", border: `1px dashed ${C.line}`,
    borderRadius: 8, padding: "8px 14px", fontSize: 11.5, color: C.accent, cursor: "pointer",
    fontWeight: 600, fontFamily: C.sans },
  // --- rule modal ---
  modalScrim: { position: "fixed", inset: 0, background: "rgba(4,7,11,.62)", display: "grid",
    placeItems: "center", zIndex: 50, padding: 20, backdropFilter: "blur(4px)" },
  modal: { background: C.surface, borderRadius: 14, width: "min(860px,96vw)", maxHeight: "90vh",
    overflow: "auto", boxShadow: "0 30px 80px rgba(0,0,0,.6)", border: `1px solid ${C.line}`,
    animation: "fwfade .15s ease" },
  modalHead: { display: "flex", justifyContent: "space-between", alignItems: "center",
    padding: "16px 20px", borderBottom: `1px solid ${C.lineSoft}` },
  modalX: { background: "none", border: "none", fontSize: 22, lineHeight: 1, color: C.sub, cursor: "pointer", padding: 0 },
  modalFoot: { display: "flex", justifyContent: "flex-end", gap: 10, padding: "14px 20px",
    borderTop: `1px solid ${C.lineSoft}`, background: C.bg2 },
  fieldLbl: { display: "block", fontSize: 10.5, fontWeight: 600, color: C.sub, textTransform: "uppercase",
    letterSpacing: 0.5, marginBottom: 5 },
  ifThen: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: 0, border: `1px solid ${C.line}`,
    borderRadius: 11, overflow: "hidden" },
  ifCol: { padding: "16px 18px", borderRight: `1px solid ${C.lineSoft}` },
  thenCol: { padding: "16px 18px", background: C.bg2 },
  colTag: { fontSize: 11, fontWeight: 700, color: C.accent, letterSpacing: 0.5, marginBottom: 12,
    textTransform: "uppercase" },
  typeCard: { textAlign: "left", background: C.bg2, border: `1px solid ${C.line}`, borderRadius: 9,
    padding: "10px 12px", cursor: "pointer", fontFamily: C.sans, color: C.ink },
  typeCardOn: { borderColor: C.accent, background: "rgba(61,214,163,.1)", boxShadow: `inset 0 0 0 1px ${C.accent}` },
  radio: { width: 12, height: 12, borderRadius: "50%", border: `2px solid ${C.line}`, display: "inline-block" },
  radioOn: { borderColor: C.accent, background: C.accent, boxShadow: `inset 0 0 0 2px ${C.surface}` },
  select: { border: `1px solid ${C.line}`, borderRadius: 7, padding: "7px 10px", fontFamily: C.sans,
    fontSize: 12, background: C.bg2, color: C.ink },
  btnGhost: { background: C.surface2, border: `1px solid ${C.line}`, borderRadius: 8, padding: "8px 16px",
    fontSize: 12.5, cursor: "pointer", color: C.ink, fontFamily: C.sans },
  btnPrimary: { background: C.brand, color: "#fff", border: "none", borderRadius: 8, padding: "8px 18px",
    fontSize: 12.5, fontWeight: 600, cursor: "pointer", fontFamily: C.sans,
    boxShadow: C.name === "dark" ? "0 0 18px rgba(61,214,163,.3)" : "0 2px 8px rgba(64,182,107,.3)" },
  // --- KEV catalog ---
  kevBar: { display: "flex", justifyContent: "space-between", alignItems: "center", gap: 14,
    padding: "13px 22px", borderBottom: `1px solid ${C.lineSoft}` },
  kevSearch: { display: "flex", alignItems: "center", gap: 8, flex: "1 1 360px", maxWidth: 460,
    border: `1px solid ${C.line}`, borderRadius: 9, padding: "7px 12px", background: C.bg2 },
  kevInput: { border: "none", outline: "none", flex: 1, fontFamily: C.sans, fontSize: 13, background: "transparent", color: C.ink },
  kevClear: { background: "none", border: "none", cursor: "pointer", color: C.sub, fontSize: 16, lineHeight: 1, padding: 0 },
  kevEmpty: { padding: "44px 18px", textAlign: "center", color: C.sub, fontSize: 13 },
  // --- catalog search ---
  ecoBtn: { background: C.surface, border: `1px solid ${C.line}`, borderRight: "none",
    borderRadius: "9px 0 0 9px", padding: "10px 14px", fontSize: 12.5, color: C.ink, cursor: "pointer",
    fontFamily: C.sans, whiteSpace: "nowrap", minWidth: 110, textAlign: "left" },
  ecoMenu: { position: "absolute", top: 44, left: 0, zIndex: 20, background: C.surface,
    border: `1px solid ${C.line}`, borderRadius: 10, boxShadow: "0 12px 30px rgba(0,0,0,.3)",
    minWidth: 180, overflow: "hidden", padding: 4 },
  ecoItem: { display: "flex", justifyContent: "space-between", alignItems: "center", width: "100%",
    background: "none", border: "none", padding: "8px 10px", borderRadius: 7, cursor: "pointer",
    color: C.ink, fontFamily: C.sans, fontSize: 12.5 },
  acMenu: { position: "absolute", top: 46, left: 116, right: 110, zIndex: 25, background: C.surface,
    border: `1px solid ${C.line}`, borderRadius: 8, boxShadow: "0 12px 30px rgba(15,39,72,.18)", overflow: "hidden" },
  acItem: { display: "flex", justifyContent: "space-between", alignItems: "center", width: "100%",
    background: "none", border: "none", padding: "9px 14px", cursor: "pointer", color: C.ink,
    fontFamily: C.sans, fontSize: 13, borderBottom: `1px solid ${C.lineSoft}` },
  catInput: { flex: 1, border: `1px solid ${C.line}`, padding: "10px 14px", fontFamily: C.sans,
    fontSize: 13, background: C.surface, color: C.ink, outline: "none" },
  catSearchBtn: { background: C.accent, color: C.name === "dark" ? "#06140f" : "#fff", border: "none",
    borderRadius: "0 9px 9px 0", padding: "0 22px", fontSize: 13, fontWeight: 600, cursor: "pointer",
    fontFamily: C.sans },
  crumb: { display: "flex", alignItems: "center", gap: 8, fontSize: 12, color: C.sub, marginBottom: 4 },
  capPill: { fontSize: 11, color: C.accent, background: `${C.accent}14`, border: `1px solid ${C.accent}40`,
    borderRadius: 20, padding: "3px 11px", fontWeight: 500 },
  sampleChip: { background: C.surface, border: `1px solid ${C.line}`, borderRadius: 20, padding: "4px 12px",
    fontFamily: C.mono, fontSize: 11, cursor: "pointer" },
  featCard: { background: C.surface, border: `1px solid ${C.line}`, borderRadius: 6, padding: "18px 20px",
    display: "flex", flexDirection: "column", minHeight: 200, boxShadow: "0 1px 2px rgba(0,0,0,.04)" },
  body: { display: "grid", gridTemplateColumns: "230px 1fr", minHeight: "calc(100vh - 56px)" },
  nav: { background: C.navbar, padding: "14px 10px",
    display: "flex", flexDirection: "column", gap: 1 },
  navItem: { display: "flex", alignItems: "center", gap: 8, textAlign: "left", background: "none", border: "none", padding: "9px 13px",
    color: "rgba(255,255,255,.82)", cursor: "pointer", fontSize: 13, borderRadius: 4, fontFamily: C.sans, width: "100%",
    transition: "background .1s" },
  navOn: { background: "rgba(95,217,104,.14)", color: "#5fd968", fontWeight: 600, boxShadow: `inset 3px 0 0 ${C.accent}` },
  navGroupHead: { display: "flex", justifyContent: "space-between", alignItems: "center", width: "100%",
    textAlign: "left", background: "none", border: "none", padding: "10px 13px 6px", cursor: "pointer",
    color: "rgba(255,255,255,.45)", fontSize: 10.5, fontWeight: 700, letterSpacing: 0.6, textTransform: "uppercase", fontFamily: C.sans },
  navSub: { textAlign: "left", background: "none", border: "none", padding: "8px 12px 8px 22px",
    color: "rgba(255,255,255,.7)", cursor: "pointer", fontSize: 12.5, borderRadius: 8, fontFamily: C.sans, width: "100%",
    display: "block", transition: "background .12s, color .12s" },
  navSubOn: { background: "rgba(95,217,104,.14)", color: "#5fd968", fontWeight: 600, boxShadow: `inset 3px 0 0 ${C.accent}` },
  save: { background: C.brand, color: "#fff", border: "none", padding: "11px 12px", borderRadius: 4,
    cursor: "pointer", fontSize: 13, fontWeight: 600, marginBottom: 8 },
  navNote: { fontSize: 10.5, color: C.dim, lineHeight: 1.5 },
  main: { padding: "26px 30px", overflow: "auto" },
  card: { background: C.surface, border: `1px solid ${C.line}`, borderRadius: 16, padding: 0, marginBottom: 18,
    boxShadow: "0 2px 14px rgba(15,39,72,.05)", overflow: "hidden" },
  cardHead: { padding: "16px 20px", borderBottom: `1px solid ${C.lineSoft}` },
  h2: { margin: 0, fontSize: 16, fontWeight: 600 },
  desc: { margin: "5px 0 0", color: C.sub, fontSize: 12, maxWidth: 660, lineHeight: 1.5 },
  table: { width: "100%", borderCollapse: "collapse" },
  th: { textAlign: "left", padding: "11px 22px", fontSize: 10.5, color: C.sub, fontWeight: 600,
    textTransform: "uppercase", letterSpacing: 0.7, borderBottom: `1px solid ${C.line}`, background: C.bg2 },
  tr: { borderBottom: `1px solid ${C.lineSoft}` },
  td: { padding: "12px 22px", verticalAlign: "middle" },
  subhead: { padding: "14px 22px 6px", fontSize: 11, fontWeight: 600, color: C.warn,
    textTransform: "uppercase", letterSpacing: 0.6, fontFamily: C.mono },
  input: { border: `1px solid ${C.line}`, borderRadius: 6, padding: "6px 9px", width: 70,
    fontFamily: C.mono, fontSize: 12, textAlign: "right", background: C.bg2, color: C.ink },
  chip: { background: C.lineSoft, border: `1px solid ${C.line}`, borderRadius: 5, padding: "2px 4px 2px 7px",
    fontFamily: C.mono, fontSize: 11, display: "inline-flex", alignItems: "center", gap: 3 },
  chipX: { background: "none", border: "none", cursor: "pointer", color: C.sub, fontSize: 13, padding: 0 },
  callout: { margin: 22, padding: "13px 15px", background: "rgba(245,183,64,.08)", border: `1px solid rgba(245,183,64,.3)`,
    borderRadius: 9, fontSize: 12, color: "#e2c49a", lineHeight: 1.55 },
  code: { fontFamily: C.mono, background: C.lineSoft, padding: "1px 5px", borderRadius: 4, color: C.accent },
  form: { display: "flex", gap: 8, flexWrap: "wrap", padding: 22, borderTop: `1px solid ${C.lineSoft}` },
  formInput: { border: `1px solid ${C.line}`, borderRadius: 7, padding: "9px 11px",
    fontFamily: C.mono, fontSize: 12, flex: "1 1 130px", background: C.bg2, color: C.ink },
  add: { background: C.brand, color: "#fff", border: "none", borderRadius: 8, padding: "9px 16px",
    cursor: "pointer", fontWeight: 600, fontSize: 12 },
  remove: { background: "none", border: `1px solid ${C.line}`, color: C.block, borderRadius: 6,
    padding: "5px 10px", cursor: "pointer", fontSize: 11 },
  covCell: { padding: "4px 10px 4px 0", fontSize: 11.5, verticalAlign: "top" },
  rationale: { whiteSpace: "pre-wrap", fontFamily: C.mono, fontSize: 11.5, lineHeight: 1.6,
    background: C.bg2, border: `1px solid ${C.line}`, borderRadius: 9, padding: "12px 14px",
    margin: 0, color: C.ink, maxWidth: 780 },
};

const FONTS = `@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap');
  ::selection{background:rgba(22,82,212,.16)}
  @keyframes fwpulse{0%,100%{opacity:1}50%{opacity:.4}}
  @keyframes fwfade{from{opacity:0;transform:translateY(4px)}to{opacity:1;transform:none}}
  @keyframes fwslide{from{transform:translateX(24px);opacity:.4}to{transform:none;opacity:1}}`;

const DEMO = {
  policy: { version: "12", cvssBlockThreshold: 7.0, blockKnownExploited: true, epssBlockThreshold: 0.5,
    minPackageAgeDays: 14, maxTreeDepth: 8, licenseBlocklist: ["GPL-3.0", "AGPL-3.0"],
    operationalRiskAction: "Notify", minScorecardScore: 0,
    weights: { safetensorsOnly: true, blockPickle: true, requireHashPin: true },
    enabledSources: ["osv", "kev", "epss", "malware", "artifactory"], requiredSources: ["osv", "malware"], quarantineOnUncertainty: true, enableResearchAgent: true, enableContentScan: true, enableReachability: true, downgradeUnreachable: false,
    watches: [
      { name: "PROD-watch", description: "Production promotion gate", ecosystems: [], enabled: true,
        policyName: "Block-Promotion-On-High-Vulnerability", policyType: "Security", rules: [
        { name: "Block-high-vuln", type: "CVEs", minSeverity: "High", knownExploitedOnly: false, block: true, notify: true },
        { name: "Block-malicious", type: "Malicious", block: true, notify: true } ] },
      { name: "Security-watch", description: "All security findings, notify only (visibility)", ecosystems: [], enabled: true,
        policyName: "Security_policy_1", policyType: "Security", rules: [
        { name: "All-CVEs", type: "CVEs", minSeverity: "Low", knownExploitedOnly: false, block: false, notify: true } ] },
      { name: "License-watch", description: "Prohibited-license enforcement", ecosystems: [], enabled: true,
        policyName: "license-policy", policyType: "License", rules: [
        { name: "Block-prohibited-licenses", type: "License", block: true, notify: true } ] },
    ],
    exceptions: [{ package: "torch==2.4.0", reason: "Approved GPU stack", approvedBy: "J. Mokoena",
      ticket: "SEC-1042", expires: "2026-09-01" }] },
  audit: [
    { id: "1", package: { ecosystem: "PyPI", name: "transformers", version: "4.44.0" },
      decision: "Allow", triggeredRules: [], componentsEvaluated: 41, timestamp: new Date().toISOString(),
      coverage: { allRequiredConclusive: true, gaps: [], sources: [
        { source: "osv", status: "Empty", findingCount: 0, detail: "no vulnerabilities recorded", required: true },
        { source: "kev", status: "Ok", findingCount: 0, detail: null, required: false },
        { source: "epss", status: "Ok", findingCount: 0, detail: null, required: false },
        { source: "vulncheck", status: "NotConfigured", findingCount: 0, detail: "no API key \u2014 licensed feed inactive", required: false } ] },
      researchRationale: "Decision ALLOW for transformers@4.44.0 across 41 components. OSV (required) returned conclusively with no recorded vulnerabilities across the resolved tree; KEV and EPSS confirmed no known-exploited or high-probability entries. Coverage gap: VulnCheck is not licensed, so pre-NVD / zero-day intelligence was NOT consulted \u2014 absence of zero-day findings is therefore unverified, not confirmed. Residual risk: low for known vulnerabilities, indeterminate for pre-disclosure threats. A reviewer relying on this for production (vs production) should confirm against a licensed early-warning feed." },
    { id: "2", package: { ecosystem: "npm", name: "example-pkg", version: "1.2.0" },
      decision: "Block", triggeredRules: ["SEC-VULN-02:KEV:CVE-2024-1111[transitive d2:minimist]"],
      componentsEvaluated: 18, timestamp: new Date().toISOString(),
      coverage: { allRequiredConclusive: true, gaps: [], sources: [
        { source: "osv", status: "Ok", findingCount: 1, detail: null, required: true },
        { source: "kev", status: "Ok", findingCount: 0, detail: null, required: false },
        { source: "epss", status: "Ok", findingCount: 1, detail: null, required: false } ] },
      researchRationale: "Decision BLOCK. A known-exploited vulnerability (CVE-2024-1111) was found at depth 2 in transitive dependency 'minimist', not in the requested package itself \u2014 this is exactly the supply-chain surface a root-only scan would miss. KEV listing makes this a hard block under SEC-VULN-02. Coverage was complete across required sources. No override recommended." },
    { id: "5", package: { ecosystem: "PyPI", name: "obscure-lib", version: "0.0.3" },
      decision: "Quarantine", triggeredRules: ["SEC-COV-02:REQUIRED_SOURCE_INCONCLUSIVE"],
      componentsEvaluated: 4, timestamp: new Date().toISOString(),
      coverage: { allRequiredConclusive: false,
        gaps: ["osv timed out \u2014 its coverage dimension was not verified (REQUIRED for clean allow)"],
        sources: [
        { source: "osv", status: "Timeout", findingCount: 0, detail: "request cancelled/timed out", required: true },
        { source: "kev", status: "Ok", findingCount: 0, detail: null, required: false },
        { source: "epss", status: "Skipped", findingCount: 0, detail: "no CVEs to score", required: false } ] },
      researchRationale: "Decision QUARANTINE \u2014 NOT a clean pass. The required source OSV timed out, so the primary CVE dimension was never verified for this package or its 3 dependencies. Per SEC-COV-02 the package is held rather than allowed, because absence of findings from a failed source is not evidence of safety. Action: re-run once OSV is reachable; do not promote on the strength of the secondary feeds alone." },
    { id: "6", package: { ecosystem: "PyPI", name: "reuests", version: "1.0.0" },
      decision: "Block", triggeredRules: ["SEC-VULN-01:CVSS:MAL-2022-7441", "SEC-VULN-02:KEV:MAL-2022-7441"],
      componentsEvaluated: 1, timestamp: new Date().toISOString(),
      coverage: { allRequiredConclusive: true, gaps: [], sources: [
        { source: "osv", status: "Empty", findingCount: 0, detail: "no CVE \u2014 malware carries none", required: true },
        { source: "malware", status: "Ok", findingCount: 1, detail: "free OpenSSF feed", required: true },
        { source: "socket", status: "NotConfigured", findingCount: 0, detail: "behavioural tier inactive", required: false } ] },
      researchRationale: "Decision BLOCK. 'reuests' is a typosquat of 'requests' flagged by the OpenSSF Malicious Packages feed (MAL-2022-7441). Note the CVE source (OSV) returned EMPTY \u2014 malicious packages carry no CVE, so a CVE-only scanner would have passed this as clean. The malware feed is what caught it, which is why it is set as a required source. Behavioural tier (Socket) not licensed, so install-script analysis was not performed; not needed here given the confirmed-bad listing." },
    { id: "3", package: { ecosystem: "HuggingFace", name: "vendor/model", version: "main" },
      decision: "Block", triggeredRules: ["SEC-AIML-01:PICKLE_DANGEROUS_IMPORT:references os.system"],
      componentsEvaluated: 1, timestamp: new Date().toISOString(),
      coverage: { allRequiredConclusive: true, gaps: [], sources: [
        { source: "weights-scan", status: "Ok", findingCount: 1, detail: null, required: true } ] },
      researchRationale: "Decision BLOCK. Pickle opcode scan found a GLOBAL reference to os.system inside the weight file \u2014 a code-execution vector triggered on model load. Format is not safetensors. Hard block under SEC-AIML-01. No override without converting to safetensors and re-scanning." },
    { id: "4", package: { ecosystem: "NuGet", name: "Serilog", version: "3.1.1" },
      decision: "Allow", triggeredRules: [], componentsEvaluated: 7, timestamp: new Date().toISOString(),
      coverage: { allRequiredConclusive: true, gaps: [], sources: [
        { source: "osv", status: "Empty", findingCount: 0, detail: "no vulnerabilities recorded", required: true },
        { source: "kev", status: "Ok", findingCount: 0, detail: null, required: false },
        { source: "epss", status: "Ok", findingCount: 0, detail: null, required: false } ] },
      researchRationale: "Decision ALLOW for Serilog@3.1.1 across 7 components. Required source conclusive, no findings. Standard low-risk logging dependency." },
  ],
};
