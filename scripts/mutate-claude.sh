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
# Source .env (gitignored) so the key set there flows through to the claude CLI. Re-sourced every
# tick (load_env in run_once) so a refreshed CLAUDE_CODE_OAUTH_TOKEN takes effect WITHOUT a restart
# — the OAuth access token is short-lived; setup-token gives a durable 1-year one.
load_env() { [ -f .env ] && { set -a; . ./.env 2>/dev/null || true; set +a; }; }
load_env

# ---- Use the operator's INTERACTIVE Claude login when no valid .env token is set ----
# The worker often runs under WSL, whose HOME differs from Windows — so a `claude` login done on
# Windows is invisible to WSL ("Not logged in"), which is why we needed an explicit .env token. But
# that token (a short-lived OAuth access token) EXPIRES and then every cycle fails 401. Durable answer
# without copying secrets: point CLAUDE_CONFIG_DIR at the Windows .claude profile so WSL's claude reads
# the SAME live credentials the operator's interactive `claude` already uses. Verified: with
# CLAUDE_CONFIG_DIR=/mnt/c/Users/<user>/.claude, WSL claude authenticates with no token.
# Only do this when there's no usable .env credential, and only if that profile actually exists.
if [ -z "${CLAUDE_CODE_OAUTH_TOKEN:-}" ] && [ -z "${ANTHROPIC_API_KEY:-}" ] && [ -z "${CLAUDE_CONFIG_DIR:-}" ]; then
  for _cdir in "/mnt/c/Users/$USER/.claude" "/mnt/c/Users/Carter/.claude" "$HOME/.claude"; do
    if [ -f "$_cdir/.credentials.json" ] || [ -f "$_cdir/settings.json" ]; then
      export CLAUDE_CONFIG_DIR="$_cdir"
      echo "[auth] no .env token — using interactive Claude login at $CLAUDE_CONFIG_DIR"
      break
    fi
  done
fi

# IMPORTANT: do NOT override CLAUDE_CONFIG_DIR for the cycle. An isolated/empty config dir was tried
# and it BROKE /mutate (claude produced zero output) — with a redirected config the project's skills
# don't load the same way and the slash command dies silently. The operator's DEFAULT config correctly
# exposes the /mutate skill ("skills":[…,"mutate"]) and runs the cycle. We rely on the default config;
# auth still comes from CLAUDE_CODE_OAUTH_TOKEN in .env.

# ---- PATH: include user-local bin so non-login shells find CLI tools ----
# cursor-agent (and similar) install to ~/.local/bin, which is added to PATH by ~/.bashrc/~/.profile
# — but those only run in LOGIN/interactive shells. The worker's subprocess calls (drain_cursor_auth,
# the cycle) run in non-login shells where ~/.local/bin is NOT on PATH, so `command -v cursor-agent`
# returned "not found" even though it's installed. Add the common user-bin dirs explicitly.
export PATH="$HOME/.local/bin:$HOME/bin:$PATH"

# ---- Tool resolution (works in Git-Bash AND WSL) ----
# The worker itself calls `gh` (PR detection) and may report `dotnet`. In WSL those Linux binaries
# don't exist — only the Windows .exe (via /mnt/c). Bare `gh` then fails SILENTLY, which is why the
# worker reported "no change" even after a real PR was opened. Wrapper functions fall back to the
# .exe so every gh/dotnet call works in either shell. (Same approach as scripts/mutate-ide.sh.)
_find_exe() {  # _find_exe <name> <path1> [path2...]
  command -v "$1" >/dev/null 2>&1 && { echo "$1"; return; }
  command -v "$1.exe" >/dev/null 2>&1 && { echo "$1.exe"; return; }
  local n="$1"; shift
  for p in "$@"; do [ -x "$p" ] && { echo "$p"; return; }; done
  echo "$n"
}
GH_BIN="$(_find_exe gh "/mnt/c/Program Files/GitHub CLI/gh.exe" "/c/Program Files/GitHub CLI/gh.exe")"
DOTNET_BIN="$(_find_exe dotnet "/mnt/c/Program Files/dotnet/dotnet.exe" "/c/Program Files/dotnet/dotnet.exe")"
gh()     { "$GH_BIN" "$@"; }
dotnet() { "$DOTNET_BIN" "$@"; }
# Tell the cycle which repo to act on (mutate-ide.sh reads REPO; default to this repo's gh remote).
export REPO="${REPO:-${EVOLUTION_REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || echo '')}}"

