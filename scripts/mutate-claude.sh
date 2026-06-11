#!/usr/bin/env bash
# mutate-claude.sh — the mutation WORKER. Drains the dashboard queue and runs /mutate via the
# Claude Code CLI, reporting live stage progress back to the dashboard.
#
# Based on an MIT-licensed evolution harness (see NOTICE).
#
# HOW IT WORKS (no API key, no GitHub secret):
#   • `claude -p "/mutate"` uses your EXISTING Claude Code login (Pro/Max). Nothing to configure for auth.
#   • GitHub access is via the `gh` CLI being logged in (`gh auth login`).
#   • This is the WORKER the dashboard "Mutate" button waits for. Clicking Mutate queues a request
#     (ticket + run id) in the queue dir; this loop drains it, runs the cycle, and POSTs progress to
#     the API so the dashboard shows a live bar (setup → plan → test → fix → build → tests → PR).
#   • While looping it also heartbeats the API so the dashboard can show "worker online".
#   • PR-only: every change becomes a pull request for human review.
#
# Usage:
#   ./scripts/mutate-claude.sh            # drain + run one cycle now
#   ./scripts/mutate-claude.sh --loop 1m  # WORKER MODE: drain every minute, heartbeat, run when queued
#   FORCE_RUN=true ./scripts/mutate-claude.sh   # bypass the hour gate, act immediately
#
# (start-worker.cmd launches this in --loop mode in the background for you.)
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

# ---- Headless auth (the key lesson) ----
# A background `claude -p` worker must NOT rely on the interactive OAuth login — that needs a TTY
# and races with other Claude sessions, giving "Not logged in". Like SAID-ECHO's evolve-claude.sh,
# the worker uses an EXPLICIT credential from the environment / .env:
#   • CLAUDE_CODE_OAUTH_TOKEN — from `claude setup-token` (1-year token, subscription plans), or
#   • ANTHROPIC_API_KEY       — a standard API key.
# Source .env (gitignored) so the key set there flows through to the claude CLI.
if [ -f .env ]; then set -a; . ./.env 2>/dev/null || true; set +a; fi

# ---- Environment self-sufficiency (don't trust the inherited PATH) ----
# A worker launched from cmd.exe can inherit a narrow PATH that's missing dotnet / gh, which makes
# the cycle report "dotnet not found" or "0 tickets" even though the tools exist. Ensure the common
# install locations are on PATH so the cycle behaves the same as an interactive shell.
for d in "/c/Program Files/dotnet" "/c/Program Files/GitHub CLI" "/c/nvm4w/nodejs" \
         "$HOME/.dotnet/tools" "/c/Program Files/Git/bin" "/c/Program Files/Git/usr/bin"; do
  case ":$PATH:" in *":$d:"*) : ;; *) [ -d "$d" ] && PATH="$PATH:$d" ;; esac
done
export PATH
# Tell the cycle which repo to act on (mutate-ide.sh reads REPO; default to this repo's gh remote).
export REPO="${REPO:-${EVOLUTION_REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || echo '')}}"

QUEUE_DIR="${EVOLUTION_QUEUE_DIR:-./data/evolution-queue}"
API="${ADVISORY_API:-http://localhost:5000/api}"
CUR_RUN=""    # run id we are currently reporting progress for

# One-line environment report so the worker window shows exactly what it resolved.
echo "[env] dotnet=$(command -v dotnet || echo MISSING) | gh=$(command -v gh || echo MISSING) | claude=$(command -v claude || echo MISSING) | repo=${REPO:-<none>}"

# Fail fast with a clear message if there is no headless credential.
if [ -z "${CLAUDE_CODE_OAUTH_TOKEN:-}" ] && [ -z "${ANTHROPIC_API_KEY:-}" ]; then
  echo "⚠ No headless Claude credential. A background worker can't use the interactive login."
  echo "  Fix (one of):"
  echo "   • Run 'claude setup-token' (interactive), then add to .env:  CLAUDE_CODE_OAUTH_TOKEN=..."
  echo "   • Or add to .env:  ANTHROPIC_API_KEY=sk-ant-..."
  echo "  Then re-run start-worker.cmd. (Heartbeat-only mode until then.)"
fi

