// BrainDashboard — the real .said brain, in the browser.
//
// Loads said-wasm (the actual sca-core retrieval engine compiled to WASM), fetches
// the project's Advisory.said + the encoder, and renders an Option-C hero dashboard:
// headline stats → drill-down panels (Explore / Recall / Loop). All data is LIVE
// from the brain itself — no API recall endpoints, no dependency on the CLI binary
// (this reads the finished .said file directly, so the said-0.9.0 index bug doesn't apply).
import React, { useEffect, useState, useRef, useCallback } from "react";

const WASM_BASE = "/said/pkg/said_wasm.js";
const ENCODER = {
  tokenizer: "/said/encoder/tokenizer.json",
  safetensors: "/said/encoder/model.safetensors",
  config: "/said/encoder/config.json",
};

// Lazy singleton: init the wasm module + build the SaidBrain once.
let _modPromise = null;
async function loadBrain(API) {
  if (!_modPromise) {
    _modPromise = (async () => {
      const mod = await import(/* @vite-ignore */ WASM_BASE);
      await mod.default(); // __wbg_init — resolves said_wasm_bg.wasm next to the js
      return mod;
    })();
  }
  const mod = await _modPromise;
  // Fetch the project brain + encoder in parallel.
  const [brainResp, tok, saf, cfg] = await Promise.all([
    fetch(`${API}/admin/context/download`),
    fetch(ENCODER.tokenizer).then((r) => r.arrayBuffer()),
    fetch(ENCODER.safetensors).then((r) => r.arrayBuffer()),
    fetch(ENCODER.config).then((r) => r.arrayBuffer()),
  ]);
  if (!brainResp.ok) {
    const e = new Error("brain-not-built");
    e.code = brainResp.status;
    throw e;
  }
  const bytes = new Uint8Array(await brainResp.arrayBuffer());
  return new mod.SaidBrain(
    bytes,
    "Advisory.said",
    new Uint8Array(tok),
    new Uint8Array(saf),
    new Uint8Array(cfg),
  );
}

const nf = (n) => (n == null ? "—" : Number(n).toLocaleString());

export default function BrainDashboard({ C, s, API, StatTile, Callout, nfmt }) {
  const fmt = nfmt || nf;
  const [brain, setBrain] = useState(null);
  const [stats, setStats] = useState(null);
  const [phase, setPhase] = useState("loading"); // loading | ready | unbuilt | error
  const [err, setErr] = useState("");
  const [tab, setTab] = useState(null);
  const [hist, setHist] = useState(null);   // ticket totals for the stat row
  const [disc, setDisc] = useState(null);   // discover(): pillars / memory_types / top_tags

  useEffect(() => {
    fetch(`${API}/evolution/history`).then((r) => r.json()).then(setHist).catch(() => setHist(null));
  }, [API]);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const b = await loadBrain(API);
        if (!alive) return;
        // WARM UP the brain so its learning state (recall-weights, dream, s_slow) is REAL,
        // not zero — run a handful of real asks tied to what the loop actually does. This is the
        // brain learning from use; the math runs in-browser, nothing leaves the page.
        const WARMUP = [
          "where do endpoints register", "the build and test gate", "operator merge approval",
          "self-repair loop on a failed build", "real test fixture for the cycle",
          "fix replay by fingerprint", "surgical edit via said edit", "first attempt build failure",
        ];
        for (const q of WARMUP) { try { b.ask_fused(q, 5, false); } catch {} }
        try { b.dream(1n); } catch {}   // force a consolidation pass so dream cycle > 0

        let st = {};
        try { st = b.stats() || {}; } catch { st = {}; }
        try { st.__symbols = b.symbol_count(); } catch { st.__symbols = null; }
        try { st.__count = b.count_memories(); } catch { st.__count = null; }
        let d = null; try { d = b.discover(); } catch {}
        if (!alive) return;
        setBrain(b); setStats(st); setDisc(d); setPhase("ready");
      } catch (e) {
        if (!alive) return;
        if (e.message === "brain-not-built") setPhase("unbuilt");
        else { setErr(String(e.message || e)); setPhase("error"); }
      }
    })();
    return () => { alive = false; };
  }, [API]);

  // ---- Stat extraction (real wasm stats() shape: active_memories, total_*_bytes,
  // compression_ratio, audit_event_count; symbols/count come from separate methods) ----
  const memories = stats?.active_memories ?? stats?.active_frames ?? stats?.__count ?? null;
  const symbols = stats?.__symbols ?? null;
  const recalls = stats?.audit_event_count ?? stats?.total_recalls ?? null;
  const dreams = stats?.tombstone_count ?? stats?.dream_cycles ?? null;
  const deleted = stats?.deleted_memories ?? null;
  const fileBytes = stats?.total_uncompressed_bytes ?? stats?.total_compressed_bytes ?? null;
  const ratio = stats?.compression_ratio ? Math.round(stats.compression_ratio * 10) / 10 : null;

  const card = { ...s.card, padding: "18px 20px" };

  if (phase === "loading")
    return <div style={card}><BrainSpinner C={C} label="Booting the brain (loading WASM + encoder)…" /></div>;

  if (phase === "unbuilt")
    return (
      <Callout>
        The <b>.said brain</b> isn’t built yet — it’s created on the next worker/mutation
        cycle. Once it exists, this dashboard fills with live memories, recall, and stats.
      </Callout>
    );

  if (phase === "error")
    return (
      <div style={{ ...card, borderColor: C.block }}>
        <div style={{ color: C.block, fontWeight: 700, marginBottom: 6 }}>Brain failed to load</div>
        <div style={{ fontSize: 12.5, color: C.sub }}>{err}</div>
        <div style={{ fontSize: 11.5, color: C.dim, marginTop: 8 }}>
          The WASM engine reads <code style={s.code}>Advisory.said</code> in your browser. If this
          persists, the brain file or the encoder under <code style={s.code}>/said/</code> may be missing.
        </div>
      </div>
    );

  // ---- READY: enterprise stat row + tab strip + panel ----
  const indexedMb = fileBytes != null ? (fileBytes / 1e6).toFixed(1) : null;
  const TABS = [
    { key: "flow", label: "Flow" },
    { key: "loop", label: "Activity" },
    { key: "composition", label: "Composition" },
    { key: "recall", label: "Recall" },
    { key: "salience", label: "Salience" },
    { key: "explore", label: "Explore" },
  ];
  const active = tab || "flow"; // default to the agent flow — memory + orchestration in one picture
  const tk = hist?.totals || {};
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {/* STAT ROW — meaningful metrics only (no internal jargon) */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(6,1fr)", gap: 12 }}>
        <MetricCard C={C} color={C.info} value={fmt(tk.started ?? "—")} label="Tickets started" sub="self-healing loop" />
        <MetricCard C={C} color={C.accent} value={fmt(tk.closed ?? "—")} label="Tickets completed" sub="closed + shipped" />
        <MetricCard C={C} color={C.accentDim} value={fmt(tk.merged ?? "—")} label="PRs shipped" sub="merged to main" />
        <MetricCard C={C} color={C.accent} value={fmt(memories)} label="Memories" sub="code + notes indexed" />
        <MetricCard C={C} color={C.info} value={fmt(symbols)} label="Symbols" sub="classes / methods" />
        <MetricCard C={C} color={C.accentDim} value={ratio ? `${ratio}×` : "—"} label="Compression" sub={indexedMb ? `${indexedMb} MB` : "indexed"} />
      </div>

      {/* SECTION CARD — tab strip + panel, matching the Overview's card style */}
      <div style={{ ...s.card, marginBottom: 0, overflow: "hidden" }}>
        <div style={{ display: "flex", alignItems: "center", borderBottom: `1px solid ${C.line}`, padding: "0 6px" }}>
          {TABS.map((t) => (
            <button key={t.key} onClick={() => setTab(t.key)} style={{
              background: "transparent", border: "none", cursor: "pointer",
              padding: "14px 18px 12px", fontSize: 13, fontWeight: 600,
              color: active === t.key ? C.ink : C.sub,
              borderBottom: `2px solid ${active === t.key ? C.accent : "transparent"}`,
              marginBottom: -1,
            }}>{t.label}</button>
          ))}
          <div style={{ flex: 1 }} />
          <a href={`${API}/admin/context/download`}
             style={{ ...s.add, background: C.surface, color: C.ink, textDecoration: "none", border: `1px solid ${C.line}`, fontSize: 12, marginRight: 8, padding: "7px 13px" }}>
            ⬇ Download .said
          </a>
        </div>
        <div style={{ padding: "18px 20px" }}>
          {active === "flow" && <FlowCanvas C={C} s={s} brain={brain} API={API} />}
          {active === "loop" && <LoopPanel C={C} s={s} API={API} />}
          {active === "composition" && <CompositionPanel C={C} s={s} disc={disc} stats={stats} fmt={fmt} />}
          {active === "recall" && <RecallPanel C={C} s={s} brain={brain} />}
          {active === "salience" && <SaliencePanel C={C} s={s} brain={brain} />}
          {active === "explore" && <ExplorePanel C={C} s={s} brain={brain} fmt={fmt} total={memories} />}
        </div>
      </div>
    </div>);
}

