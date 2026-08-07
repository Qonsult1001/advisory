#!/usr/bin/env node
/**
 * Advisory privacy hook for Cursor (and any editor with a compatible hook contract).
 *
 * Cursor runs this script BEFORE it sends a prompt / file to its AI backend. We take the text, send it to
 * the Advisory privacy-filter (/api/dlp/redact), and hand Cursor back the REDACTED text — so POPIA / PCI /
 * secrets are stripped before anything leaves the machine, even though Cursor's own network traffic can't
 * be intercepted. The model has no say: the hook runs unconditionally and Cursor uses what it returns.
 *
 * Wired for two events (see hooks.json):
 *   • beforeSubmitPrompt  — redacts the prompt text the developer typed.
 *   • beforeReadFile      — redacts a file's contents before they're attached to the LLM request.
 *
 * Contract: JSON on stdin → JSON on stdout. We return the redacted content and permission "allow".
 * Fail-safe: if the filter is unreachable we DENY (fail closed) so unredacted data never slips through —
 * set ADVISORY_FAIL_OPEN=1 to instead allow-through on outage (not recommended for POPIA/PCI).
 *
 * Config via env (IT sets these in the pushed Cursor config or the machine environment):
 *   ADVISORY_REDACT_URL   default http://localhost:5000/api/dlp/redact
 *                         (on the published box: https://pacman.<host>/api/dlp/redact via the console proxy,
 *                          or the direct API URL — anything that reaches /api/dlp/redact)
 *   ADVISORY_FAIL_OPEN    "1" to allow-through when the filter is down (default: fail closed = deny)
 */
import http from 'node:http';
import https from 'node:https';

const URL_STR = process.env.ADVISORY_REDACT_URL || 'http://localhost:5000/api/dlp/redact';
const FAIL_OPEN = process.env.ADVISORY_FAIL_OPEN === '1';

function readStdin() {
  return new Promise((resolve) => {
    let d = '';
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', (c) => (d += c));
    process.stdin.on('end', () => resolve(d));
    // If nothing arrives promptly, don't hang the editor.
    setTimeout(() => resolve(d), 4000);
  });
}

function redact(text) {
  return new Promise((resolve, reject) => {
    const u = new URL(URL_STR);
    const lib = u.protocol === 'https:' ? https : http;
    const body = JSON.stringify({ text });
    const req = lib.request(
      u,
      { method: 'POST', headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) }, timeout: 5000 },
      (res) => {
        let d = '';
        res.on('data', (c) => (d += c));
        res.on('end', () => {
          try { resolve(JSON.parse(d)); } catch (e) { reject(e); }
        });
      }
    );
    req.on('error', reject);
    req.on('timeout', () => { req.destroy(new Error('redact timeout')); });
    req.write(body);
    req.end();
  });
}

(async () => {
  let input = {};
  try { input = JSON.parse(await readStdin() || '{}'); } catch { input = {}; }

  // Pull the text to scan depending on the event. beforeSubmitPrompt → prompt; beforeReadFile → content.
  const original =
    (typeof input.prompt === 'string' && input.prompt) ||
    (typeof input.content === 'string' && input.content) ||
    (typeof input.file_content === 'string' && input.file_content) ||
    '';

  if (!original) { process.stdout.write(JSON.stringify({ continue: true, permission: 'allow' })); return; }

  try {
    const r = await redact(original);
    const out = { continue: true, permission: 'allow' };
    // Return the redacted text in the field Cursor reads back for this event.
    if (typeof input.prompt === 'string') out.prompt = r.redacted;
    else out.content = r.redacted;
    if (r.hasSensitive) out.agentMessage = 'Advisory: sensitive data (POPIA/PCI) was redacted before sending.';
    process.stdout.write(JSON.stringify(out));
  } catch (err) {
    // Filter unreachable. Fail CLOSED by default so unredacted PII never leaves.
    if (FAIL_OPEN) {
      process.stdout.write(JSON.stringify({ continue: true, permission: 'allow',
        agentMessage: 'Advisory: privacy-filter unreachable; sent WITHOUT redaction (fail-open configured).' }));
    } else {
      process.stdout.write(JSON.stringify({ continue: false, permission: 'deny',
        userMessage: 'Advisory privacy-filter is unreachable — request blocked so unredacted data is not sent. Check the filter, or set ADVISORY_FAIL_OPEN=1 to override.' }));
    }
  }
})();
