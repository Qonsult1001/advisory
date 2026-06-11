#!/usr/bin/env bash
# evolve-claude.sh — the EVOLUTION (research) timer. Runs /evolve via the Claude Code CLI on a loop.
#
# Twin of mutate-claude.sh, but for the forward-looking RESEARCH loop. Where /mutate fixes bugs and
# opens code PRs, /evolve studies the supply-chain security landscape and records findings into
# RESEARCH.md + memory/ — it NEVER edits product code.
#
# Based on an MIT-licensed evolution harness (see NOTICE).
#
# HOW IT WORKS (no API key, no GitHub secret):
#   • `claude -p "/evolve"` uses your EXISTING Claude Code login. Nothing to configure for auth.
#   • Keep this looping; the WEEKLY gate in scripts/evolve-ide.sh decides which ticks act — off-schedule
#     ticks print SKIPPED and cost nothing. The dashboard "Run research now" button bypasses the gate
#     by dropping a research-*.request in the queue, which this loop drains.
#   • Output is a RESEARCH.md/memory PR (PR-only) — never product code.
#
# DASHBOARD BUTTON: the web "Run research now" button drops research-*.request into the queue dir.
#   The API writes to /data/evolution-queue, bind-mounted to ./.evolution-queue, so this loop reads
#   the same files from the host.
#
# Usage:
#   ./scripts/evolve-claude.sh            # run one research cycle now (subject to the weekly gate)
#   ./scripts/evolve-claude.sh --loop 6h  # tick every 6h; acts only during the weekly window
#   FORCE_RUN=true ./scripts/evolve-claude.sh   # bypass the schedule, research now
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

QUEUE_DIR="${EVOLUTION_QUEUE_DIR:-./.evolution-queue}"

drain_queue() {
  # A research-*.request means "run now" was clicked — consume it and force a run this tick.
  [ -d "$QUEUE_DIR" ] || return 0
  local forced=1
  for req in "$QUEUE_DIR"/research-*.request; do
    [ -e "$req" ] || continue
    echo "[$(date '+%F %T')] dashboard research request: $(basename "$req")"
    rm -f "$req"
    forced=0
  done
  return $forced   # 0 = a request existed (force this run), 1 = none
}

stamp_last_run() {
  mkdir -p "$QUEUE_DIR" 2>/dev/null || true
  date -u +%Y-%m-%dT%H:%M:%SZ > "$QUEUE_DIR/research.last" 2>/dev/null || true
}

run_once() {
  if drain_queue; then export FORCE_RUN=true; fi   # dashboard "run now" overrides the weekly gate
  echo "[$(date '+%F %T')] /evolve cycle start"
  # Claude Code executes the /evolve command (see .claude/commands/evolve.md). The weekly gate lives
  # in evolve-ide.sh; the cycle writes RESEARCH.md/memory and opens a PR — no product code.
  claude -p --dangerously-skip-permissions --verbose "/evolve" 2>&1
  stamp_last_run
  echo "[$(date '+%F %T')] /evolve cycle complete"
}

secs() { local v="$1" n="${1%[smhSMH]}" u="${1##*[0-9]}"; case "$u" in s|S) echo "$n";; m|M) echo $((n*60));; h|H) echo $((n*3600));; *) echo $((n*60));; esac; }

case "${1:-}" in
  --loop) i="$(secs "${2:-6h}")"; echo "research loop every ${2:-6h} (${i}s)"; while true; do run_once || echo "cycle failed; retrying next interval"; sleep "$i"; done ;;
  --help|-h) echo "usage: $0 [--loop INTERVAL]";;
  "") run_once ;;
  *) echo "unknown: $1"; exit 1 ;;
esac
