#!/usr/bin/env bash
# evolve-claude.sh — run one /evolve cycle via the Claude Code CLI (uses your existing Claude login).
#
# Adapted from yoyo-evolve (MIT, https://github.com/yologdev/yoyo-evolve).
#
# Usage:
#   ./scripts/evolve-claude.sh           # run once
#   ./scripts/evolve-claude.sh --loop 1h # run every hour (local only; CI uses the workflow)
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
