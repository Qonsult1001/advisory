#!/usr/bin/env bash
# evolve-ide.sh — infrastructure for the PkgFirewall evolution cycle. PR-ONLY.
#
# Adapted from yoyo-evolve (MIT, https://github.com/yologdev/yoyo-evolve). Stripped to the pieces a
# .NET+React repo needs, and hardened so it can ONLY ever open a pull request — it never pushes to
# the default branch and never merges.
#
# Subcommands:  setup | finish
# Env:  REPO (owner/name, default = origin), LABEL (default "evolve"), DEFAULT_BRANCH (default main)
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

REPO="${REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || echo '')}"
LABEL="${LABEL:-evolve}"
DEFAULT_BRANCH="${DEFAULT_BRANCH:-main}"
DATE="$(date +%Y%m%d-%H%M)"
BRANCH="evolve/session-$DATE"
EVO=".evolve"; mkdir -p "$EVO"

die() { echo "evolve-ide: $*" >&2; exit 1; }

build_and_test() {
  # Returns 0 only if build + tests pass. Output captured to .evolve/checks.log.
  : > "$EVO/checks.log"
  echo "→ dotnet build" | tee -a "$EVO/checks.log"
  dotnet build src/PkgFirewall.Api/PkgFirewall.Api.csproj -c Release --nologo >>"$EVO/checks.log" 2>&1 || return 1
  echo "→ dotnet test" | tee -a "$EVO/checks.log"
  dotnet test tests/PkgFirewall.Tests/PkgFirewall.Tests.csproj --nologo >>"$EVO/checks.log" 2>&1 || return 1
  if git diff --name-only "$DEFAULT_BRANCH"...HEAD 2>/dev/null | grep -q '^web/'; then
    echo "→ npm build (web changed)" | tee -a "$EVO/checks.log"
    npm --prefix web run build >>"$EVO/checks.log" 2>&1 || return 1
  fi
  return 0
}

case "${1:-}" in
  setup)
    [ -n "$REPO" ] || die "no REPO and no gh remote — set REPO=owner/name"
    echo "Repo: $REPO | Label: $LABEL | Session branch: $BRANCH"

    # Fetch open tickets with the evolve label, plus their comments (tester replies).
    gh issue list --repo "$REPO" --state open --label "$LABEL" \
      --json number,title,body,author,comments \
      --jq '.[] | "## Issue #\(.number): \(.title)\nby @\(.author.login)\n\n\(.body)\n" + (if (.comments|length)>0 then "\n### Tester comments:\n" + (.comments|map("- @\(.author.login): \(.body)")|join("\n")) + "\n" else "" end)' \
      > "$EVO/ISSUES_TODAY.md" 2>/dev/null || echo "" > "$EVO/ISSUES_TODAY.md"

    if [ ! -s "$EVO/ISSUES_TODAY.md" ]; then
      echo "NO WORK — no open issues labelled '$LABEL'."
      exit 0
    fi

    # Start the session branch from the default branch.
    git fetch origin "$DEFAULT_BRANCH" --quiet 2>/dev/null || true
    git checkout -B "$BRANCH" "origin/$DEFAULT_BRANCH" 2>/dev/null || git checkout -B "$BRANCH"
    echo "$BRANCH" > "$EVO/branch"

    cat > "$EVO/plan_prompt.md" <<EOF
You are evolving the PkgFirewall codebase to address the tickets in .evolve/ISSUES_TODAY.md.
Plan one focused task per ticket: the smallest correct change plus a test. Score by Impact×Urgency
(see skills/plan/SKILL.md). Do not exceed the tickets' scope. Write SESSION_PLAN.md.
EOF
    # baseline build (informational)
    build_and_test && echo "→ baseline: build+tests green" || echo "→ baseline: build/tests RED (see .evolve/checks.log)"
    echo "SETUP OK — $(grep -c '^## Issue' "$EVO/ISSUES_TODAY.md") ticket(s) to address."
    ;;

  finish)
    [ -n "$REPO" ] || die "no REPO"
    BRANCH="$(cat "$EVO/branch" 2>/dev/null || git rev-parse --abbrev-ref HEAD)"
    [ "$BRANCH" != "$DEFAULT_BRANCH" ] || die "refusing to operate on the default branch ($DEFAULT_BRANCH)"

    # Final verification → determines clean PR vs draft.
    if build_and_test; then TESTS=pass; DRAFT=""; else TESTS=fail; DRAFT="--draft"; fi
    echo "→ final checks: $TESTS"

    # Commit any stragglers.
    git add -A && git commit -m "evolve: session wrap-up ($DATE)" 2>/dev/null || true

    # PUSH THE BRANCH ONLY — never the default branch.
    git push -u origin "$BRANCH" || die "push failed (auth?)"

    TICKETS="$(grep -oE '#[0-9]+' "$EVO/ISSUES_TODAY.md" | sort -u | tr '\n' ' ')"
    BODY="🤖 Automated evolution session $DATE.

Addresses: $TICKETS
Tests: $([ "$TESTS" = pass ] && echo '✅ passing' || echo '⚠️ not passing — draft for review')

Written by the evolution cycle (Claude Code). **For human review — will not auto-merge.**

<details><summary>checks (tail)</summary>

\`\`\`
$(tail -40 "$EVO/checks.log" 2>/dev/null)
\`\`\`
</details>"

    PR_URL="$(gh pr create --repo "$REPO" --base "$DEFAULT_BRANCH" --head "$BRANCH" $DRAFT \
      --title "evolve: session $DATE" --body "$BODY" 2>&1 | grep -oE 'https://[^ ]+' | tail -1)"
    echo "PR: ${PR_URL:-<create failed>}"

    # Reply on each ticket with the PR link.
    for n in $(grep -oE '#[0-9]+' "$EVO/ISSUES_TODAY.md" | tr -d '#' | sort -u); do
      gh issue comment "$n" --repo "$REPO" \
        --body "🤖 Evolution opened a $([ -n "$DRAFT" ] && echo 'draft ')PR addressing this: ${PR_URL:-(see branch $BRANCH)}. Tests $([ "$TESTS" = pass ] && echo 'passed ✅' || echo 'need review ⚠️'). A human will review before merge." >/dev/null 2>&1 || true
    done
    echo "FINISH OK"
    ;;

  *)
    echo "usage: $0 {setup|finish}"; exit 1 ;;
esac