// Enterprise metric card — clean, with a colored accent top-border (matches the Overview tiles).
function MetricCard({ C, color, value, label, sub, small }) {
  return (
    <div style={{ background: C.surface, border: `1px solid ${C.line}`, borderTop: `3px solid ${color}`, borderRadius: 12, padding: "14px 16px", minWidth: 0 }}>
      <div style={{ fontSize: small ? 16 : 24, fontWeight: 800, color: C.ink, lineHeight: 1.15, letterSpacing: -0.3, textTransform: small ? "capitalize" : "none", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{value}</div>
      <div style={{ fontSize: 10.5, color: C.sub, textTransform: "uppercase", letterSpacing: 0.5, fontWeight: 700, marginTop: 5 }}>{label}</div>
      {sub && <div style={{ fontSize: 11, color: C.dim, marginTop: 2 }}>{sub}</div>}
    </div>
  );
}

// ============================================================================
// FLOW — the mutation cycle AS an agent graph (Beapi-style canvas). Memory feeds
// every stage. This is the "memory + orchestration in one picture" view.
// ============================================================================
// The REAL phases the mutation cycle runs (matches GroqCycle / MutateStages — no fiction).
// `routePhase` = the mutationRouting key that picks this phase's agent (so the drawer can show/edit
// the right agent's model + prompt). `logMatch` = regex over the run log for this phase's terminal slice.
const FLOW_STAGES = [
  { key: "plan",   label: "Plan",   routePhase: "planning",      color: "#1f7fd1",
    prompt: "Plan ONE small, correct code change: the endpoint/behaviour to add, the file(s) to touch, and the test. Minimal, PR-only — no code yet.",
    logMatch: /\[groq\]|\[plan\]|planning|awaiting|approved by operator/i },
  { key: "code",   label: "Code",   routePhase: "execution",     color: "#2f9e36",
    prompt: "Implement the change as SURGICAL edits via said edit (anchored insert/replace, never a whole-file rewrite). Use the real test fixture. Return strict JSON edits.",
    logMatch: /implementing|groq implementing|change set|said edit/i },
  { key: "test",   label: "Build & Test", routePhase: "execution", color: "#d99016",
    prompt: "Build + run tests in the clone (the gate). A change that doesn't compile and pass can never reach a PR.",
    logMatch: /building|testing|build\b|tests?\b|gate/i },
  { key: "repair", label: "Repair", routePhase: "execution",     color: "#d63649",
    prompt: "On a failed build/test, feed the exact compiler/test error back and retry the change — up to N attempts, until green.",
    logMatch: /repair attempt|asking groq to fix|re-building after a failure|didn't build/i },
  { key: "pr",     label: "PR",     routePhase: "documentation", color: "#40be46",
    prompt: "Open a pull request for the green change. PR-only — a human reviews and merges; the cycle never pushes to main.",
    logMatch: /pr opened|opening pull request|pull\/\d+/i },
];

// Map a live run's (status, stage, log) → which node is ACTIVE + its state. Real MutateStages keys.
function mapRunToNode(run) {
  if (!run) return null;
  const status = (run.status || "").toLowerCase();
  const stage = (run.stage || "").toLowerCase();
  const log = (run.log || "").toLowerCase();
  const repairing = /repair attempt|asking groq to fix|re-building after a failure|didn't build/.test(log);
  if (status === "released") return { node: "pr", state: "done" };
  if (status === "pr-open") return { node: "pr", state: "done" };
  if (status === "failed" || status === "rejected") {
    const n = repairing ? "repair" : stage.includes("build") || stage.includes("test") ? "test" : stage.includes("implement") || status === "running" ? "code" : "plan";
    return { node: n, state: "failed" };
  }
  if (status === "awaiting-approval" || stage.includes("await") || (stage.includes("plan") && !stage.includes("implement"))) return { node: "plan", state: "active" };
  if (repairing) return { node: "repair", state: "active" };
  if (stage.includes("test") || stage.includes("build") || status === "tests") return { node: "test", state: "active" };
  if (stage.includes("implement") || stage.includes("fix") || status === "running") return { node: "code", state: "active" };
  if (status === "queued" || stage.includes("setup") || stage.includes("queue")) return { node: "plan", state: "queued" };
  return { node: "plan", state: "active" };
}

// Extract the run-log lines that belong to a given phase (its "terminal" slice).
function phaseLogLines(run, stage) {
  if (!run?.log) return [];
  return run.log.split("\n").map((l) => l.trim()).filter((l) => l && stage.logMatch.test(l));
}

function stageColor(C, live) {
  if (!live) return C.accent;
  if (live.state === "failed") return C.block;
  if (live.state === "done") return C.accent;
  return C.info; // active / queued
}

// Self-contained flow visual for the Mutation page: loads its own brain (best-effort)
// and renders the live agent-flow canvas. Brain is optional — recall counts just stay "…"
// if it isn't available, but the LIVE phase-tracking works regardless.
export function MutationFlow({ C, s, API }) {
  const [brain, setBrain] = useState(null);
  useEffect(() => {
    let alive = true;
    loadBrain(API).then((b) => { if (alive) setBrain(b); }).catch(() => {});
    return () => { alive = false; };
  }, [API]);
  return <FlowCanvas C={C} s={s} brain={brain} API={API} compact />;
}

function FlowCanvas({ C, s, brain, API, compact }) {
  const [sel, setSel] = useState(null);   // selected phase key → opens the drawer
  const [run, setRun] = useState(null);   // the active/most-recent run (live phase)

  // LIVE: poll the runs endpoint and follow the active mutation through its phases.
  useEffect(() => {
    let alive = true;
    const tick = () => fetch(`${API}/evolution/runs?limit=5`).then((r) => r.json())
      .then((d) => { if (!alive) return; setRun((d?.runs || [])[0] || null); }).catch(() => {});
    tick();
    const id = setInterval(tick, 2500);
    return () => { alive = false; clearInterval(id); };
  }, [API]);

  const live = mapRunToNode(run);
  const isLive = run && ["queued", "running", "awaiting-approval", "tests"].includes((run.status || "").toLowerCase());

  // Readable node geometry (no more tiny). 5 real phases.
  const W = 900, nodeW = 124, nodeH = 64, rowY = 24, H = 150;
  const gap = (W - 70 - nodeW) / (FLOW_STAGES.length + 1);
  const xs = (i) => 54 + gap * (i + 1);
  const startX = 12, endX = W - 46;
  const cy = rowY + nodeH / 2;
  const center = (i) => ({ x: xs(i) + nodeW / 2, y: cy });
  const repairIdx = FLOW_STAGES.findIndex((x) => x.key === "repair");
  const codeIdx = FLOW_STAGES.findIndex((x) => x.key === "code");

  return (
    <div style={{ position: "relative" }}>
      {/* LIVE banner */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 10, fontSize: 12.5 }}>
        <span style={{ width: 9, height: 9, borderRadius: "50%", background: isLive ? C.allow : C.dim,
          animation: isLive ? "pulse 1.3s infinite" : "none", display: "inline-block" }} />
        {run ? (
          <span style={{ color: C.sub }}>
            {isLive ? <b style={{ color: C.ink }}>Live</b> : <b style={{ color: C.ink }}>{run.status === "released" || run.status === "pr-open" ? "Last run" : "Idle"}</b>}
            {" — #"}{run.ticket}: <span style={{ color: C.ink }}>{run.stage || run.status}</span>
          </span>
        ) : <span style={{ color: C.dim }}>No active run — start a mutation and this graph follows it through every phase.</span>}
        <div style={{ flex: 1 }} />
        <span style={{ fontSize: 11, color: C.dim }}>each phase is an agent — click to inspect &amp; refine</span>
        <style>{`@keyframes pulse{0%{box-shadow:0 0 0 0 ${C.allow}88}70%{box-shadow:0 0 0 7px ${C.allow}00}100%{box-shadow:0 0 0 0 ${C.allow}00}}`}</style>
      </div>

      <div style={{ border: `1px solid ${C.line}`, borderRadius: 12, background: C.surface2, overflow: "hidden", padding: "4px 0" }}>
        <svg width="100%" viewBox={`0 0 ${W} ${H}`} style={{ display: "block" }}>
          <defs>
            <marker id="arrow" markerWidth="9" markerHeight="9" refX="7" refY="3" orient="auto" markerUnits="strokeWidth">
              <path d="M0,0 L7,3 L0,6 Z" fill={C.sub} />
            </marker>
          </defs>

          <FlowDot C={C} x={startX} y={cy - 14} label="Start" tone={C.sub} />
          <Edge C={C} x1={startX + 48} y1={cy} x2={xs(0)} y2={cy} />
          {FLOW_STAGES.map((st, i) => i < FLOW_STAGES.length - 1 && (
            <Edge key={"e" + i} C={C} x1={xs(i) + nodeW} y1={cy} x2={xs(i + 1)} y2={cy} />
          ))}
          <Edge C={C} x1={xs(FLOW_STAGES.length - 1) + nodeW} y1={cy} x2={endX - 4} y2={cy} />
          <FlowDot C={C} x={endX} y={cy - 14} label="Done" tone={C.accent} />

          {/* Repair → Code loop-back */}
          {(() => {
            const a = center(repairIdx), b = center(codeIdx);
            const d = `M ${a.x} ${rowY + nodeH} C ${a.x} ${rowY + nodeH + 38}, ${b.x} ${rowY + nodeH + 38}, ${b.x} ${rowY + nodeH}`;
            return <g><path d={d} fill="none" stroke="#d63649" strokeWidth="1.6" strokeDasharray="4 3" markerEnd="url(#arrow)" />
              <text x={(a.x + b.x) / 2} y={rowY + nodeH + 36} fontSize="10" fill="#d63649" textAnchor="middle">retry until green</text></g>;
          })()}

          {/* Phase nodes */}
          {FLOW_STAGES.map((st, i) => {
            const x = xs(i), on = sel === st.key;
            const liveHere = live && live.node === st.key;
            const liveTone = liveHere ? stageColor(C, live) : null;
            const dim = isLive && !liveHere;
            const stroke = liveHere ? liveTone : on ? st.color : C.line;
            return (
              <g key={st.key} style={{ cursor: "pointer", opacity: dim ? 0.5 : 1, transition: "opacity .2s" }} onClick={() => setSel(on ? null : st.key)}>
                {liveHere && live.state === "active" && (
                  <rect x={x - 3} y={rowY - 3} width={nodeW + 6} height={nodeH + 6} rx="12" fill="none" stroke={liveTone} strokeWidth="2">
                    <animate attributeName="opacity" values="1;0.25;1" dur="1.3s" repeatCount="indefinite" />
                  </rect>
                )}
                <rect x={x} y={rowY} width={nodeW} height={nodeH} rx="10" fill={liveHere ? `${liveTone}14` : C.surface}
                  stroke={stroke} strokeWidth={liveHere || on ? 2.4 : 1.2} />
                <rect x={x} y={rowY} width={nodeW} height="5" rx="2.5" fill={st.color} />
                <text x={x + nodeW / 2} y={rowY + 30} fontSize="14" fontWeight="700" fill={C.ink} textAnchor="middle">{st.label}</text>
                <text x={x + nodeW / 2} y={rowY + 49} fontSize="10" fill={liveHere ? liveTone : C.dim} textAnchor="middle" fontWeight={liveHere ? 700 : 500}>
                  {liveHere ? (live.state === "active" ? "● running" : live.state === "failed" ? "✕ failed" : live.state === "done" ? "✓ done" : "queued") : "agent"}
                </text>
              </g>
            );
          })}
        </svg>
      </div>
      <div style={{ fontSize: 11, color: C.dim, marginTop: 6 }}>The dashed red edge is the self-repair loop. Click a phase to open its live steps and agent settings.</div>

      {/* RIGHT-SIDE DRAWER — terminal steps + agent config for the clicked phase */}
      {sel && <PhaseDrawer C={C} s={s} API={API} stage={FLOW_STAGES.find((x) => x.key === sel)} run={run} onClose={() => setSel(null)} />}
    </div>
  );
}

// Right-side drawer: live terminal (the phase's slice of the run log) + the agent that runs it
// (model dropdown + editable prompt). Reuses the real mutationRouting → agent + persona config.
function PhaseDrawer({ C, s, API, stage, run, onClose }) {
  const [cfg, setCfg] = useState(null);     // admin settings (agents + mutationRouting)
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  useEffect(() => { fetch(`${API}/admin/settings`).then((r) => r.json()).then(setCfg).catch(() => setCfg(null)); }, []);

  const agentId = cfg?.mutationRouting?.[stage.routePhase];
  const agent = cfg?.agents?.find((a) => a.id === agentId) || cfg?.agents?.find((a) => a.standard === "openai");
  const logLines = phaseLogLines(run, stage);

  const update = (patch) => setCfg((c) => ({ ...c, agents: c.agents.map((a) => a.id === agent.id ? { ...a, ...patch } : a) }));
  const save = async () => {
    setSaving(true); setSaved(false);
    await fetch(`${API}/admin/settings`, { method: "PUT", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ agents: cfg.agents, mutationRouting: cfg.mutationRouting, evolutionRouting: cfg.evolutionRouting,
        memoryMb: cfg.memoryMb || 0, runtime: cfg.runtime, database: cfg.database }) }).catch(() => {});
    setSaving(false); setSaved(true); setTimeout(() => setSaved(false), 2000);
  };

  return (
    <>
      <div onClick={onClose} style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.32)", zIndex: 90 }} />
      <div style={{ position: "fixed", top: 0, right: 0, bottom: 0, width: 460, maxWidth: "92vw", background: C.surface,
        boxShadow: "-8px 0 30px rgba(0,0,0,.2)", zIndex: 91, display: "flex", flexDirection: "column" }}>
        {/* header */}
        <div style={{ display: "flex", alignItems: "center", gap: 10, padding: "16px 18px", borderBottom: `1px solid ${C.line}` }}>
          <span style={{ width: 12, height: 12, borderRadius: 3, background: stage.color }} />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 15, fontWeight: 700, color: C.ink }}>{stage.label}</div>
            <div style={{ fontSize: 11, color: C.sub }}>agent phase · routed via <b>{stage.routePhase}</b></div>
          </div>
          <button onClick={onClose} style={{ background: "none", border: "none", cursor: "pointer", color: C.sub, fontSize: 20, lineHeight: 1 }}>×</button>
        </div>

        <div style={{ overflowY: "auto", padding: 18, display: "flex", flexDirection: "column", gap: 18 }}>
          {/* TERMINAL — this phase's live steps from the run log */}
          <div>
            <div style={{ fontSize: 11, fontWeight: 700, color: C.sub, textTransform: "uppercase", letterSpacing: 0.4, marginBottom: 6 }}>Live steps {run ? `· #${run.ticket}` : ""}</div>
            <pre style={{ margin: 0, background: "#0f1722", color: "#cfe6d4", borderRadius: 8, padding: "12px 14px", fontSize: 11.5,
              lineHeight: 1.6, fontFamily: C.mono, maxHeight: 200, overflow: "auto", whiteSpace: "pre-wrap" }}>
              {logLines.length ? logLines.map((l, i) => `▸ ${l}`).join("\n") : "— no activity for this phase yet —\nStart a mutation; this fills with the real steps as the phase runs."}
            </pre>
          </div>

          {/* WHAT THIS PHASE DOES */}
          <div>
            <div style={{ fontSize: 11, fontWeight: 700, color: C.sub, textTransform: "uppercase", letterSpacing: 0.4, marginBottom: 4 }}>What it does</div>
            <div style={{ fontSize: 12.5, color: C.ink, lineHeight: 1.5 }}>{stage.prompt}</div>
          </div>

          {/* AGENT CONFIG — model + editable prompt (the real config) */}
          <div style={{ borderTop: `1px solid ${C.line}`, paddingTop: 16 }}>
            <div style={{ fontSize: 11, fontWeight: 700, color: C.sub, textTransform: "uppercase", letterSpacing: 0.4, marginBottom: 8 }}>Agent — model &amp; prompt</div>
            {!cfg && <BrainSpinner C={C} label="loading agent config…" />}
            {cfg && !agent && <div style={{ fontSize: 12, color: C.dim }}>No agent routed to this phase yet.</div>}
            {cfg && agent && (
              <>
                <div style={{ fontSize: 12, color: C.sub, marginBottom: 4 }}>Agent</div>
                <div style={{ fontSize: 13, fontWeight: 600, color: C.ink, marginBottom: 10 }}>{agent.id} <span style={{ color: C.dim, fontWeight: 400 }}>({agent.standard})</span></div>
                <div style={{ fontSize: 12, color: C.sub, marginBottom: 4 }}>Model</div>
                <input value={agent.model || ""} onChange={(e) => update({ model: e.target.value })}
                  style={{ width: "100%", border: `1px solid ${C.line}`, borderRadius: 7, padding: "7px 10px", fontSize: 12.5, fontFamily: C.mono, boxSizing: "border-box", marginBottom: 12 }} />
                <div style={{ fontSize: 12, color: C.sub, marginBottom: 4 }}>System prompt (persona)</div>
                <textarea value={agent.persona || ""} onChange={(e) => update({ persona: e.target.value })} rows={5}
                  style={{ width: "100%", border: `1px solid ${C.line}`, borderRadius: 7, padding: "8px 10px", fontSize: 12, fontFamily: C.sans, resize: "vertical", boxSizing: "border-box" }} />
                <div style={{ display: "flex", gap: 10, alignItems: "center", marginTop: 12 }}>
                  <button onClick={save} disabled={saving} style={{ ...s.btnPrimary, opacity: saving ? 0.6 : 1 }}>{saving ? "Saving…" : "Save agent"}</button>
                  {saved && <span style={{ fontSize: 12, color: C.accentDim }}>✓ saved</span>}
                  <span style={{ fontSize: 11, color: C.dim }}>applies to the next run</span>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </>
  );
}

