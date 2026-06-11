#!/usr/bin/env bash
# evolve-claude.sh — the evolution timer. Runs /evolve via the Claude Code CLI on a loop.
#
# Adapted from yoyo-evolve (MIT, https://github.com/yologdev/yoyo-evolve).
#
# HOW IT WORKS (no API key, no GitHub secret):
#   • `claude -p "/evolve"` uses your EXISTING Claude Code login (Pro/Max). You're already
#     authenticated if you can run `claude`. There is NOTHING to configure for auth.
#   • GitHub access is via the `gh` CLI being logged in (`gh auth login`).
#   • Keep this looping; the INTERNAL timer in scripts/evolve-ide.sh (EVOLVE_HOURS) decides which
#     ticks actually connect to GitHub and do work — off-schedule ticks print SKIPPED and cost nothing.
#   • PR-only: every change becomes a pull request for human review.
#
# Usage:
#   ./scripts/evolve-claude.sh            # run one cycle now (subject to the hour gate)
#   ./scripts/evolve-claude.sh --loop 30m # tick every 30 min; acts only during EVOLVE_HOURS
#   FORCE_RUN=true ./scripts/evolve-claude.sh   # bypass the schedule, act immediately
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

run_once() {
  echo "[$(date '+%F %T')] /evolve cycle start"
  # Claude Code executes the /evolve command (see .claude/commands/evolve.md).
  claude -p --dangerously-skip-permissions --verbose "/evolve" 2>&1
  echo "[$(date '+%F %T')] /evolve cycle complete"
}

secs() { local v="$1" n="${1%[smhSMH]}" u="${1##*[0-9]}"; case "$u" in s|S) echo "$n";; m|M) echo $((n*60));; h|H) echo $((n*3600));; *) echo $((n*60));; esac; }

case "${1:-}" in
  --loop) i="$(secs "${2:-1h}")"; echo "loop every ${2:-1h} (${i}s)"; while true; do run_once || echo "cycle failed; retrying next interval"; sleep "$i"; done ;;
  --help|-h) echo "usage: $0 [--loop INTERVAL]";;
  "") run_once ;;
  *) echo "unknown: $1"; exit 1 ;;
esac
