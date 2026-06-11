#!/usr/bin/env bash
# mutate-claude.sh — the mutation timer. Runs /mutate via the Claude Code CLI on a loop.
#
# Based on an MIT-licensed evolution harness (see NOTICE).
#
# HOW IT WORKS (no API key, no GitHub secret):
#   • `claude -p "/mutate"` uses your EXISTING Claude Code login (Pro/Max). You're already
#     authenticated if you can run `claude`. There is NOTHING to configure for auth.
#   • GitHub access is via the `gh` CLI being logged in (`gh auth login`).
#   • Keep this looping; the INTERNAL timer in scripts/mutate-ide.sh (MUTATE_HOURS) decides which
#     ticks actually connect to GitHub and do work — off-schedule ticks print SKIPPED and cost nothing.
#   • PR-only: every change becomes a pull request for human review.
#
# DASHBOARD BUTTON: the web "Mutate" button labels the ticket and drops a request file in the
#   queue dir. The API container writes to /data/evolution-queue, which docker-compose BIND MOUNTS
#   to ./.evolution-queue in this repo — so this loop reads the SAME files from the host. No volume
#   spelunking needed. (Override the host path with EVOLUTION_QUEUE_HOST in compose / EVOLUTION_QUEUE_DIR here.)
#   Even if the file is missed, /mutate acts on whatever tickets carry the `mutation` label, so the button works.
#
# Usage:
#   ./scripts/mutate-claude.sh            # run one cycle now (subject to the hour gate)
#   ./scripts/mutate-claude.sh --loop 30m # tick every 30 min; acts only during MUTATE_HOURS
#   FORCE_RUN=true ./scripts/mutate-claude.sh   # bypass the schedule, act immediately
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

# Dashboard "Mutate" button queues ticket requests here; we drain them on each run.
# Default matches the docker-compose bind mount (./.evolution-queue:/data/evolution-queue).
QUEUE_DIR="${EVOLUTION_QUEUE_DIR:-./.evolution-queue}"

drain_queue() {
  [ -d "$QUEUE_DIR" ] || return 0
  for req in "$QUEUE_DIR"/ticket-*.request; do
    [ -e "$req" ] || continue
    echo "[$(date '+%F %T')] dashboard request: $(basename "$req")"
    rm -f "$req"            # consume the request (the labelled ticket is what the cycle acts on)
  done
}

run_once() {
  drain_queue            # honor any dashboard-queued requests (they just ensure a labelled ticket exists)
  echo "[$(date '+%F %T')] /mutate cycle start"
  # Claude Code executes the /mutate command (see .claude/commands/mutate.md). FORCE_RUN bypasses
  # the EVOLVE_HOURS gate so a manual/queued run acts immediately.
  claude -p --dangerously-skip-permissions --verbose "/mutate" 2>&1
  echo "[$(date '+%F %T')] /mutate cycle complete"
}

secs() { local v="$1" n="${1%[smhSMH]}" u="${1##*[0-9]}"; case "$u" in s|S) echo "$n";; m|M) echo $((n*60));; h|H) echo $((n*3600));; *) echo $((n*60));; esac; }

case "${1:-}" in
  --loop) i="$(secs "${2:-1h}")"; echo "loop every ${2:-1h} (${i}s)"; while true; do run_once || echo "cycle failed; retrying next interval"; sleep "$i"; done ;;
  --help|-h) echo "usage: $0 [--loop INTERVAL]";;
  "") run_once ;;
  *) echo "unknown: $1"; exit 1 ;;
esac