function FlowDot({ C, x, y, label, tone }) {
  return <g><circle cx={x + 16} cy={y + 14} r="15" fill={C.surface} stroke={tone} strokeWidth="1.6" />
    <text x={x + 16} y={y + 18} fontSize="9" fill={tone} textAnchor="middle" fontWeight="700">{label}</text></g>;
}
function Edge({ C, x1, y1, x2, y2 }) {
  return <line x1={x1} y1={y1} x2={x2} y2={y2} stroke={C.sub} strokeWidth="1.5" markerEnd="url(#arrow)" />;
}

// ============================================================================
// COMPOSITION — discover(): what's actually in the brain (pillars, types, tags)
// ============================================================================
function CompositionPanel({ C, s, disc, stats, fmt }) {
  if (!disc) return <div style={{ fontSize: 12.5, color: C.dim }}>Composition unavailable.</div>;
  const pillars = Object.entries(disc.pillars || {});
  const types = Object.entries(disc.memory_types || {});
  const tags = (disc.top_tags || []).slice(0, 10);
  const total = stats?.active_memories || tags.reduce((a, [, n]) => Math.max(a, n), 1) || 1;
  const Bar = ([name, n]) => {
    const pct = Math.min(100, Math.round((n / total) * 100));
    return (
      <div key={name} style={{ marginBottom: 8 }}>
        <div style={{ display: "flex", justifyContent: "space-between", fontSize: 12, color: C.ink, marginBottom: 3 }}>
          <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", maxWidth: "78%", fontFamily: String(name).includes(":") ? C.mono : C.sans }}>{name}</span>
          <b>{fmt(n)}</b>
        </div>
        <div style={{ height: 7, background: C.line, borderRadius: 4, overflow: "hidden" }}>
          <div style={{ width: `${pct}%`, height: "100%", background: C.accent }} />
        </div>
      </div>
    );
  };
  return (
    <div>
      <PanelHead C={C} title="What's in the brain" sub="the live composition of the .said memory — pillars, memory types, and the dominant tags" />
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 20 }}>
        <div>
          <div style={{ fontSize: 12.5, fontWeight: 700, color: C.ink, marginBottom: 8 }}>Memory pillars</div>
          {pillars.length ? pillars.map(Bar) : <div style={{ fontSize: 12, color: C.dim }}>All frames are Code (a code-init brain).</div>}
          <div style={{ fontSize: 12.5, fontWeight: 700, color: C.ink, margin: "16px 0 8px" }}>Memory types</div>
          {types.length ? types.map(Bar) : <div style={{ fontSize: 12, color: C.dim }}>AST-chunked code frames (method / class declarations).</div>}
        </div>
        <div>
          <div style={{ fontSize: 12.5, fontWeight: 700, color: C.ink, marginBottom: 8 }}>Top tags</div>
          {tags.map(Bar)}
        </div>
      </div>
    </div>
  );
}