QUEUE_DIR="${EVOLUTION_QUEUE_DIR:-./data/evolution-queue}"
API="${ADVISORY_API:-http://localhost:5000/api}"
CUR_RUN=""    # run id we are currently reporting progress for
CUR_TICKET="" # ticket number being processed (used to close the issue after auto-release)

# One-line environment report so the worker window shows exactly what it resolved.
echo "[env] dotnet=$DOTNET_BIN | gh=$GH_BIN | claude=$(command -v claude || echo MISSING) | repo=${REPO:-<none>}"

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

# BACKGROUND heartbeat: a /mutate cycle blocks for minutes inside `claude`, during which the worker
# can't ping — so the dashboard flipped to OFFLINE mid-run (the worker can't do two things at once).
# Fix: fork a subshell that pings every 30s and keep its PID so we can stop it when the cycle ends.
HB_PID=""
start_heartbeat() { ( while true; do api_post "evolution/worker/ping" '{}'; sleep 30; done ) & HB_PID=$!; }
stop_heartbeat()  { [ -n "$HB_PID" ] && kill "$HB_PID" 2>/dev/null; HB_PID=""; }
trap 'stop_heartbeat' EXIT

# PROJECT CONTEXT (Cursor-style full-project awareness for whichever agent runs).
# Format: .said brain (preferred — semantic + symbol search via tools/said) or a plain .md map.
# Built ONCE if absent; pass FORCE_CONTEXT=true (or the Admin "Rebuild context" hits this) to redo.
SAID_BIN="./tools/said/said.exe"
CONTEXT_FORMAT="${CONTEXT_FORMAT:-said}"
build_context() {
  if [ "$CONTEXT_FORMAT" = "said" ] && [ -x "$SAID_BIN" ]; then
    if [ ! -f Advisory.said ] || [ "${FORCE_CONTEXT:-}" = "true" ]; then
      echo "[$(date '+%F %T')] building project context (.said brain)…"
      [ "${FORCE_CONTEXT:-}" = "true" ] && rm -f Advisory.said 2>/dev/null
      # `init` already recurses the whole repo (AST-aware via the 'code' feature) and builds the full
      # semantic + symbol + trigram index. Do NOT also `add --dir` — that double-adds and corrupts the
      # SCA index so `ask` returns 'no match'. A single init = 671 frames, 506 symbols, ask works.
      "$SAID_BIN" init >/dev/null 2>&1 || true
    fi
    # Push live brain stats to the dashboard (the worker can run said.exe; the container can't).
    local st; st="$("$SAID_BIN" stats --json 2>/dev/null || echo '')"
    [ -n "$st" ] && api_post "admin/context/stats" "$st"
  else
    # Markdown fallback: a file tree + per-file head, built once.
    if [ ! -f PROJECT_CONTEXT.md ] || [ "${FORCE_CONTEXT:-}" = "true" ]; then
      echo "[$(date '+%F %T')] building project context (PROJECT_CONTEXT.md)…"
      {
        echo "# Project Context — Advisory"; echo
        echo "Auto-generated map of the codebase for agent context. Regenerate with FORCE_CONTEXT=true."; echo
        echo "## Source tree"; echo '```'
        git ls-files src web/src tests 2>/dev/null | head -400
        echo '```'
      } > PROJECT_CONTEXT.md 2>/dev/null || true
    fi
  fi
}
progress() {   # progress <stage> [status] [prUrl] [logline]
  [ -n "$CUR_RUN" ] || return 0
  local stage="$1" status="${2:-}" pr="${3:-}" log="${4:-}"
  api_post "evolution/run/$CUR_RUN/progress" \
    "$(printf '{"stage":"%s","status":"%s","prUrl":"%s","log":"%s"}' "$stage" "$status" "$pr" "$log")"
}

# Reset a run to a clean state — used when a cycle is stopped for an external reason (rate limit /
# out of credits) rather than a real failure, so the dashboard doesn't show a misleading "failed" and
# the ticket can simply be re-queued later. Best-effort; ignores errors.
reset_run() {   # reset_run <runId>
  [ -n "$1" ] || return 0
  api_post "evolution/run/$1/reset" '{}' 2>/dev/null || true
}

