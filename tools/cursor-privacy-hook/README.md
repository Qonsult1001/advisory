# Advisory privacy hook for Cursor — POPIA / PCI redaction inside standard Cursor

Standard Cursor (downloaded from cursor.com, paid subscription) sends prompts and file contents to its own
AI backend — traffic you **cannot** intercept or MITM (it's Cursor's servers, with certificate pinning). So
you can't filter it at the network. **This hook filters it inside Cursor instead.**

Cursor runs a shell hook **before** it sends a prompt or file to the AI. This package's hook takes that
text, sends it to the Advisory **privacy-filter** (`/api/dlp/redact`), and hands Cursor back the **redacted**
version. POPIA IDs, payment cards, names, emails and secrets are replaced with `[…:REDACTED]` **before
anything leaves the developer's machine**. The developer uses Cursor completely normally; the model has no
say — the hook runs unconditionally.

This works for **any editor with the same hook contract** (Claude Code has one too), not just Cursor.

## What it covers

- **`beforeSubmitPrompt`** — the prompt the developer types is redacted before it's sent.
- **`beforeReadFile`** — a file's contents are redacted before they're attached to the AI request.

## Install (per developer — or push from IT)

1. Copy this folder's `redact-hook.mjs` to `~/.cursor/advisory/redact-hook.mjs` (needs Node.js, which
   Cursor already ships with / most dev machines have).
2. Copy `hooks.json` to `~/.cursor/hooks.json` (global, all projects) — or to `<project>/.cursor/hooks.json`
   for one repo.
3. Point the hook at your Advisory privacy-filter by setting **one** environment variable for the user
   (system env, login profile, or MDM):
   ```
   ADVISORY_REDACT_URL=https://pacman.dtpodmandev01.directtransact.corp/api/dlp/redact
   ```
   (Any URL that reaches the API's `/api/dlp/redact` works — the console proxy, or the API host directly.)
4. Restart Cursor. Done — every prompt and file read is now redacted before it leaves.

> **IT rollout:** drop `redact-hook.mjs` + `hooks.json` into the two locations above via your MDM / Group
> Policy / a login script, and set `ADVISORY_REDACT_URL` machine-wide. Zero per-developer action after that
> — same model as the package-proxy tokens and SSO.

## Fail-closed by default

If the privacy-filter is **unreachable**, the hook **DENIES** the request (blocks it) so unredacted data is
never sent by accident. To prefer availability over strict blocking during an outage, set
`ADVISORY_FAIL_OPEN=1` (not recommended for POPIA/PCI-regulated work).

## Verify it works

Redact a sample directly against the endpoint the hook uses:

```bash
curl -s -X POST "$ADVISORY_REDACT_URL" -H "Content-Type: application/json" \
  -d '{"text":"SA ID 8001015009087, card 4111 1111 1111 1111, John Smith"}'
# → {"redacted":"SA ID [ACCOUNT_NUMBER:REDACTED], card [ACCOUNT_NUMBER:REDACTED], [PERSON_NAME:REDACTED]", "hasSensitive":true, ...}
```

Or drive the hook exactly as Cursor does (JSON on stdin):

```bash
echo '{"prompt":"my card is 4111 1111 1111 1111"}' | node redact-hook.mjs
# → {"continue":true,"permission":"allow","prompt":"my card is [CREDIT_CARD:REDACTED]", ...}
```

## Claude Code (and the app-layer vs. network question)

Claude Code also has hooks, but a **key difference**: its `UserPromptSubmit` hook can **block** a prompt or
add context — it **cannot silently rewrite/redact** the prompt (only Cursor's `beforeSubmitPrompt` can).
So there are two ways to protect Claude Code, and you'll likely want both:

- **Silent redaction (recommended for Claude Code):** route it through the gateway —
  `ANTHROPIC_BASE_URL=https://<your-gateway>` (+ `ANTHROPIC_AUTH_TOKEN`). The gateway redacts and forwards;
  the developer never sees an interruption.
- **Detect-and-block guard (test it right now):** add `claude-code-guard.sh` as a `UserPromptSubmit` hook.
  It scans your prompt via `/api/dlp/redact` and **blocks** it (fail-closed) if it contains POPIA/PCI,
  showing the redacted preview. Add to `~/.claude/settings.json`:

  ```json
  {
    "hooks": {
      "UserPromptSubmit": [
        { "hooks": [ { "type": "command",
          "command": "ADVISORY_REDACT_URL=http://localhost:5000/api/dlp/redact bash /path/to/claude-code-guard.sh" } ] }
      ]
    }
  }
  ```

  Then type a prompt with a fake SA ID or card — Claude Code refuses it. That's the app-layer proof.

## Network enforcement — the non-bypassable backstop

The hook is the app-layer control (redacts, but a developer can remove it). For an enforcement guarantee,
pair it with a **firewall egress policy** on developer subnets:

- **Block** outbound to the AI vendors' inference hosts: `api.openai.com`, `api.anthropic.com`, and — to
  cut off Cursor's uninterceptable built-in AI — `api2.cursor.sh` / `api3.cursor.sh` / `api4.cursor.sh` /
  `*.cursorapi.com`.
- **Allow** only your gateway's egress to those vendors (the gateway holds the keys and does the redaction).

Now a developer who removes the hook simply **can't reach the AI** except through the gateway, and Cursor's
built-in AI can't phone home — forcing everyone down the redacting path. Hook = redaction; network block =
it can't be turned off.

## Honest limits

- The hook covers the **prompt + file** surfaces Cursor exposes hooks for. If Cursor adds a data path with
  no hook, that path isn't covered — hooks can only filter what Cursor lets them see.
- This protects **outbound** data (what you send to the AI). It does not change what the AI *returns*.
- For belt-and-suspenders enforcement (so the hook can't be removed by a developer), pair it with a
  network egress policy that only allows AI access via the gateway. The hook is the app-layer control;
  the network block is the backstop.
