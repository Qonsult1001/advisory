#!/usr/bin/env bash
# evolve-ide.sh — infrastructure for the Advisory EVOLUTION (research) cycle. PR-ONLY, RESEARCH ONLY.
#
# Twin of mutate-ide.sh. Where mutate fixes code, evolve studies the landscape and records findings
# into RESEARCH.md + memory/. It is HARDENED so it can only ever touch those paths and open a PR —
# it never edits src/ or web/, never pushes to the default branch, never merges.
#
# Subcommands:  setup | finish
# Env:  REPO (owner/name, default = origin), DEFAULT_BRANCH (default main)
#       RESEARCH_SCHEDULE_DOW (0-6, default 0=Sun), RESEARCH_SCHEDULE_HOUR (0-23, default 2)
#       FORCE_RUN=true  — bypass the weekly gate (manual / "run now")
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

REPO="${REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || echo '')}"
DEFAULT_BRANCH="${DEFAULT_BRANCH:-main}"
DATE="$(date +%Y%m%d-%H%M)"
BRANCH="evolution/research-$DATE"
DOW="${RESEARCH_SCHEDULE_DOW:-0}"
HOUR="${RESEARCH_SCHEDULE_HOUR:-2}"

die() { echo "evolve-ide: $*" >&2; exit 1; }

# ── Weekly gate: only research during the scheduled day-of-week + hour window (UTC). ──
weekly_gate() {
  [ "${FORCE_RUN:-}" = "true" ] && return 0
  local now_dow now_hour
  now_dow="$(date -u +%w)"      # 0-6, Sun=0
  now_hour="$((10#$(date -u +%H)))"
  if [ "$now_dow" = "$DOW" ] && [ "$now_hour" = "$HOUR" ]; then return 0; fi
  echo "SKIPPED — research runs weekly on dow=$DOW hour=$HOUR UTC (now dow=$now_dow hour=$now_hour). FORCE_RUN=true to override."
  return 1
}

case "${1:-}" in
  setup)
    [ -n "$REPO" ] || die "no REPO and no gh remote — set REPO=owner/name"
    weekly_gate || exit 0
    echo "Repo: $REPO | Research branch: $BRANCH | Schedule: weekly dow=$DOW hour=$HOUR UTC"
    git fetch origin "$DEFAULT_BRANCH" --quiet 2>/dev/null || true
    git checkout -B "$BRANCH" "origin/$DEFAULT_BRANCH" 2>/dev/null || git checkout -B "$BRANCH"
    echo "$BRANCH" > ".evolve-research-branch"
    echo "SETUP OK — research on $BRANCH. /evolve writes RESEARCH.md + memory/ only."
    ;;

  finish)
    [ -n "$REPO" ] || die "no REPO"
    BRANCH="$(cat ".evolve-research-branch" 2>/dev/null || git rev-parse --abbrev-ref HEAD)"
    [ "$BRANCH" != "$DEFAULT_BRANCH" ] || die "refusing to operate on the default branch ($DEFAULT_BRANCH)"

    # SAFETY: research must NOT have touched product code. Abort the PR if it did.
    if git diff --name-only "origin/$DEFAULT_BRANCH"...HEAD 2>/dev/null | grep -qE '^(src/|web/)'; then
      die "research changed src/ or web/ — that is a mutation, not evolution. Refusing to open a research PR."
    fi

    # Commit only RESEARCH.md + memory changes.
    git add RESEARCH.md memory 2>/dev/null || true
    git commit -m "evolve: research session $DATE" 2>/dev/null || echo "(nothing new to commit)"

    git push -u origin "$BRANCH" || die "push failed (auth?)"
    PR_URL="$(gh pr create --repo "$REPO" --base "$DEFAULT_BRANCH" --head "$BRANCH" \
      --title "evolve: research session $DATE" \
      --body "🔬 Evolution research session $DATE. Findings recorded in RESEARCH.md + memory/. **No product code changed** — approve a finding in the dashboard to file a mutation ticket. PR-only." \
      2>&1 | grep -oE 'https://[^ ]+' | tail -1)"
    echo "PR: ${PR_URL:-<create failed>}"
    echo "FINISH OK"
    ;;

  *)
    echo "usage: $0 {setup|finish}"; exit 1 ;;
esac