# ---- progress reporting to the dashboard (best-effort; never fails the cycle) ----
api_post() { curl -s -m 5 -X POST "$API/$1" -H "Content-Type: application/json" -d "$2" >/dev/null 2>&1 || true; }
heartbeat() { api_post "evolution/worker/ping" '{}'; }
progress() {   # progress <stage> [status] [prUrl] [logline]
  [ -n "$CUR_RUN" ] || return 0
  local stage="$1" status="${2:-}" pr="${3:-}" log="${4:-}"
  api_post "evolution/run/$CUR_RUN/progress" \
    "$(printf '{"stage":"%s","status":"%s","prUrl":"%s","log":"%s"}' "$stage" "$status" "$pr" "$log")"
}
# Append a single live-activity line to the run log (no stage change) so the dashboard shows what
# the engine is actually DOING — which file it reads/edits, which command it runs.
activity() {   # activity <text>
  [ -n "$CUR_RUN" ] || return 0
  local msg; msg="$(printf '%s' "$1" | tr -d '\r' | sed 's/"/\\"/g' | cut -c1-200)"
  api_post "evolution/run/$CUR_RUN/progress" "$(printf '{"log":"%s"}' "$msg")"
}

# Read claude's stream-json on stdin and translate each tool action into a human-readable activity
# line POSTed to the dashboard. Echoes raw output too (so the worker window still shows everything).
stream_activity() {
  python - "$API" "$CUR_RUN" <<'PY' 2>/dev/null || cat   # fallback: just passthrough if python missing
import sys, json, urllib.request
api, run = sys.argv[1], sys.argv[2]
def post(text):
    if not run: return
    try:
        body=json.dumps({"log":text[:200]}).encode()
        urllib.request.urlopen(urllib.request.Request(f"{api}/evolution/run/{run}/progress",
            data=body, headers={"Content-Type":"application/json"}), timeout=5)
    except Exception: pass
for line in sys.stdin:
    sys.stdout.write(line); sys.stdout.flush()
    line=line.strip()
    if not line.startswith("{"): continue
    try: ev=json.loads(line)
    except Exception: continue
    t=ev.get("type")
    if t=="assistant":
        for c in ev.get("message",{}).get("content",[]):
            if c.get("type")=="text" and c.get("text","").strip():
                post("· "+c["text"].strip().split("\n")[0][:160])
            elif c.get("type")=="tool_use":
                name=c.get("name",""); inp=c.get("input",{})
                if name in ("Edit","Write"):   post(f"✎ editing {inp.get('file_path','')}")
                elif name=="Read":             post(f"reading {inp.get('file_path','')}")
                elif name=="Bash":             post(f"$ {str(inp.get('command',''))[:140]}")
                elif name in ("Grep","Glob"):  post(f"searching {inp.get('pattern','')}")
                elif name=="TodoWrite":        post("planning tasks…")
                else:                          post(f"{name}…")
    elif t=="result":
        post(("✔ " if not ev.get("is_error") else "✖ ")+str(ev.get("result",""))[:160])
PY
}

# Pull the next queued run id from the API (so we report against the dashboard's run row).
next_run_id() { curl -s -m 5 "$API/evolution/next" 2>/dev/null | grep -oE '"id":"[a-z0-9]+"' | head -1 | cut -d'"' -f4; }

# Ask the API (root in the container) to delete the consumed request — works even when the host
# user can't remove a root-owned file on the bind mount. Falls back to a local rm.
consume_request() {   # consume_request <basename>
  curl -s -m 5 -X POST "$API/evolution/queue/consume" -H "Content-Type: application/json" \
    -d "$(printf '{"file":"%s"}' "$1")" >/dev/null 2>&1 || true
  rm -f "$QUEUE_DIR/$1" 2>/dev/null || true   # best-effort; ignore permission errors
}

drain_queue() {   # returns 0 if a request was found (work to do), 1 if none
  [ -d "$QUEUE_DIR" ] || return 1
  local found=1
  for req in "$QUEUE_DIR"/ticket-*.request; do
    [ -e "$req" ] || continue
    # request format: line1=ticket, line2=runId, line3=timestamp
    CUR_RUN="$(sed -n '2p' "$req" 2>/dev/null | tr -d '[:space:]')"
    echo "[$(date '+%F %T')] picked up $(basename "$req") (run=${CUR_RUN:-?})"
    consume_request "$(basename "$req")"   # remove via API (root) so it isn't re-picked next tick
    found=0
  done
  [ -z "$CUR_RUN" ] && CUR_RUN="$(next_run_id)"   # fall back to whatever the API has queued
  return $found
}