// ============================================================================
// SALIENCE — live interactive: score how important the brain thinks text is
// ============================================================================
function SaliencePanel({ C, s, brain }) {
  const [text, setText] = useState("operator approved the merge of a green build that fixed the endpoint");
  const [res, setRes] = useState(null);
  const run = useCallback(() => {
    try { setRes(brain.salience_score(text, null)); } catch (e) { setRes({ error: String(e.message || e) }); }
  }, [text, brain]);
  useEffect(() => { run(); }, []); // score the default on open
  const band = res?.band, score = res?.score;
  const bandColor = band === "high" ? C.allow : band === "medium" ? C.warn : C.dim;
  return (
    <div>
      <PanelHead C={C} title="Salience" sub="how important does the brain rate a memory? — the heuristic that decides what's worth keeping vs skipping" />
      <textarea value={text} onChange={(e) => setText(e.target.value)} rows={3}
        style={{ width: "100%", border: `1px solid ${C.line}`, borderRadius: 8, padding: "10px 12px", fontSize: 12.5, fontFamily: C.sans, resize: "vertical", boxSizing: "border-box" }} />
      <div style={{ display: "flex", gap: 10, marginTop: 10, alignItems: "center" }}>
        <button onClick={run} style={{ ...s.btnPrimary }}>Score it</button>
        {res && !res.error && (
          <div style={{ display: "flex", gap: 16, alignItems: "center" }}>
            <span style={{ fontSize: 26, fontWeight: 800, color: bandColor }}>{score}</span>
            <span style={{ fontSize: 12, fontWeight: 700, color: bandColor, textTransform: "uppercase", border: `1px solid ${bandColor}`, borderRadius: 999, padding: "2px 10px" }}>{band}</span>
            <span style={{ fontSize: 12.5, color: C.sub }}>recommendation: <b style={{ color: C.ink }}>{res.recommendation}</b></span>
          </div>
        )}
        {res?.error && <span style={{ fontSize: 12, color: C.block }}>{res.error}</span>}
      </div>
      {res?.tags && <div style={{ marginTop: 10, display: "flex", gap: 6 }}>{res.tags.map((t) => <span key={t} style={{ fontSize: 11, background: C.surface2, border: `1px solid ${C.line}`, borderRadius: 999, padding: "3px 10px", color: C.sub, fontFamily: C.mono }}>{t}</span>)}</div>}
      <div style={{ fontSize: 11.5, color: C.dim, marginTop: 12 }}>This is the live <code style={s.code}>salience_score</code> running in your browser — the same heuristic the brain uses to decide whether a memory is worth storing.</div>
    </div>
  );
}

