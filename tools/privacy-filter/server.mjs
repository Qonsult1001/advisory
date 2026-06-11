// On-prem PII redaction sidecar — runs openai/privacy-filter DIRECTLY via onnxruntime-node.
//
// transformers.js can't load the custom `openai_privacy_filter` architecture through its pipeline
// helper, so we run the ONNX graph ourselves:
//   1. Tokenize with the model's own o200k tokenizer (loads standalone; decode is byte-reversible,
//      so we recover exact char offsets by accumulating decoded-piece lengths).
//   2. Feed input_ids/attention_mask into the q4f16 ONNX session → per-token logits over 33 BIOES
//      labels (8 PII categories).
//   3. Decode BIOES spans → entities, map token spans to char spans, redact.
//
// The model weights (~0.8 GB q4f16) download once into the mounted /cache volume; after that it
// runs fully offline. Exposes POST /redact {text} and GET /health.
import http from "node:http";
import { existsSync, mkdirSync, createWriteStream } from "node:fs";
import { dirname, join } from "node:path";
import { pipeline as streamPipeline } from "node:stream/promises";
import { AutoTokenizer } from "@huggingface/transformers";
import * as ort from "onnxruntime-node";

const PORT = parseInt(process.env.PORT || "8071", 10) || 8071;
const REPO = process.env.PF_REPO || "openai/privacy-filter";
const ONNX_VARIANT = process.env.PF_ONNX_VARIANT || "model_fp16.onnx"; // standard fp16 ops (ORT 1.21-compatible)
const CACHE = process.env.PF_CACHE || "/cache";
const HF_BASE = "https://huggingface.co";

let tokenizer = null;
let session = null;
let id2label = null;
let loadState = "loading";
let loadError = null;
let loadProgress = "";

// 8 PII categories → our DLP categories.
const CATEGORY_MAP = {
  account_number: { cat: "PII", rule: "ACCOUNT_NUMBER", sev: "High" },
  private_address: { cat: "PII", rule: "ADDRESS", sev: "High" },
  private_date: { cat: "PII", rule: "DATE", sev: "Low" },
  private_email: { cat: "PII", rule: "EMAIL", sev: "Medium" },
  private_person: { cat: "PII", rule: "PERSON_NAME", sev: "High" },
  private_phone: { cat: "PII", rule: "PHONE", sev: "Medium" },
  private_url: { cat: "PII", rule: "URL", sev: "Low" },
  secret: { cat: "Secret", rule: "CREDENTIAL", sev: "High" },
};

async function downloadFile(repoPath, destPath) {
  if (existsSync(destPath)) return;
  mkdirSync(dirname(destPath), { recursive: true });
  const url = `${HF_BASE}/${REPO}/resolve/main/${repoPath}`;
  loadProgress = `downloading ${repoPath}`;
  console.log(`[privacy-filter] ${loadProgress}`);
  const resp = await fetch(url);
  if (!resp.ok) throw new Error(`download ${repoPath}: HTTP ${resp.status}`);
  await streamPipeline(resp.body, createWriteStream(destPath));
}

async function load() {
  try {
    // Tokenizer: loads standalone regardless of the unsupported model architecture.
    loadProgress = "loading tokenizer";
    tokenizer = await AutoTokenizer.from_pretrained(REPO, { cache_dir: CACHE });

    // config.json for id2label (BIOES labels).
    const cfgPath = join(CACHE, "pf-config.json");
    await downloadFile("config.json", cfgPath);
    const cfg = JSON.parse(await (await import("node:fs/promises")).readFile(cfgPath, "utf8"));
    id2label = cfg.id2label;

    // ONNX graph + external weight data. q4f16 ships the graph plus one *_data file.
    const onnxDir = join(CACHE, "pf-onnx");
    const onnxPath = join(onnxDir, ONNX_VARIANT);
    await downloadFile(`onnx/${ONNX_VARIANT}`, onnxPath);
    // External weight data: ORT references these by the exact names the .onnx records, which are
    // "<variant>.onnx_data", "<variant>.onnx_data_1", … Download each until one is absent.
    await downloadFile(`onnx/${ONNX_VARIANT}_data`, `${onnxPath}_data`).catch(() => {});
    for (let i = 1; i <= 6; i++) {
      try { await downloadFile(`onnx/${ONNX_VARIANT}_data_${i}`, `${onnxPath}_data_${i}`); }
      catch { break; } // first missing index ends the chain
    }

    loadProgress = "creating ONNX session";
    console.log(`[privacy-filter] ${loadProgress}`);
    session = await ort.InferenceSession.create(onnxPath);
    loadState = "ready";
    loadProgress = "";
    console.log(`[privacy-filter] ready — inputs=${session.inputNames} outputs=${session.outputNames}`);
  } catch (e) {
    const msg = String(e?.message || e);
    // The model's fused MoE / quantized-gather ops are implemented only for the CUDA and WebGPU
    // execution providers, not the CPU EP that onnxruntime-node uses. On a CPU-only host the graph
    // cannot be executed natively — report that clearly so the gateway uses its fallback engine.
    loadState = /MoE|GatherBlockQuantized|activation_alpha|Unrecognized attribute/.test(msg) ? "cpu-unsupported" : "error";
    loadError = String(e?.stack || e);
    console.error("[privacy-filter] load failed (gateway uses fallback):", e);
  }
}
load();

