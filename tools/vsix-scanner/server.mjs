// Minimal HTTP wrapper around trailofbits/vsix-audit — the real code-level VS Code extension
// security scanner (Discord-webhook exfiltration, SSH/cookie theft, eval/Function/process.binding,
// obfuscation, IOC/C2 + crypto wallets, YARA RAT rules). The .NET API calls this sidecar; it shells
// out to the installed `vsix-audit` CLI and returns its structured JSON.
//
// GET /health                         -> { ok, version }
// GET /scan?id=<publisher.extension>  -> vsix-audit JSON (findings[], inventory, metadata)
//        &registry=marketplace|openvsx|cursor (optional)

import { createServer } from 'node:http';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const exec = promisify(execFile);
const PORT = process.env.PORT || 8099;

// vsix-audit prints a "Downloading…" banner to stdout before the JSON — slice from the first '{'.
function extractJson(stdout) {
  const i = stdout.indexOf('{');
  if (i < 0) throw new Error('no JSON in scanner output');
  return JSON.parse(stdout.slice(i));
}

async function runScan(id, registry) {
  const target = registry && registry !== 'marketplace' ? `${registry}:${id}` : id;
  // vsix-audit EXITS NON-ZERO when it finds issues (exit code reflects worst severity), so a thrown
  // error here still carries valid JSON on stdout. Capture stdout regardless of exit code; only treat
  // it as a real failure when no JSON came back at all.
  let stdout;
  try {
    ({ stdout } = await exec('vsix-audit', ['scan', target, '-o', 'json'], {
      timeout: 90_000, maxBuffer: 64 * 1024 * 1024,
    }));
  } catch (e) {
    if (e && typeof e.stdout === 'string' && e.stdout.includes('{')) stdout = e.stdout;
    else throw e;
  }
  return extractJson(stdout);
}

const ID_RE = /^[A-Za-z0-9][A-Za-z0-9_.-]*\.[A-Za-z0-9][A-Za-z0-9_.-]*$/;

const server = createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const send = (code, obj) => {
    res.writeHead(code, { 'content-type': 'application/json' });
    res.end(JSON.stringify(obj));
  };
  try {
    if (url.pathname === '/health') {
      const { stdout } = await exec('vsix-audit', ['--version'], { timeout: 10_000 });
      return send(200, { ok: true, version: stdout.trim() });
    }
    if (url.pathname === '/scan') {
      const id = (url.searchParams.get('id') || '').trim();
      const registry = (url.searchParams.get('registry') || '').trim();
      if (!ID_RE.test(id)) return send(400, { error: 'invalid extension id (expected publisher.extension)' });
      const result = await runScan(id, registry);
      return send(200, result);
    }
    return send(404, { error: 'not found' });
  } catch (e) {
    // A scanner failure must be reported honestly — never a silent "clean".
    return send(502, { error: 'scan failed', detail: String(e.message || e).slice(0, 500) });
  }
});

server.listen(PORT, () => console.log(`vsix-scanner listening on :${PORT}`));