function BrainSpinner({ C, label }) {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 10, color: C.sub, fontSize: 13 }}>
      <span style={{
        width: 14, height: 14, border: `2px solid ${C.line}`, borderTopColor: C.accent,
        borderRadius: "50%", display: "inline-block", animation: "spin 0.8s linear infinite",
      }} />
      <span>{label}</span>
      <style>{`@keyframes spin{to{transform:rotate(360deg)}}`}</style>
    </div>
  );
}

// ---- EXPLORE: paginated real memory list + detail ----
function ExplorePanel({ C, s, brain, fmt, total }) {
  const PAGE = 25;
  const [offset, setOffset] = useState(0);
  const [rows, setRows] = useState([]);
  const [sel, setSel] = useState(null);
  const [detail, setDetail] = useState(null);

  useEffect(() => {
    try { setRows(brain.list_memories(offset, PAGE) || []); } catch { setRows([]); }
  }, [brain, offset]);

  const open = useCallback((id) => {
    setSel(id);
    try { setDetail(brain.get_memory(id)); } catch { setDetail({ error: "could not load" }); }
  }, [brain]);

  return (
    <div>
      <PanelHead C={C} title="Explore memories" sub={`${fmt(total)} stored — click any to read it`} />
      <div style={{ display: "grid", gridTemplateColumns: detail ? "1.3fr 1fr" : "1fr", gap: 14 }}>
        <div>
          <div style={{ maxHeight: 380, overflow: "auto", border: `1px solid ${C.line}`, borderRadius: 10 }}>
            {rows.length === 0 && <div style={{ padding: 14, color: C.dim, fontSize: 12.5 }}>No memories on this page.</div>}
            {rows.map((m, i) => {
              const id = m.doc_id || m.id || String(i);
              const kind = m.kind || m.tag || (id.includes("fixcase") ? "fixcase" : "frame");
              const title = m.name || m.title || shorten(m.preview || m.content || id, 60);
              return (
                <div key={id} onClick={() => open(id)} style={{
                  padding: "9px 12px", borderBottom: `1px solid ${C.lineSoft}`, cursor: "pointer",
                  background: sel === id ? C.surface2 : "transparent", display: "flex", gap: 10, alignItems: "center",
                }}>
                  <KindChip C={C} kind={kind} />
                  <span style={{ fontSize: 12.5, color: C.ink, flex: 1, fontFamily: kind === "frame" ? C.mono : C.sans, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{title}</span>
                  {m.salience != null && <SalienceBar C={C} v={m.salience} />}
                </div>
              );
            })}
          </div>
          <div style={{ display: "flex", gap: 8, marginTop: 10, alignItems: "center" }}>
            <PageBtn C={C} disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - PAGE))}>← Prev</PageBtn>
            <span style={{ fontSize: 11.5, color: C.dim }}>{offset + 1}–{offset + rows.length} of {fmt(total)}</span>
            <PageBtn C={C} disabled={offset + PAGE >= (total || 0)} onClick={() => setOffset(offset + PAGE)}>Next →</PageBtn>
          </div>
        </div>
        {detail && (
          <div style={{ border: `1px solid ${C.line}`, borderRadius: 10, padding: 12, maxHeight: 430, overflow: "auto" }}>
            <div style={{ fontSize: 11, color: C.dim, marginBottom: 6, fontFamily: C.mono, wordBreak: "break-all" }}>{sel}</div>
            <pre style={{ margin: 0, fontSize: 11.5, lineHeight: 1.5, whiteSpace: "pre-wrap", wordBreak: "break-word", color: C.ink, fontFamily: C.mono }}>
              {detail?.content || detail?.body || detail?.error || JSON.stringify(detail, null, 2)}
            </pre>
          </div>
        )}
      </div>
    </div>
  );
}

