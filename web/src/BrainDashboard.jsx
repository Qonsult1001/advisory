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
  const [tab, setTab] = useState(null); // explore | recall | loop | null

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const b = await loadBrain(API);
        if (!alive) return;
        let st = {};
        try { st = b.stats() || {}; } catch { st = {}; }
        // symbol count + memory count are separate methods, not in stats().
        try { st.__symbols = b.symbol_count(); } catch { st.__symbols = null; }
        try { st.__count = b.count_memories(); } catch { st.__count = null; }
        setBrain(b);
        setStats(st);
        setPhase("ready");
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
    { key: "explore", label: "Explore", sub: "browse stored memories" },
    { key: "recall", label: "Recall", sub: "live semantic search" },
    { key: "loop", label: "Loop activity", sub: "self-healing cycles" },
  ];
  const active = tab || "explore"; // default to a panel so the page is never just stats
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {/* STAT ROW — enterprise cards with accent top-border (matches Overview) */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(6,1fr)", gap: 12 }}>
        <MetricCard C={C} color={C.accent} value={fmt(memories)} label="Memories" sub="code + notes" />
        <MetricCard C={C} color={C.info} value={fmt(symbols)} label="Symbols" sub="classes / methods" />
        <MetricCard C={C} color={C.accentDim} value={ratio ? `${ratio}×` : "—"} label="Compression" sub={indexedMb ? `${indexedMb} MB indexed` : "indexed"} />
        <MetricCard C={C} color={C.warn} value={fmt(deleted ?? 0)} label="Tombstones" sub="superseded" />
        <MetricCard C={C} color={C.info} value={(stats?.mode || "portable")} label="Mode" sub="brain format" small />
        <MetricCard C={C} color={C.accent} value="WASM" label="Engine" sub="in-browser" small />
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
          {active === "explore" && <ExplorePanel C={C} s={s} brain={brain} fmt={fmt} total={memories} />}
          {active === "recall" && <RecallPanel C={C} s={s} brain={brain} />}
          {active === "loop" && <LoopPanel C={C} s={s} API={API} />}
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

// ---- LOOP: per-cycle activity from the existing evolution runs ----
function LoopPanel({ C, s, API }) {
  const [runs, setRuns] = useState(null);
  useEffect(() => {
    fetch(`${API}/evolution/runs`).then((r) => r.json()).then((d) => setRuns(d?.runs || [])).catch(() => setRuns([]));
  }, [API]);
  return (
    <div>
      <PanelHead C={C} title="Loop activity" sub="what the brain drove — recent self-healing mutation cycles" />
      {runs == null && <BrainSpinner C={C} label="loading cycles…" />}
      {runs && runs.length === 0 && <div style={{ fontSize: 12.5, color: C.dim }}>No cycles yet. Run a mutation and the brain’s work shows here.</div>}
      {runs && runs.slice(0, 12).map((r) => (
        <div key={r.id} style={{ display: "flex", gap: 10, alignItems: "center", padding: "9px 0", borderBottom: `1px solid ${C.lineSoft}` }}>
          <StatusDot C={C} status={r.status} />
          <span style={{ fontSize: 12, color: C.sub, minWidth: 48 }}>#{r.ticket}</span>
          <span style={{ fontSize: 12.5, color: C.ink, flex: 1, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{r.ticketTitle || r.stage || r.status}</span>
          <span style={{ fontSize: 11, color: C.dim }}>{r.status}</span>
          {r.prUrl && <a href={r.prUrl} target="_blank" rel="noreferrer" style={{ fontSize: 11.5, color: C.accentDim }}>PR ↗</a>}
        </div>
      ))}
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
