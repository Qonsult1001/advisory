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

QUEUE_DIR="${EVOLUTION_QUEUE_DIR:-./.evolution-queue}"
API="${ADVISORY_API:-http://localhost:5000/api}"
CUR_RUN=""    # run id we are currently reporting progress for

# ---- progress reporting to the dashboard (best-effort; never fails the cycle) ----
api_post() { curl -s -m 5 -X POST "$API/$1" -H "Content-Type: application/json" -d "$2" >/dev/null 2>&1 || true; }
heartbeat() { api_post "evolution/worker/ping" '{}'; }
progress() {   # progress <stage> [status] [prUrl] [logline]
  [ -n "$CUR_RUN" ] || return 0
  local stage="$1" status="${2:-}" pr="${3:-}" log="${4:-}"
  api_post "evolution/run/$CUR_RUN/progress" \
    "$(printf '{"stage":"%s","status":"%s","prUrl":"%s","log":"%s"}' "$stage" "$status" "$pr" "$log")"
}

# Pull the next queued run id from the API (so we report against the dashboard's run row).
next_run_id() { curl -s -m 5 "$API/evolution/next" 2>/dev/null | grep -oE '"id":"[a-z0-9]+"' | head -1 | cut -d'"' -f4; }

drain_queue() {   # returns 0 if a request was found (work to do), 1 if none
  [ -d "$QUEUE_DIR" ] || return 1
  local found=1
  for req in "$QUEUE_DIR"/ticket-*.request; do
    [ -e "$req" ] || continue
    # request format: line1=ticket, line2=runId, line3=timestamp
    CUR_RUN="$(sed -n '2p' "$req" 2>/dev/null | tr -d '[:space:]')"
    echo "[$(date '+%F %T')] picked up $(basename "$req") (run=${CUR_RUN:-?})"
    rm -f "$req"
    found=0
  done
  [ -z "$CUR_RUN" ] && CUR_RUN="$(next_run_id)"   # fall back to whatever the API has queued
  return $found
}

run_cycle() {
  # Report milestones around the phases the script controls. Claude does plan→test→fix internally;
  # we mark the coarse, real boundaries so the bar reflects genuine progress, not a fake timer.
  progress "setup" "running" "" "worker picked up the ticket"
  echo "[$(date '+%F %T')] /mutate cycle start (run=${CUR_RUN:-?})"

  progress "plan" "running" "" "planning + implementing the fix (Claude)"
  # Claude Code executes the /mutate command (see .claude/commands/mutate.md): plan, write a failing
  # test, implement, build, test, open PR. This is the long step.
  if claude -p --dangerously-skip-permissions --verbose "/mutate" 2>&1; then
    # Try to discover the PR the cycle just opened for this run's branch.
    local pr=""
    pr="$(gh pr list --state open --json url,headRefName --jq '.[0].url' 2>/dev/null || true)"
    progress "pr" "pr-open" "$pr" "cycle complete — PR opened for review"
    echo "[$(date '+%F %T')] /mutate cycle complete → ${pr:-(see GitHub PRs)}"
  else
    progress "fix" "failed" "" "cycle failed — see worker log"
    echo "[$(date '+%F %T')] /mutate cycle FAILED"
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