// ---- RECALL: live semantic ask against the real engine ----
function RecallPanel({ C, s, brain }) {
  const [q, setQ] = useState("");
  const [hits, setHits] = useState(null);
  const [busy, setBusy] = useState(false);
  const inputRef = useRef(null);

  const run = useCallback(() => {
    const query = q.trim();
    if (!query) return;
    setBusy(true);
    // Fused 3-engine ask (sym+grep+SCA) — the same path `said ask` uses.
    setTimeout(() => {
      try {
        const out = brain.ask_fused(query, 8, false);
        setHits(Array.isArray(out) ? out : out?.results || []);
      } catch (e) {
        try { setHits(brain.search(query, 8) || []); } // fallback: lexical
        catch { setHits([]); }
      }
      setBusy(false);
    }, 0);
  }, [q, brain]);

  return (
    <div>
      <PanelHead C={C} title="Recall" sub="ask the brain — live semantic search (sym + grep + SCA fusion)" />
      <div style={{ display: "flex", gap: 8 }}>
        <input ref={inputRef} value={q} onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => e.key === "Enter" && run()}
          placeholder="e.g. where do endpoints register · the build/test gate · operator merge approval"
          style={{ ...s.input, flex: 1 }} />
        <button onClick={run} disabled={busy} style={{ ...s.btnPrimary, opacity: busy ? 0.6 : 1 }}>{busy ? "…" : "Recall"}</button>
      </div>
      <div style={{ marginTop: 12 }}>
        {hits == null && <div style={{ fontSize: 12.5, color: C.dim }}>Type a question and hit Recall — results come straight from the brain, no LLM.</div>}
        {hits && hits.length === 0 && <div style={{ fontSize: 12.5, color: C.dim }}>No matches above threshold.</div>}
        {hits && hits.map((h, i) => (
          <div key={i} style={{ border: `1px solid ${C.line}`, borderRadius: 10, padding: "10px 12px", marginBottom: 8 }}>
            <div style={{ display: "flex", gap: 8, alignItems: "center", marginBottom: 4 }}>
              <span style={{ fontSize: 11, fontFamily: C.mono, color: C.dim, flex: 1, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{h.doc_id || h.id || h.name || `result ${i + 1}`}</span>
              {(h.score != null || h.similarity != null) && <ScoreChip C={C} v={h.score ?? h.similarity} />}
            </div>
            <pre style={{ margin: 0, fontSize: 11.5, lineHeight: 1.5, whiteSpace: "pre-wrap", wordBreak: "break-word", color: C.ink, fontFamily: C.mono, maxHeight: 150, overflow: "auto" }}>
              {shorten(h.content || h.preview || h.snippet || "", 600)}
            </pre>
          </div>
        ))}
      </div>
    </div>
  );
}

// ---- LOOP: REAL ticket history — graph (started/completed/merged per day) + activity feed ----
function LoopPanel({ C, s, API }) {
  const [hist, setHist] = useState(null);
  useEffect(() => {
    fetch(`${API}/evolution/history`).then((r) => r.json()).then(setHist).catch(() => setHist({ enabled: false }));
  }, [API]);

  if (hist == null) return <BrainSpinner C={C} label="loading ticket history…" />;
  if (!hist.enabled) return <div style={{ fontSize: 12.5, color: C.dim }}>Ticket history is unavailable (no repo configured).</div>;

  const t = hist.totals || { started: 0, closed: 0, merged: 0 };
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 18 }}>
      <PanelHead C={C} title="Loop activity" sub="what the self-healing loop has done — tickets started, completed, and shipped" />

      {/* headline counters */}
      <div style={{ display: "flex", gap: 22, flexWrap: "wrap" }}>
        <Counter C={C} color={C.info} value={t.started} label="Tickets started" />
        <Counter C={C} color={C.accent} value={t.closed} label="Tickets completed" />
        <Counter C={C} color={C.accentDim} value={t.merged} label="PRs shipped" />
      </div>

      {/* interactive grouped-bar timeline */}
      <TicketTimeline C={C} days={hist.days || []} />

      {/* activity feed — real tickets, started/completed (replaces "tombstones") */}
      <div>
        <div style={{ fontSize: 12.5, fontWeight: 700, color: C.ink, marginBottom: 8 }}>Recent tickets</div>
        <div style={{ border: `1px solid ${C.line}`, borderRadius: 10, overflow: "hidden" }}>
          {(hist.recent || []).map((r, i) => (
            <div key={r.number} style={{ display: "flex", gap: 10, alignItems: "center", padding: "9px 12px", borderBottom: i < (hist.recent.length - 1) ? `1px solid ${C.lineSoft}` : "none" }}>
              <span style={{ width: 9, height: 9, borderRadius: "50%", background: r.state === "CLOSED" || r.closedAt ? C.accent : C.warn, display: "inline-block", flexShrink: 0 }} />
              <span style={{ fontSize: 12, color: C.sub, minWidth: 42, fontFamily: C.mono }}>#{r.number}</span>
              <span style={{ fontSize: 12.5, color: C.ink, flex: 1, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{r.title}</span>
              <span style={{ fontSize: 11, fontWeight: 600, color: r.closedAt ? C.accentDim : C.warn }}>{r.closedAt ? "completed" : "started"}</span>
              <span style={{ fontSize: 11, color: C.dim, minWidth: 70, textAlign: "right" }}>{(r.closedAt || r.createdAt || "").slice(0, 10)}</span>
            </div>
          ))}
          {(hist.recent || []).length === 0 && <div style={{ padding: 14, fontSize: 12.5, color: C.dim }}>No tickets yet.</div>}
        </div>
      </div>
    </div>
  );
}