# Translate claude's stream-json (stdin) into readable activity lines POSTed to the run log live, so
# the dashboard shows WHAT the engine is doing (reading/editing files, running commands) — not just a %.
# Echoes raw output too (worker window still shows everything). Falls back to passthrough if no python.
stream_activity() {
  # Use python3 (WSL/Linux) or python (Git-Bash); fall back to raw passthrough if neither exists.
  local PY; PY="$(command -v python3 || command -v python || echo '')"
  [ -z "$PY" ] && { cat; return; }
  "$PY" - "$API" "$CUR_RUN" <<'PY' 2>/dev/null || cat
import sys, json, urllib.request
api, run = sys.argv[1], sys.argv[2]
def post(text):
    if not run: return
    try:
        body=json.dumps({"log":text[:200]}).encode()
        urllib.request.urlopen(urllib.request.Request(api+"/evolution/run/"+run+"/progress",
            data=body, headers={"Content-Type":"application/json"}), timeout=5)
    except Exception: pass
for line in sys.stdin:
    sys.stdout.write(line); sys.stdout.flush()
    line=line.strip()
    if not line.startswith("{"): continue
    try: ev=json.loads(line)
    except Exception: continue
    if ev.get("type")=="assistant":
        for c in ev.get("message",{}).get("content",[]):
            if c.get("type")=="text" and c.get("text","").strip():
                post("· "+c["text"].strip().split("\n")[0][:160])
            elif c.get("type")=="tool_use":
                n=c.get("name",""); i=c.get("input",{})
                if n in ("Edit","Write"):  post("editing "+str(i.get("file_path","")))
                elif n=="Read":            post("reading "+str(i.get("file_path","")))
                elif n=="Bash":            post("$ "+str(i.get("command",""))[:140])
                elif n in ("Grep","Glob"): post("searching "+str(i.get("pattern","")))
                elif n=="TodoWrite":       post("planning tasks…")
                else:                      post(n+"…")
    elif ev.get("type")=="result":
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

# Drain per-agent TEST requests for CLI agents (claude-cli/cursor-cli). The dashboard "Test" button
# queues agenttest-<id>.request; we run the local Claude CLI and POST the reply back so each agent
# is verifiable as its own module.
drain_agent_tests() {
  [ -d "$QUEUE_DIR" ] || return 0
  for req in "$QUEUE_DIR"/agenttest-*.request; do
    [ -e "$req" ] || continue
    local id prompt reply rc
    id="$(sed -n '1p' "$req" 2>/dev/null | tr -d '[:space:]')"
    prompt="$(sed -n '2p' "$req" 2>/dev/null)"
    echo "[$(date '+%F %T')] agent test: $id"
    consume_request "$(basename "$req")"
    reply="$(claude -p --dangerously-skip-permissions "$prompt" 2>&1)"; rc=$?
    local ok=true; [ $rc -ne 0 ] && ok=false
    # escape for JSON
    local esc; esc="$(printf '%s' "$reply" | tr -d '\r' | sed 's/\\/\\\\/g; s/"/\\"/g' | tr '\n' ' ' | cut -c1-1500)"
    curl -s -m 30 -X POST "$API/admin/agent/$id/test/result" -H "Content-Type: application/json" \
      -d "$(printf '{"reply":"%s","ok":%s,"error":null}' "$esc" "$ok")" >/dev/null 2>&1 || true
  done
}

# Post an auth-flow result back to the dashboard for agent $1.
_post_auth_result() { # id status message url ok
  curl -s -m 10 -X POST "$API/admin/agent/$1/cursor-auth/result" -H "Content-Type: application/json" \
    -d "$(printf '{"status":"%s","message":"%s","url":"%s","ok":%s}' "$2" "$3" "$4" "$5")" >/dev/null 2>&1 || true
}

# Persist a long-lived Claude token into .env so the worker (and the API key-mask layer) re-source it.
# Replaces any existing CLAUDE_CODE_OAUTH_TOKEN line; appends if absent. .env is gitignored.
_persist_claude_token() { # token
  local tok="$1" env_file="./.env"   # worker runs from repo root; same file load_env sources
  [ -n "$tok" ] || return 1
  [ -f "$env_file" ] || : > "$env_file"
  if grep -q '^CLAUDE_CODE_OAUTH_TOKEN=' "$env_file" 2>/dev/null; then
    sed -i "s|^CLAUDE_CODE_OAUTH_TOKEN=.*|CLAUDE_CODE_OAUTH_TOKEN=$tok|" "$env_file"
  else
    printf 'CLAUDE_CODE_OAUTH_TOKEN=%s\n' "$tok" >> "$env_file"
  fi
  load_env   # re-source so the freshly captured token takes effect this tick
}

# Drain CLI AUTH requests for both cursor-cli and claude-cli. Each prints a browser URL the user opens
# to authenticate; we relay status + URL back to the dashboard. For claude we also capture the
# long-lived token from `claude setup-token` and persist it to .env so other users' agent works after.
drain_cursor_auth() {
  [ -d "$QUEUE_DIR" ] || return 0
  for req in "$QUEUE_DIR"/cursorauth-*.request; do
    [ -e "$req" ] || continue
    local id standard user out url ok status msg tok
    id="$(sed -n '1p' "$req" 2>/dev/null | tr -d '[:space:]')"
    standard="$(sed -n '2p' "$req" 2>/dev/null | tr -d '[:space:]')"
    user="$(sed -n '3p' "$req" 2>/dev/null)"
    echo "[$(date '+%F %T')] cli auth: $id (standard=$standard user=$user)"
    consume_request "$(basename "$req")"

    if [ "$standard" = "claude-cli" ]; then
      if ! command -v claude >/dev/null 2>&1; then
        _post_auth_result "$id" "error" "claude CLI not found on this machine" "" "false"; continue
      fi
      # `claude setup-token` prints a browser URL, then (after the user approves) the long-lived token.
      out="$(timeout 30 claude setup-token 2>&1 || true)"
      url="$(printf '%s' "$out" | grep -oE 'https?://[^ ]+' | head -1)"
      tok="$(printf '%s' "$out" | grep -oE 'sk-ant-[A-Za-z0-9_-]+' | head -1)"
      if [ -n "$tok" ]; then
        _persist_claude_token "$tok"; status=authenticated; ok=true
        msg="token captured and persisted to .env — claude-cli is now authenticated for all users"
      elif [ -n "$url" ]; then status=browser-required; ok=false
        msg="open this URL to authenticate, then re-run if no token is captured"
      else status=pending; ok=false
        msg="$(printf '%s' "$out" | tr -d '\r' | sed 's/\\/\\\\/g; s/"/\\"/g' | tr '\n' ' ' | cut -c1-300)"
      fi
      _post_auth_result "$id" "$status" "$msg" "$url" "$ok"
      continue
    fi

    # cursor-cli — TWO-PHASE login: cursor-agent login prints a browser URL, then WAITS for the user to
    # approve in the browser. A short `timeout` killed the process before approval, so it never
    # persisted. Instead: run login in the background to a temp log, post the URL as soon as it appears
    # (dashboard shows it), then keep the process alive up to ~3 min waiting for approval, polling the
    # log for success and posting the final authenticated status.
    if ! command -v cursor-agent >/dev/null 2>&1; then
      _post_auth_result "$id" "error" "cursor-agent CLI not found on this machine — install Cursor CLI first" "" "false"; continue
    fi
    local clog; clog="${TMPDIR:-/tmp}/cursorlogin-$id.$$"; : > "$clog"
    NO_OPEN_BROWSER=1 cursor-agent login > "$clog" 2>&1 &
    local cpid=$!
    local url="" posted_url=0 n=0 status=pending ok=false
    while kill -0 "$cpid" 2>/dev/null && [ "$n" -lt 90 ]; do   # up to ~180s for the browser step
      sleep 2; n=$((n+1))
      if [ "$posted_url" = "0" ]; then
        url="$(grep -oE 'https?://[^ ]+' "$clog" 2>/dev/null | head -1)"
        if [ -n "$url" ]; then
          _post_auth_result "$id" "browser-required" "Open the link to authenticate, then it completes automatically." "$url" "false"
          echo "[$(date '+%F %T')] cursor login URL posted — awaiting browser approval"; posted_url=1
        fi
      fi
      grep -qiE 'logged in|authenticated|success' "$clog" 2>/dev/null && { status=authenticated; ok=true; break; }
    done
    kill "$cpid" 2>/dev/null
    # Final truth: ask cursor-agent directly rather than trust the log.
    if cursor-agent status 2>/dev/null | grep -qiE 'logged in|authenticated|^Logged in'; then status=authenticated; ok=true; fi
    local msg; msg="$(tail -c 300 "$clog" 2>/dev/null | tr -d '\r' | sed 's/\\/\\\\/g; s/"/\\"/g' | tr '\n' ' ')"
    [ "$ok" = "true" ] && msg="cursor-agent authenticated ✔"
    _post_auth_result "$id" "$status" "$msg" "$url" "$ok"
    rm -f "$clog" 2>/dev/null
    echo "[$(date '+%F %T')] cursor login result: status=$status"
  done
}

drain_queue() {   # returns 0 if a request was found (work to do), 1 if none
  [ -d "$QUEUE_DIR" ] || return 1
  local found=1
  for req in "$QUEUE_DIR"/ticket-*.request; do
    [ -e "$req" ] || continue
    # request format: line1=ticket, line2=runId, line3=timestamp
    CUR_TICKET="$(sed -n '1p' "$req" 2>/dev/null | tr -d '[:space:]')"
    CUR_RUN="$(sed -n '2p' "$req" 2>/dev/null | tr -d '[:space:]')"
    echo "[$(date '+%F %T')] picked up $(basename "$req") (ticket=#${CUR_TICKET:-?} run=${CUR_RUN:-?})"
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
  # Quick auth check before the heavy cycle. `< /dev/null` so it doesn't block ~3s waiting on stdin.
  local probe
  probe="$(claude -p --dangerously-skip-permissions "reply with exactly: READY" < /dev/null 2>&1)"
  printf '%s' "$probe" | grep -qx "READY" && return 0
  if printf '%s' "$probe" | grep -qiE "not logged in|please run /login|invalid|unauthor|401|authentication"; then
    return 1   # missing/invalid credential — caught here instead of a silent empty cycle
  fi
  return 0       # some other output — not an auth problem; let the real cycle run and report
}

run_cycle() {
  start_heartbeat   # keep pinging in the background so the dashboard stays "online" through the long run
  progress "setup" "running" "" "worker picked up the ticket"
  echo "[$(date '+%F %T')] /mutate cycle start (run=${CUR_RUN:-?})"

  # Preflight: confirm the worker has a working headless credential before the heavy cycle.
  if ! claude_ready; then
    progress "fix" "failed" "" "no headless Claude credential — set CLAUDE_CODE_OAUTH_TOKEN (claude setup-token) or ANTHROPIC_API_KEY in .env, then click Mutate again"
    echo "[$(date '+%F %T')] /mutate cycle FAILED — missing/invalid headless credential (see .env note above)"; CUR_RUN=""; return 0
  fi

  # PRs that already existed before this run — so we only claim a PR the cycle actually created.
  local before; before="$(gh pr list --state open --json number --jq '[.[].number]|join(",")' 2>/dev/null || true)"

  # PROJECT CONTEXT — give every agent full-codebase awareness (like Cursor). The operator's choice
  # of format (.said brain or .md map) comes from the Admin Center; env CONTEXT_FORMAT still overrides.
  # Built ONCE if missing.
  local cfmt; cfmt="$(curl -s -m 5 "$API/admin/settings" 2>/dev/null | grep -oE '"contextFormat":"[a-z]+"' | head -1 | cut -d'"' -f4)"
  [ -n "$cfmt" ] && CONTEXT_FORMAT="${CONTEXT_FORMAT:-$cfmt}"
  build_context

  # EPIC C — per-task agent routing. Fetch the operator's routing (which agent runs research /
  # planning / execution / documentation, and whether sequentially or in parallel) and drop it where
  # the /mutate skill reads it, so each phase dispatches to its assigned agent.
  local routing; routing="$(curl -s -m 5 "$API/admin/routing/mutation" 2>/dev/null || echo '{}')"
  printf '%s\n' "$routing" > .evolve/routing.json 2>/dev/null || true
  if printf '%s' "$routing" | grep -q '"name"'; then
    local mode; mode="$(printf '%s' "$routing" | grep -oE '"mode":"[a-z]+"' | head -1 | cut -d'"' -f4)"
    progress "plan" "running" "" "routing loaded (${mode:-sequential}) — see .evolve/routing.json"
    echo "[$(date '+%F %T')] task routing: ${routing}"
  fi

  progress "plan" "running" "" "evolve: planning + implementing the fix"
  # The evolve engine (Claude Code) runs the /mutate skill: plan, write a failing test, implement,
  # build, test, open PR. Capture its output so we can tell real success from a no-op.
  # STREAM stream-json through stream_activity so the dashboard shows live what it's doing.
  # Capture full output too (out) for honest success/failure detection.
  # Export run context so the /mutate skill can post its plan and poll for approval (EPIC A).
  # ADVISORY_APPROVAL=required makes the skill park for Approve/Reject before implementing.
  export ADVISORY_RUN="$CUR_RUN" ADVISORY_API="$API" ADVISORY_APPROVAL="${ADVISORY_APPROVAL:-required}"

  # CRITICAL: the /mutate skill's Step 1 runs `./scripts/mutate-ide.sh setup`, which has an HOUR GATE
  # (MUTATE_HOURS, default 0,4,8,12,16,20) — off-hours it prints "SKIPPED — hour N not in schedule" and
  # exits 0, so the skill finds NO WORK and produces no change. When a worker drains a queued ticket the
  # operator clicked, that gate must not apply (they asked for it NOW). Export the bypass here so it is
  # inherited by claude's subprocess Bash calls (the launcher's FORCE_RUN doesn't reliably propagate
  # through `claude -p`). This was the real cause of every clicked cycle ending in "no change".
  export FORCE_RUN=true MUTATE_HOURS="*"

  # RATE-LIMIT-AWARE retry. The /mutate cycle runs on the operator's Claude (Max) subscription, which
  # has a standard rate limit. If the account is rate-limited / out of credits for the window, claude
  # gets cut off and produces no usable cycle. Per operator policy: respect the model's standard limit,
  # and on a hit retry with backoff — wait 30s, then 60s — and if it STILL fails, report clearly,
  # reset the run (so it returns to a clean queueable state), and exit this cycle. We do NOT hammer the
  # API. A "rate-limited" run is detected by the stream's rate_limit/out_of_credits markers (or an
  # empty cycle that the result event flags), NOT by normal no-PR outcomes.
  # NOTE: in the worker's WSL login shell `mktemp` can succeed (exit 0) yet print NOTHING (empty
  # TMPDIR), so `$(mktemp || echo …)` leaves $tmp empty → `tee ""` writes nowhere → out="" and EVERY
  # cycle looked like "no output / no change" even though claude ran fine. Use mktemp's value only if
  # non-empty; otherwise a deterministic writable path. This was the real cause of the empty cycles.
  local out rc tmp; tmp="$(mktemp 2>/dev/null)"; [ -n "$tmp" ] || tmp="${TMPDIR:-/tmp}/mutate-out.$$"
  : > "$tmp" 2>/dev/null || tmp="./.evolve/mutate-out.$$"   # last-resort: repo-local (always writable)
  # Auth is resolved at startup (interactive login or .env token). One quick line so the log shows
  # which credential path is in use — no extra claude call (the self-test diagnostic was removed; it
  # was adding a full ~30-60s API round-trip to every cycle for no benefit once auth was confirmed).
  echo "[$(date '+%F %T')]   ↳ auth: ${CLAUDE_CONFIG_DIR:+config=$CLAUDE_CONFIG_DIR }token=$( [ -n "${CLAUDE_CODE_OAUTH_TOKEN:-}" ] && echo set || echo none )"

  local attempt=0; local -a backoffs=(30 60); local rate_limited=0
  while : ; do
    : > "$tmp"
    # Capture claude's RAW stream to $tmp FIRST (tee at the source, before stream_activity), so a
    # crash/no-python in the activity parser can never blackhole the output we rely on for outcome
    # detection. stream_activity only drives the live dashboard log; the file is the source of truth.
    # IMPORTANT: redirect stdin from /dev/null. `claude -p` at the head of a pipe inherits the worker's
    # (absent) stdin and blocks ~3s "waiting for stdin", which makes prompt delivery unreliable and can
    # yield an empty cycle. `< /dev/null` tells it there's no piped input so it runs the prompt cleanly.
    # DECOUPLE capture from the live parser. Previously: `claude | tee $tmp | stream_activity`. The
    # parser POSTs every line to the API (urllib); if those POSTs are slow/block (WSL→localhost
    # latency), the pipe backpressures, `tee` STALLS, and $tmp ends up EMPTY even though claude is
    # producing output — exactly the 0-byte symptom we kept seeing. Fix: write claude's stream STRAIGHT
    # to $tmp with no downstream consumer that can stall it (capture is now guaranteed), then drive the
    # dashboard activity log FROM the captured file afterwards (best-effort; can't affect capture).
    claude -p --dangerously-skip-permissions --verbose --output-format stream-json "/mutate" < /dev/null > "$tmp" 2>&1
    rc=$?
    out="$(cat "$tmp" 2>/dev/null)"
    # Replay the captured stream through the activity parser for the dashboard log (non-blocking to
    # capture; if the API POSTs are slow it only delays the log, never the cycle outcome).
    stream_activity < "$tmp" >/dev/null 2>&1 &
    # Diagnostic: prove whether claude produced output and what exit code it returned.
    echo "[$(date '+%F %T')]   ↳ cycle capture: rc=$rc bytes=$(wc -c < "$tmp" 2>/dev/null | tr -d ' ') tmp=$tmp"
    # Rate-limit detection — BUT ONLY when the cycle actually FAILED to do work. Every Claude stream
    # carries a routine `rate_limit_event` whose info includes `overageStatus:rejected` /
    # `out_of_credits` as plan metadata — that is NOT a failure and appears even on fully successful
    # 78KB cycles. Previously we retried on the marker alone, which wrongly backed off on good runs.
    # Real rate-limiting looks like: an ERROR result (429 / "rate limit exceeded" / "usage limit")
    # OR a near-empty cycle (no assistant turns). So: require an actual error signal AND little work.
    local assistant_turns; assistant_turns="$(printf '%s' "$out" | grep -c '"type":"assistant"')"
    local hard_limit=0
    printf '%s' "$out" | grep -qiE 'rate.?limit.{0,40}(exceeded|reached)|usage limit reached|"type":"error"[^}]*(429|rate|overloaded)|too many requests|http 429' && hard_limit=1
    if [ "$hard_limit" = "1" ] && [ "$assistant_turns" -lt 2 ]; then
      rate_limited=1
      if [ "$attempt" -lt "${#backoffs[@]}" ]; then
        local wait="${backoffs[$attempt]}"; attempt=$((attempt+1))
        progress "plan" "running" "" "Claude rate limit hit — backing off ${wait}s then retrying (attempt $attempt/${#backoffs[@]})"
        echo "[$(date '+%F %T')]   ↳ rate limit (error + no work) — waiting ${wait}s before retry (attempt $attempt/${#backoffs[@]})"
        sleep "$wait"; rate_limited=0; continue
      fi
      # Exhausted backoff and still rate-limited: report, reset the run, exit cleanly.
      rm -f "$tmp" 2>/dev/null
      progress "fix" "rate-limited" "" "Claude account is rate-limited / out of credits for this window — run reset; re-queue this ticket after the limit resets"
      echo "[$(date '+%F %T')] /mutate cycle STOPPED — Claude rate-limited after $attempt retries. Resetting run so the ticket can be cleanly re-queued later."
      reset_run "$CUR_RUN"
      CUR_RUN=""; return 0
    fi
    break   # not rate-limited — proceed to normal outcome detection
  done
  rm -f "$tmp" 2>/dev/null

  # HONEST outcome detection — do NOT claim success just because claude exited 0.
  if [ $rc -ne 0 ] || printf '%s' "$out" | grep -qiE "unknown skill|not logged in|please run /login|no such (command|skill)"; then
    local why="cycle failed"
    printf '%s' "$out" | grep -qi "unknown skill" && why="the /mutate skill was not found (.claude/skills/mutate)"
    printf '%s' "$out" | grep -qiE "not logged in|/login" && why="Claude is not logged in for the worker shell"
    # Surface the REAL reason: show the last lines of the cycle output so 'cycle failed' is never a
    # black box. The tail usually carries the actual error (build break, no edit, skill abort, etc.).
    local tail_out; tail_out="$(printf '%s' "$out" | tail -n 6 | tr -d '\r' | sed 's/[[:space:]]\+/ /g')"
    [ -n "$tail_out" ] && echo "[$(date '+%F %T')]   ↳ last output: $tail_out"
    progress "fix" "failed" "" "$why (rc=$rc) — ${tail_out:-see worker log}"
    echo "[$(date '+%F %T')] /mutate cycle FAILED — $why (rc=$rc)"; CUR_RUN=""; return 0
  fi

  # Require an actual NEW open PR before claiming pr-open.
  local after newpr="" newnum=""; after="$(gh pr list --state open --json number,url --jq '.[]|"\(.number) \(.url)"' 2>/dev/null || true)"
  while read -r num url; do
    [ -n "$num" ] || continue
    case ",$before," in *",$num,"*) : ;; *) newpr="$url"; newnum="$num" ;; esac
  done <<< "$after"

  if [ -n "$newpr" ]; then
    progress "pr" "pr-open" "$newpr" "cycle complete — PR opened"
    echo "[$(date '+%F %T')] /mutate cycle complete → $newpr"
    # AUTO_RELEASE: hands-free end-to-end — merge the PR, pull latest, recompile + redeploy Docker.
    # Off by default (review-first). start-worker.cmd can set AUTO_RELEASE=true.
    if [ "${AUTO_RELEASE:-}" = "true" ] && [ -n "$newnum" ]; then
      progress "pr" "running" "$newpr" "AUTO: merging #$newnum + recompiling Docker…"
      echo "[$(date '+%F %T')] AUTO_RELEASE: releasing #$newnum"
      if REPO="$REPO" bash scripts/mutate-ide.sh release "$newnum" 2>&1 | tee -a /tmp/release.$$ ; then
        # Belt-and-suspenders: explicitly close the ticket so it can't be left OPEN if the PR body
        # lacked a "Closes #N" keyword (the squash-merge doesn't always carry the body).
        if [ -n "$CUR_TICKET" ]; then
          gh issue close "$CUR_TICKET" --repo "$REPO" \
            --comment "Fixed and released via PR #$newnum (merged to main, recompiled + redeployed). Closing." 2>/dev/null || true
        fi
        progress "pr" "released" "$newpr" "AUTO: merged #$newnum, closed #${CUR_TICKET}, redeployed ✔"
        echo "[$(date '+%F %T')] AUTO_RELEASE done for #$newnum (closed #${CUR_TICKET})"
      else
        progress "pr" "pr-open" "$newpr" "AUTO release failed — PR #$newnum is open, release manually"
      fi
      rm -f /tmp/release.$$ 2>/dev/null
    fi
  else
    # "No change" is the most confusing outcome — the skill ran but produced no PR. Surface WHY by
    # echoing the tail of its output (what it actually said it did/couldn't do), and check whether it
    # even reached the approval checkpoint or made any commits on a session branch.
    local tail_out; tail_out="$(printf '%s' "$out" | tail -n 8 | tr -d '\r' | sed 's/[[:space:]]\+/ /g')"
    local branch_commits; branch_commits="$(git log --oneline origin/main..HEAD 2>/dev/null | head -3 | tr '\n' ' ')"
    echo "[$(date '+%F %T')]   ↳ no-change diagnostics:"
    echo "[$(date '+%F %T')]     last output: ${tail_out:-<empty — skill produced no text>}"
    [ -n "$branch_commits" ] && echo "[$(date '+%F %T')]     uncommitted-to-main commits: $branch_commits"
    progress "pr" "skipped" "" "no PR — ${tail_out:-skill produced no output; see worker log}"
    echo "[$(date '+%F %T')] /mutate cycle complete — no PR opened (no change)"
  fi
  CUR_RUN=""
}

run_once() {
  load_env            # pick up a refreshed token without needing a worker restart
  heartbeat
  drain_agent_tests   # answer any "Test agent" requests for CLI agents (fast, runs every tick)
  drain_cursor_auth   # run cursor-agent login for any queued cursor-cli auth requests
  if drain_queue; then
    run_cycle
    stop_heartbeat   # cycle done — back to the foreground per-tick heartbeat
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
