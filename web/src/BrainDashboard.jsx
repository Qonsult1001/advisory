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
const FLOW_STAGES = [
  { key: "plan",   label: "Plan",     prompt: "explore read-only, design the approach, make a todo list", recall: "recalled prior plans + decisions", color: "#1f7fd1" },
  { key: "design", label: "Design",   prompt: "architecture + file-layout conventions",                   recall: "the project's stored conventions", color: "#7b54d1" },
  { key: "code",   label: "Code",     prompt: "surgical edits, follow conventions, don't over-engineer",   recall: "recalled verified patterns + real fixture", color: "#2f9e36" },
  { key: "test",   label: "Test",     prompt: "run build + tests (the gate)",                              recall: "known commands (the Workflow section)", color: "#d99016" },
  { key: "repair", label: "Repair",   prompt: "feed the error back, fix, retry until green",               recall: "stored Errors & Corrections", color: "#d63649" },
  { key: "memory", label: "Memory",   prompt: "record the session — tell the whole story",                 recall: "writes back to the iteration store", color: "#40be46" },
];

// Map a live run's (status, stage, log) → which canvas node is ACTIVE, plus a phase state.
// Mirrors the real MutateStages keys: queued|setup|plan|test|fix|build|tests|pr + statuses.
function mapRunToNode(run) {
  if (!run) return null;
  const status = (run.status || "").toLowerCase();
  const stage = (run.stage || "").toLowerCase();
  const log = (run.log || "").toLowerCase();
  const repairing = /repair attempt|asking groq to fix|re-building after a failure/.test(log);
  // terminal states
  if (status === "released") return { node: "memory", state: "done" };
  if (status === "pr-open") return { node: "memory", state: "active" };
  if (status === "failed" || status === "rejected") {
    // died — light the node it was on, in red
    const n = repairing ? "repair" : stage.includes("build") || stage.includes("test") ? "test" : stage.includes("implement") || status === "running" ? "code" : "plan";
    return { node: n, state: "failed" };
  }
  if (status === "awaiting-approval" || stage.includes("await") || stage.includes("plan")) return { node: "plan", state: "active" };
  if (repairing) return { node: "repair", state: "active" };
  if (stage.includes("test") || status === "tests") return { node: "test", state: "active" };
  if (stage.includes("build")) return { node: "test", state: "active" };
  if (stage.includes("implement") || stage.includes("fix") || status === "running") return { node: "code", state: "active" };
  if (status === "queued" || stage.includes("setup") || stage.includes("queue")) return { node: "plan", state: "queued" };
  return { node: "plan", state: "active" };
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
  const [sel, setSel] = useState(null);
  const [probe, setProbe] = useState({}); // stage.key -> live recall count from the brain
  const [run, setRun] = useState(null);   // the active/most-recent run (live phase)
  // Prove memory really feeds each stage: ask the brain each stage's recall query, count hits.
  useEffect(() => {
    if (!brain) return;
    const out = {};
    for (const st of FLOW_STAGES) {
      try { const r = brain.ask_fused(st.recall, 5, false); out[st.key] = Array.isArray(r) ? r.length : (r?.results?.length || 0); }
      catch { out[st.key] = 0; }
    }
    setProbe(out);
  }, [brain]);

  // LIVE: poll the runs endpoint and follow the active mutation through its phases.
  useEffect(() => {
    let alive = true;
    const tick = () => fetch(`${API}/evolution/runs?limit=5`).then((r) => r.json())
      .then((d) => { if (!alive) return; const rs = d?.runs || []; setRun(rs[0] || null); }).catch(() => {});
    tick();
    const id = setInterval(tick, 2500);
    return () => { alive = false; clearInterval(id); };
  }, [API]);

  const live = mapRunToNode(run);
  const isLive = run && ["queued", "running", "awaiting-approval", "tests"].includes((run.status || "").toLowerCase());

  // Canvas geometry — compact mode shrinks everything so it fits nicely under the run table.
  const W = 920;
  const nodeW = compact ? 100 : 116;
  const nodeH = compact ? 46 : 60;
  const rowY = compact ? 18 : 70;
  const busY = compact ? 132 : 250;
  const H = compact ? 196 : 360;
  const gap = (W - 80 - nodeW) / (FLOW_STAGES.length + 1);
  const xs = (i) => 60 + gap * (i + 1);
  const startX = 16, endX = W - nodeW - 16;
  const stageCenter = (i) => ({ x: xs(i) + nodeW / 2, y: rowY + nodeH / 2 });

  return (
    <div style={{ position: "relative" }}>
      {!compact && <PanelHead C={C} title="Agent flow" sub="the self-healing cycle as a graph — every stage runs on its routed agent and recalls from the brain" />}
      {/* LIVE banner — follows the active mutation through its phases */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: compact ? 8 : 12, fontSize: 12.5 }}>
        <span style={{ width: 9, height: 9, borderRadius: "50%", background: isLive ? C.allow : C.dim,
          boxShadow: isLive ? `0 0 0 0 ${C.allow}` : "none", animation: isLive ? "pulse 1.3s infinite" : "none", display: "inline-block" }} />
        {run ? (
          <span style={{ color: C.sub }}>
            {isLive ? <b style={{ color: C.ink }}>Live</b> : <b style={{ color: C.ink }}>{run.status === "released" ? "Last run" : "Idle"}</b>}
            {" — #"}{run.ticket}: <span style={{ color: C.ink }}>{run.stage || run.status}</span>
            {live && <span style={{ marginLeft: 8, fontSize: 11, fontWeight: 700, color: stageColor(C, live), textTransform: "uppercase" }}>● {live.node}{live.state === "failed" ? " (failed)" : live.state === "done" ? " (done)" : ""}</span>}
          </span>
        ) : <span style={{ color: C.dim }}>No active run — start a mutation and this graph follows it through every phase.</span>}
        <style>{`@keyframes pulse{0%{box-shadow:0 0 0 0 ${C.allow}88}70%{box-shadow:0 0 0 7px ${C.allow}00}100%{box-shadow:0 0 0 0 ${C.allow}00}}`}</style>
      </div>
      <div style={{ display: "grid", gridTemplateColumns: (sel && !compact) ? "1.5fr 1fr" : "1fr", gap: 16 }}>
        <div style={{ border: `1px solid ${C.line}`, borderRadius: 12, background: C.surface2, overflow: "hidden", maxWidth: compact ? 760 : "none" }}>
          <svg width="100%" viewBox={`0 0 ${W} ${H}`} style={{ display: "block" }}>
            <defs>
              <marker id="arrow" markerWidth="9" markerHeight="9" refX="7" refY="3" orient="auto" markerUnits="strokeWidth">
                <path d="M0,0 L7,3 L0,6 Z" fill={C.sub} />
              </marker>
            </defs>

            {/* Start node */}
            <FlowDot C={C} x={startX} y={rowY + nodeH / 2 - 14} label="Start" tone={C.sub} />
            {/* edges: start -> plan, stage -> next stage, last -> end */}
            <Edge C={C} x1={startX + 52} y1={rowY + nodeH / 2} x2={xs(0)} y2={rowY + nodeH / 2} />
            {FLOW_STAGES.map((st, i) => i < FLOW_STAGES.length - 1 && (
              <Edge key={"e" + i} C={C} x1={xs(i) + nodeW} y1={rowY + nodeH / 2} x2={xs(i + 1)} y2={rowY + nodeH / 2} />
            ))}
            <Edge C={C} x1={xs(FLOW_STAGES.length - 1) + nodeW} y1={rowY + nodeH / 2} x2={endX} y2={rowY + nodeH / 2} />
            <FlowDot C={C} x={endX} y={rowY + nodeH / 2 - 14} label="Done" tone={C.accent} />

            {/* Repair loop-back edge: repair (idx 4) curves back to code (idx 2) */}
            {(() => {
              const a = stageCenter(4), b = stageCenter(2);
              const dip = compact ? 30 : 55;
              const d = `M ${a.x} ${rowY + nodeH} C ${a.x} ${rowY + nodeH + dip}, ${b.x} ${rowY + nodeH + dip}, ${b.x} ${rowY + nodeH}`;
              return <g><path d={d} fill="none" stroke="#d63649" strokeWidth="1.6" strokeDasharray="4 3" markerEnd="url(#arrow)" />
                <text x={(a.x + b.x) / 2} y={rowY + nodeH + dip - 3} fontSize="9.5" fill="#d63649" textAnchor="middle">retry until green</text></g>;
            })()}

            {/* Stage nodes */}
            {FLOW_STAGES.map((st, i) => {
              const x = xs(i), on = sel === st.key;
              const liveHere = live && live.node === st.key;
              const liveTone = liveHere ? stageColor(C, live) : null;
              const dim = isLive && !liveHere;   // dim non-active nodes during a live run
              const stroke = liveHere ? liveTone : on ? st.color : C.line;
              return (
                <g key={st.key} style={{ cursor: "pointer", opacity: dim ? 0.5 : 1, transition: "opacity .2s" }} onClick={() => setSel(on ? null : st.key)}>
                  {liveHere && live.state === "active" && (
                    <rect x={x - 3} y={rowY - 3} width={nodeW + 6} height={nodeH + 6} rx="11" fill="none" stroke={liveTone} strokeWidth="2">
                      <animate attributeName="opacity" values="1;0.25;1" dur="1.3s" repeatCount="indefinite" />
                    </rect>
                  )}
                  <rect x={x} y={rowY} width={nodeW} height={nodeH} rx="9" fill={liveHere ? `${liveTone}14` : C.surface}
                    stroke={stroke} strokeWidth={liveHere || on ? 2.2 : 1.2} />
                  <rect x={x} y={rowY} width={nodeW} height="4" rx="2" fill={st.color} />
                  <text x={x + nodeW / 2} y={rowY + nodeH / 2 + 1} fontSize={compact ? 12 : 13} fontWeight="700" fill={C.ink} textAnchor="middle">{st.label}</text>
                  <text x={x + nodeW / 2} y={rowY + nodeH - 8} fontSize="9" fill={liveHere ? liveTone : C.sub} textAnchor="middle" fontWeight={liveHere ? 700 : 400}>
                    {liveHere ? (live.state === "active" ? "● running" : live.state === "failed" ? "✕ failed" : live.state === "done" ? "✓ done" : "queued")
                              : (probe[st.key] != null ? `↑ ${probe[st.key]} recalled` : "…")}
                  </text>
                  {/* memory feed line from the bus up into this node */}
                  <line x1={x + nodeW / 2} y1={busY} x2={x + nodeW / 2} y2={rowY + nodeH} stroke={C.accent} strokeWidth="1" strokeDasharray="3 3" opacity={dim ? 0.25 : 0.55} markerEnd="url(#arrow)" />
                </g>
              );
            })}

            {/* Memory bus (the brain) — a bar under all stages, feeding each */}
            <rect x={50} y={busY} width={W - 100} height={compact ? 38 : 46} rx="10" fill={C.surface} stroke={C.accent} strokeWidth="1.4" />
            <text x={W / 2} y={busY + (compact ? 16 : 20)} fontSize={compact ? 11 : 12} fontWeight="700" fill={C.accentDim} textAnchor="middle">🧠 .said memory — one shared brain</text>
            <text x={W / 2} y={busY + (compact ? 30 : 36)} fontSize="9.5" fill={C.sub} textAnchor="middle">recall-weighted retrieval · verified patterns · errors &amp; corrections · learns from every cycle</text>
          </svg>
        </div>

        {sel && (() => {
          const st = FLOW_STAGES.find((x) => x.key === sel);
          const body = (
            <div style={{ border: `1px solid ${C.line}`, borderRadius: 12, padding: 16, alignSelf: "start",
              ...(compact ? { position: "absolute", top: 40, right: 0, width: 320, background: C.surface, boxShadow: "0 8px 30px rgba(0,0,0,.18)", zIndex: 20 } : {}) }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 10 }}>
                <span style={{ width: 12, height: 12, borderRadius: 3, background: st.color }} />
                <span style={{ fontSize: 15, fontWeight: 700, color: C.ink, flex: 1 }}>{st.label}</span>
                {compact && <button onClick={() => setSel(null)} style={{ background: "none", border: "none", cursor: "pointer", color: C.sub, fontSize: 16, lineHeight: 1 }}>×</button>}
              </div>
              <Row C={C} k="Prompt" v={st.prompt} />
              <Row C={C} k="Recalls from memory" v={st.recall} />
              <Row C={C} k="Live recall hits" v={probe[sel] != null ? `${probe[sel]} memories matched this stage's query` : "…"} />
              {sel === "repair" && <Row C={C} k="Loop" v="on a failed build/test, feeds the compiler error back and retries — the edge that loops to Code" />}
              {sel === "memory" && <Row C={C} k="Writes back" v="records the session so the next cycle recalls it — the brain compounds" />}
            </div>
          );
          return body;
        })()}
      </div>
      {!compact && <div style={{ fontSize: 11.5, color: C.dim, marginTop: 10 }}>Click a stage to see its prompt and what it pulls from the brain. The dashed green lines are memory feeding each stage; the red dashed edge is the self-repair loop.</div>}
      {compact && <div style={{ fontSize: 11, color: C.dim, marginTop: 6 }}>Click a stage for details. Follows your running mutation live.</div>}
    </div>
  );
}
function FlowDot({ C, x, y, label, tone }) {
  return <g><circle cx={x + 16} cy={y + 14} r="14" fill={C.surface} stroke={tone} strokeWidth="1.6" />
    <text x={x + 16} y={y + 18} fontSize="9" fill={tone} textAnchor="middle" fontWeight="700">{label}</text></g>;
}
function Edge({ C, x1, y1, x2, y2 }) {
  return <line x1={x1} y1={y1} x2={x2} y2={y2} stroke={C.sub} strokeWidth="1.5" markerEnd="url(#arrow)" />;
}
function Row({ C, k, v }) {
  return <div style={{ marginBottom: 10 }}>
    <div style={{ fontSize: 10.5, color: C.sub, textTransform: "uppercase", letterSpacing: 0.4, fontWeight: 700 }}>{k}</div>
    <div style={{ fontSize: 12.5, color: C.ink, marginTop: 2, lineHeight: 1.45 }}>{v}</div>
  </div>;
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