// Interactive grouped-bar timeline: per day, three bars (started / completed / merged) with hover tooltips.
function TicketTimeline({ C, days }) {
  const [hover, setHover] = useState(null); // {day, x, info}
  if (!days.length) return <div style={{ fontSize: 12.5, color: C.dim }}>No history to chart yet.</div>;
  const W = 640, H = 200, padL = 28, padB = 28, padT = 10;
  const max = Math.max(1, ...days.flatMap((d) => [d.started, d.closed, d.merged]));
  const groupW = (W - padL) / days.length;
  const barW = Math.min(14, (groupW - 8) / 3);
  const series = [
    { key: "started", color: C.info, label: "Started" },
    { key: "closed", color: C.accent, label: "Completed" },
    { key: "merged", color: C.accentDim, label: "Shipped" },
  ];
  const y = (v) => padT + (1 - v / max) * (H - padT - padB);
  return (
    <div style={{ border: `1px solid ${C.line}`, borderRadius: 10, padding: "14px 16px 8px", position: "relative" }}>
      <div style={{ display: "flex", gap: 16, marginBottom: 8 }}>
        {series.map((sd) => (
          <span key={sd.key} style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 11.5, color: C.sub }}>
            <span style={{ width: 10, height: 10, borderRadius: 2, background: sd.color, display: "inline-block" }} /> {sd.label}
          </span>
        ))}
      </div>
      <svg width="100%" viewBox={`0 0 ${W} ${H}`} style={{ display: "block" }}>
        {/* gridlines */}
        {[0, 0.5, 1].map((g) => {
          const yy = padT + (1 - g) * (H - padT - padB);
          return <g key={g}>
            <line x1={padL} y1={yy} x2={W} y2={yy} stroke={C.lineSoft} strokeWidth="1" />
            <text x={4} y={yy + 3} fontSize="9" fill={C.dim}>{Math.round(max * g)}</text>
          </g>;
        })}
        {days.map((d, i) => {
          const gx = padL + i * groupW + (groupW - barW * 3) / 2;
          return (
            <g key={d.day}>
              {series.map((sd, j) => {
                const v = d[sd.key] || 0;
                const bh = (v / max) * (H - padT - padB);
                return (
                  <rect key={sd.key} x={gx + j * barW} y={H - padB - bh} width={barW - 2} height={bh}
                    fill={sd.color} rx="2"
                    onMouseEnter={() => setHover({ day: d.day, x: gx + barW * 1.5, info: d })}
                    onMouseLeave={() => setHover(null)}
                    style={{ cursor: "pointer", opacity: hover && hover.day !== d.day ? 0.45 : 1, transition: "opacity .1s" }} />
                );
              })}
              <text x={gx + barW * 1.5} y={H - 8} fontSize="9" fill={C.dim} textAnchor="middle">{d.day.slice(5)}</text>
            </g>
          );
        })}
      </svg>
      {hover && (
        <div style={{ position: "absolute", left: `${(hover.x / W) * 100}%`, top: 36, transform: "translateX(-50%)",
          background: C.ink, color: "#fff", borderRadius: 8, padding: "7px 11px", fontSize: 11, pointerEvents: "none", whiteSpace: "nowrap", zIndex: 5 }}>
          <div style={{ fontWeight: 700, marginBottom: 3 }}>{hover.day}</div>
          <div>Started: <b>{hover.info.started}</b></div>
          <div>Completed: <b>{hover.info.closed}</b></div>
          <div>Shipped: <b>{hover.info.merged}</b></div>
        </div>
      )}
    </div>
  );
}