# Preflight auth probe. With an explicit credential (CLAUDE_CODE_OAUTH_TOKEN / ANTHROPIC_API_KEY)
# this succeeds immediately. "Not logged in" here means the credential is MISSING or INVALID —
# not a transient slot race — so we report it clearly rather than spin.
claude_ready() {
  local probe
  probe="$(claude -p --dangerously-skip-permissions "reply with exactly: READY" 2>&1)"
  printf '%s' "$probe" | grep -qx "READY" && return 0
  if printf '%s' "$probe" | grep -qiE "not logged in|please run /login|invalid|unauthor"; then
    return 1   # missing/invalid headless credential
  fi
  return 0       # some other output — not an auth problem; let the real cycle run and report
}

run_cycle() {
  progress "setup" "running" "" "worker picked up the ticket"
  echo "[$(date '+%F %T')] /mutate cycle start (run=${CUR_RUN:-?})"

  # Preflight: confirm the worker has a working headless credential before the heavy cycle.
  if ! claude_ready; then
    progress "fix" "failed" "" "no headless Claude credential — set CLAUDE_CODE_OAUTH_TOKEN (claude setup-token) or ANTHROPIC_API_KEY in .env, then click Mutate again"
    echo "[$(date '+%F %T')] /mutate cycle FAILED — missing/invalid headless credential (see .env note above)"; CUR_RUN=""; return 0
  fi

  # PRs that already existed before this run — so we only claim a PR the cycle actually created.
  local before; before="$(gh pr list --state open --json number --jq '[.[].number]|join(",")' 2>/dev/null || true)"

  progress "plan" "running" "" "evolve: planning + implementing the fix"
  # The evolve engine (Claude Code) runs the /mutate skill: plan, write a failing test, implement,
  # build, test, open PR. STREAM its stream-json output through stream_activity so the dashboard
  # shows live what it's doing (reading/editing files, running commands) — not just a percentage.
  # Capture the full text too (out) so we can still tell real success from a no-op.
  local out rc tmp; tmp="$(mktemp 2>/dev/null || echo /tmp/mutate-out.$$)"
  claude -p --dangerously-skip-permissions --verbose --output-format stream-json "/mutate" 2>&1 \
    | stream_activity | tee "$tmp"; rc=${PIPESTATUS[0]}
  out="$(cat "$tmp" 2>/dev/null)"; rm -f "$tmp" 2>/dev/null

  # HONEST outcome detection — do NOT claim success just because claude exited 0.
  if [ $rc -ne 0 ] || printf '%s' "$out" | grep -qiE "unknown skill|not logged in|please run /login|no such (command|skill)"; then
    local why="cycle failed"
    printf '%s' "$out" | grep -qi "unknown skill" && why="the /mutate skill was not found (.claude/skills/mutate)"
    printf '%s' "$out" | grep -qiE "not logged in|/login" && why="Claude is not logged in for the worker shell"
    progress "fix" "failed" "" "$why — see worker log"
    echo "[$(date '+%F %T')] /mutate cycle FAILED — $why"; CUR_RUN=""; return 0
  fi

  # Require an actual NEW open PR before claiming pr-open.
  local after newpr=""; after="$(gh pr list --state open --json number,url --jq '.[]|"\(.number) \(.url)"' 2>/dev/null || true)"
  while read -r num url; do
    [ -n "$num" ] || continue
    case ",$before," in *",$num,"*) : ;; *) newpr="$url" ;; esac
  done <<< "$after"

  if [ -n "$newpr" ]; then
    progress "pr" "pr-open" "$newpr" "cycle complete — PR opened for review"
    echo "[$(date '+%F %T')] /mutate cycle complete → $newpr"
  else
    progress "pr" "skipped" "" "cycle ran but opened no PR (no change / no work)"
    echo "[$(date '+%F %T')] /mutate cycle complete — no PR opened (no change)"
  fi
  CUR_RUN=""
}

run_once() {
  heartbeat
  if drain_queue; then
    run_cycle
  else
    echo "[$(date '+%F %T')] queue empty — nothing to do (worker online)"
  fi
}

secs() { local v="$1" n="${1%[smhSMH]}" u="${1##*[0-9]}"; case "$u" in s|S) echo "$n";; m|M) echo $((n*60));; h|H) echo $((n*3600));; *) echo $((n*60));; esac; }

case "${1:-}" in
  --loop) i="$(secs "${2:-1m}")"; echo "WORKER online — draining every ${2:-1m} (${i}s), API=$API";
          while true; do run_once || echo "tick failed; continuing"; sleep "$i"; done ;;
  --help|-h) echo "usage: $0 [--loop INTERVAL]";;
  "") run_once ;;
  *) echo "unknown: $1"; exit 1 ;;
esac