// Build per-token char spans by accumulating decoded-piece lengths (decode is byte-reversible).
function tokenSpans(ids) {
  const spans = [];
  let pos = 0;
  for (const id of ids) {
    const piece = tokenizer.decode([id], { skip_special_tokens: false });
    spans.push({ start: pos, end: pos + piece.length });
    pos += piece.length;
  }
  return spans;
}

function argmax(arr, off, len) {
  let best = 0, bv = arr[off];
  for (let i = 1; i < len; i++) if (arr[off + i] > bv) { bv = arr[off + i]; best = i; }
  return best;
}

// Decode BIOES label sequence → entity spans (start token, end token, category).
function decodeBioes(labels) {
  const ents = [];
  let cur = null;
  for (let i = 0; i < labels.length; i++) {
    const lab = labels[i];                       // e.g. "B-private_person" | "O" | "S-secret"
    if (lab === "O" || !lab) { if (cur) { ents.push(cur); cur = null; } continue; }
    const dash = lab.indexOf("-");
    const tag = lab.slice(0, dash);              // B I E S
    const type = lab.slice(dash + 1);
    if (tag === "S") { if (cur) { ents.push(cur); cur = null; } ents.push({ type, s: i, e: i }); }
    else if (tag === "B") { if (cur) ents.push(cur); cur = { type, s: i, e: i }; }
    else if (tag === "I" || tag === "E") {
      if (cur && cur.type === type) cur.e = i;
      else { if (cur) ents.push(cur); cur = { type, s: i, e: i }; } // tolerate broken sequences
      if (tag === "E") { ents.push(cur); cur = null; }
    }
  }
  if (cur) ents.push(cur);
  return ents;
}

async function infer(text) {
  const clip = text.length > 8000 ? text.slice(0, 8000) : text;
  const enc = await tokenizer(clip, { return_tensor: false });
  const ids = enc.input_ids;
  const n = ids.length;
  const spans = tokenSpans(ids);

  const inputIds = new ort.Tensor("int64", BigInt64Array.from(ids.map((x) => BigInt(x))), [1, n]);
  const attn = new ort.Tensor("int64", BigInt64Array.from(ids.map(() => 1n)), [1, n]);
  const feeds = {};
  for (const name of session.inputNames) {
    if (name === "input_ids") feeds[name] = inputIds;
    else if (name === "attention_mask") feeds[name] = attn;
    else if (name === "position_ids") feeds[name] = new ort.Tensor("int64", BigInt64Array.from(ids.map((_, i) => BigInt(i))), [1, n]);
  }
  const out = await session.run(feeds);
  const logits = out[session.outputNames[0]];
  const data = logits.data;                       // Float32Array [1*n*33]
  const numLabels = Object.keys(id2label).length;

  const labels = [];
  for (let i = 0; i < n; i++) labels.push(id2label[String(argmax(data, i * numLabels, numLabels))]);

  const ents = decodeBioes(labels);
  return ents.map((e) => {
    let start = spans[e.s].start, end = spans[e.e].end;
    // The o200k tokenizer folds a leading space into the first token; trim surrounding
    // whitespace from the char span so redaction doesn't swallow the preceding space.
    while (start < end && /\s/.test(clip[start])) start++;
    while (end > start && /\s/.test(clip[end - 1])) end--;
    const m = CATEGORY_MAP[e.type] || { cat: "PII", rule: e.type.toUpperCase(), sev: "Medium" };
    return { category: m.cat, rule: m.rule, severity: m.sev, start, end, sample: clip.slice(start, end) };
  }).filter((e) => e.sample.length > 0);
}

function redact(text, ents) {
  let r = text;
  [...ents].sort((a, b) => b.start - a.start).forEach((e) => {
    r = r.slice(0, e.start) + `[${e.rule}:REDACTED]` + r.slice(e.end);
  });
  return r;
}

const server = http.createServer((req, res) => {
  if (req.method === "GET" && req.url === "/health") {
    res.writeHead(loadState === "ready" ? 200 : 503, { "content-type": "application/json" });
    return res.end(JSON.stringify({ state: loadState, model: `${REPO}/${ONNX_VARIANT}`, progress: loadProgress, error: loadError }));
  }
  if (req.method === "POST" && req.url === "/redact") {
    let body = "";
    req.on("data", (c) => { body += c; if (body.length > 2_000_000) req.destroy(); });
    req.on("end", async () => {
      try {
        if (loadState !== "ready") { res.writeHead(503, { "content-type": "application/json" }); return res.end(JSON.stringify({ error: `model ${loadState}`, progress: loadProgress })); }
        const { text } = JSON.parse(body || "{}");
        if (!text) { res.writeHead(400); return res.end(JSON.stringify({ error: "text required" })); }
        const entities = await infer(text);
        res.writeHead(200, { "content-type": "application/json" });
        res.end(JSON.stringify({ entities, redacted: redact(text.length > 8000 ? text.slice(0, 8000) : text, entities) }));
      } catch (e) {
        res.writeHead(500, { "content-type": "application/json" });
        res.end(JSON.stringify({ error: String(e?.stack || e) }));
      }
    });
    return;
  }
  res.writeHead(404); res.end();
});
server.listen(PORT, () => console.log(`[privacy-filter] listening on :${PORT}`));
