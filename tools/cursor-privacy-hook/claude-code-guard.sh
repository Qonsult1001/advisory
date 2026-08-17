#!/usr/bin/env bash
# Advisory privacy GUARD for Claude Code (UserPromptSubmit hook). Dependency-free: only bash + curl.
#
# HONEST NOTE: Claude Code's UserPromptSubmit hook can BLOCK a prompt but CANNOT silently rewrite/redact it
# (only Cursor's beforeSubmitPrompt can). So this hook DETECTS POPIA/PCI in your prompt via the Advisory
# privacy-filter and BLOCKS it, showing the redacted preview so you know what to remove. For SILENT
# redact-and-continue in Claude Code, route it through the gateway (ANTHROPIC_BASE_URL=<gateway>).
#
# Install: add to ~/.claude/settings.json as a UserPromptSubmit hook (see README).
#   ADVISORY_REDACT_URL  default http://localhost:5000/api/dlp/redact
URL="${ADVISORY_REDACT_URL:-http://localhost:5000/api/dlp/redact}"

input="$(cat)"

# Minimal JSON-string escaper for the prompt (quotes, backslashes, newlines, tabs) so we can POST it safely
# without jq. Reads stdin, writes an escaped string.
json_escape() {
  local s="$1"
  s="${s//\\/\\\\}"      # backslash
  s="${s//\"/\\\"}"      # double quote
  s="${s//$'\n'/\\n}"    # newline
  s="${s//$'\r'/}"       # strip CR
  s="${s//$'\t'/\\t}"    # tab
  printf '%s' "$s"
}

# Pull .prompt out of the hook payload without jq: grab the value after "prompt":". Good enough for the
# guard (we only need to know IF it contains PII; exact parsing isn't required).
prompt="$(printf '%s' "$input" | sed -n 's/.*"prompt"[[:space:]]*:[[:space:]]*"\(.*\)/\1/p')"
# Trim a trailing quote + rest of the JSON if present (best-effort).
prompt="${prompt%%\",*}"; prompt="${prompt%\"}"
[ -z "$prompt" ] && { printf '{}'; exit 0; }   # nothing to scan → allow

esc="$(json_escape "$prompt")"
resp="$(curl -s --max-time 6 -X POST "$URL" -H 'Content-Type: application/json' -d "{\"text\":\"$esc\"}" 2>/dev/null)"

# Fail CLOSED: unreachable/empty → block so unredacted PII never leaves.
if [ -z "$resp" ]; then
  printf '%s' '{"decision":"block","reason":"Advisory privacy-filter unreachable — prompt blocked so unredacted POPIA/PCI data is not sent. Start the filter, or remove the hook to override."}'
  exit 0
fi

case "$resp" in
  *'"hasSensitive":true'*)
    # Extract the redacted preview for the block message (best-effort, no jq).
    redacted="$(printf '%s' "$resp" | sed -n 's/.*"redacted"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')"
    redacted="${redacted//\\/}"   # drop any stray escape chars from the preview so the reason JSON stays valid
    printf '{"decision":"block","reason":"Advisory: your prompt contains POPIA/PCI data and was BLOCKED before sending. Remove the sensitive values. Redacted preview: %s"}' "$redacted"
    ;;
  *)
    printf '{}'   # clean → allow
    ;;
esac