function Counter({ C, color, value, label }) {
  return (
    <div>
      <div style={{ fontSize: 26, fontWeight: 800, color, lineHeight: 1.1 }}>{Number(value || 0).toLocaleString()}</div>
      <div style={{ fontSize: 11, color: C.sub, textTransform: "uppercase", letterSpacing: 0.4, fontWeight: 600, marginTop: 2 }}>{label}</div>
    </div>
  );
}

// ---- small shared bits ----
function PanelHead({ C, title, sub }) {
  return (
    <div style={{ marginBottom: 12 }}>
      <div style={{ fontSize: 14, fontWeight: 700, color: C.ink }}>{title}</div>
      <div style={{ fontSize: 11.5, color: C.sub, marginTop: 2 }}>{sub}</div>
    </div>
  );
}
function KindChip({ C, kind }) {
  const tone = kind === "fixcase" ? C.allow : kind === "frame" ? C.info : C.warn;
  return <span style={{ fontSize: 10, fontWeight: 700, color: tone, background: C.surface2, border: `1px solid ${C.line}`, borderRadius: 6, padding: "2px 7px", textTransform: "uppercase", letterSpacing: 0.4 }}>{kind}</span>;
}
function SalienceBar({ C, v }) {
  const pct = Math.max(0, Math.min(1, Number(v) || 0));
  return <span title={`salience ${pct.toFixed(2)}`} style={{ width: 34, height: 5, background: C.line, borderRadius: 3, overflow: "hidden", display: "inline-block" }}>
    <span style={{ display: "block", width: `${pct * 100}%`, height: "100%", background: C.accent }} />
  </span>;
}
function ScoreChip({ C, v }) {
  return <span style={{ fontSize: 11, fontWeight: 700, color: C.accentDim, background: C.surface2, borderRadius: 6, padding: "1px 7px" }}>{(Number(v) || 0).toFixed(2)}</span>;
}
function PageBtn({ C, disabled, onClick, children }) {
  return <button onClick={onClick} disabled={disabled} style={{ background: C.surface, color: disabled ? C.dim : C.ink, border: `1px solid ${C.line}`, borderRadius: 8, padding: "5px 12px", fontSize: 12, cursor: disabled ? "default" : "pointer", opacity: disabled ? 0.6 : 1 }}>{children}</button>;
}
function StatusDot({ C, status }) {
  const tone = status === "released" ? C.allow : status === "failed" || status === "rejected" ? C.block : status === "pr-open" ? C.info : C.warn;
  return <span style={{ width: 8, height: 8, borderRadius: "50%", background: tone, display: "inline-block" }} />;
}
function shorten(str, n) { str = String(str || ""); return str.length > n ? str.slice(0, n) + "…" : str; }
