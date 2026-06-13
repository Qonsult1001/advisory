#!/usr/bin/env bash
# mutate-ide.sh — infrastructure for the Advisory evolution cycle. PR-ONLY.
#
# Based on an MIT-licensed evolution harness (see NOTICE). Stripped to the pieces a
# .NET+React repo needs, and hardened so it can ONLY ever open a pull request — it never pushes to
# the default branch and never merges.
#
# Subcommands:  setup | finish
# Env:  REPO (owner/name, default = origin), LABEL (default "evolve"), DEFAULT_BRANCH (default main)
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

# ---- Tool resolution (works in Git-Bash AND WSL) ----
# The /mutate cycle may run inside WSL, where the .NET SDK and GitHub CLI are NOT installed as Linux
# binaries — only the Windows .exe versions exist (reachable via /mnt/c, runnable through WSL interop).
# So bare `dotnet`/`gh` resolve in Git-Bash but FAIL in WSL ("command not found") — which is why the
# cycle reported "0 tickets" and a RED build. Wrapper functions fall back to the Windows .exe when the
# native binary is absent, so the bare call sites below work unchanged in either shell.
_find_exe() {  # _find_exe <name> <path1> [path2...]
  # Prefer an EXPLICIT full path first — `command -v gh.exe` can succeed in a login shell (PATH has
  # the GitHub CLI dir) yet the resolved bare "gh.exe" then fails in a non-login SUBPROCESS where that
  # PATH entry is absent → empty output → release wrongly reports "PR not OPEN". A full path is
  # PATH-independent and always runnable, so check the known install locations BEFORE the PATH name.
  local n="$1"; shift
  for p in "$@"; do [ -x "$p" ] && { echo "$p"; return; }; done
  command -v "$n" >/dev/null 2>&1 && { echo "$n"; return; }
  command -v "$n.exe" >/dev/null 2>&1 && { echo "$n.exe"; return; }
  echo "$n"
}
DOTNET_BIN="$(_find_exe dotnet "/mnt/c/Program Files/dotnet/dotnet.exe" "/c/Program Files/dotnet/dotnet.exe")"
GH_BIN="$(_find_exe gh "/mnt/c/Program Files/GitHub CLI/gh.exe" "/c/Program Files/GitHub CLI/gh.exe")"
dotnet() { "$DOTNET_BIN" "$@"; }
gh()     { "$GH_BIN" "$@"; }
echo "[env] dotnet=$DOTNET_BIN | gh=$GH_BIN"

REPO="${REPO:-$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || echo '')}"
LABEL="${LABEL:-mutation}"
DEFAULT_BRANCH="${DEFAULT_BRANCH:-main}"
DATE="$(date +%Y%m%d-%H%M)"
BRANCH="mutation/session-$DATE"
EVO=".evolve"; mkdir -p "$EVO"

# ── Internal timer gate (when may this cycle connect to GitHub?) ──
# Scheduled like a cron-gated maintenance run: a run only proceeds during allowed hours, so you can keep a
# loop running (mutate-claude.sh --loop) but it only acts on a schedule, not every tick.
#   MUTATE_HOURS  — comma-separated hours (0-23) when a cycle may run. Default: every 4h.
#                   Set "*" to allow any hour. Off-hours print SKIPPED and exit 0.
#   FORCE_RUN=true — bypass the gate (manual run).
MUTATE_HOURS="${MUTATE_HOURS:-0,4,8,12,16,20}"

timer_gate() {
  [ "${FORCE_RUN:-}" = "true" ] && return 0
  [ "$MUTATE_HOURS" = "*" ] && return 0
  local now; now=$((10#$(date +%H)))
  case ",$MUTATE_HOURS," in
    *",$now,"*) return 0 ;;                       # this hour is an allowed slot
    *) echo "SKIPPED — hour $now is not in the schedule [$MUTATE_HOURS]. (FORCE_RUN=true to override.)"; return 1 ;;
  esac
}

die() { echo "evolve-ide: $*" >&2; exit 1; }

build_and_test() {
  # Returns 0 only if build + tests pass. Output captured to .evolve/checks.log.
  : > "$EVO/checks.log"
  echo "→ dotnet build" | tee -a "$EVO/checks.log"
  dotnet build src/Advisory.Api/Advisory.Api.csproj -c Release --nologo >>"$EVO/checks.log" 2>&1 || return 1
  echo "→ dotnet test" | tee -a "$EVO/checks.log"
  dotnet test tests/Advisory.Tests/Advisory.Tests.csproj --nologo >>"$EVO/checks.log" 2>&1 || return 1
  if git diff --name-only "$DEFAULT_BRANCH"...HEAD 2>/dev/null | grep -q '^web/'; then
    echo "→ npm build (web changed)" | tee -a "$EVO/checks.log"
    npm --prefix web run build >>"$EVO/checks.log" 2>&1 || return 1
  fi
  return 0
}

case "${1:-}" in
  setup)
    [ -n "$REPO" ] || die "no REPO and no gh remote — set REPO=owner/name"
    # Internal timer: only connect to GitHub during allowed hours.
    timer_gate || exit 0
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
You are evolving the Advisory codebase to address the tickets in .evolve/ISSUES_TODAY.md.
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

    # Commit any stragglers — but ONLY product/agent files, never accidental junk. A blanket
    # `git add -A` once swept a literal C:\Users\... cache tree into a 384-file PR. Stage explicit
    # paths instead, and hard-refuse if a Windows-home path ever sneaks into the index.
    git add -A -- src web tests scripts evolution skills .claude .github \
      RESEARCH.md JOURNAL.md DAY_COUNT SESSION_PLAN.md memory Dockerfile docker-compose.yml \
      *.md 2>/dev/null || true
    if git diff --cached --name-only | grep -qiE 'C:.*Users|/\.npm/|shell-snapshots'; then
      die "refusing to commit: junk path staged (Windows home / npm cache). Clean the working tree first."
    fi
    git commit -m "mutate: session wrap-up ($DATE)" 2>/dev/null || true

    # PUSH THE BRANCH ONLY — never the default branch.
    git push -u origin "$BRANCH" || die "push failed (auth?)"

    TICKETS="$(grep -oE '#[0-9]+' "$EVO/ISSUES_TODAY.md" | sort -u | tr '\n' ' ')"
    # "Closes #N" lines so merging the PR auto-closes each addressed ticket on GitHub.
    CLOSES="$(grep -oE '#[0-9]+' "$EVO/ISSUES_TODAY.md" | tr -d '#' | sort -u | sed 's/^/Closes #/' | tr '\n' ' ')"
    BODY="🤖 Automated evolution session $DATE.

$CLOSES

Addresses: $TICKETS
Tests: $([ "$TESTS" = pass ] && echo '✅ passing' || echo '⚠️ not passing — draft for review')

Written by the mutation cycle. **For human review — will not auto-merge.**

<details><summary>checks (tail)</summary>

\`\`\`
$(tail -40 "$EVO/checks.log" 2>/dev/null)
\`\`\`
</details>"

    PR_URL="$(gh pr create --repo "$REPO" --base "$DEFAULT_BRANCH" --head "$BRANCH" $DRAFT \
      --title "mutate: session $DATE" --body "$BODY" 2>&1 | grep -oE 'https://[^ ]+' | tail -1)"
    echo "PR: ${PR_URL:-<create failed>}"

    # Reply on each ticket with the PR link.
    for n in $(grep -oE '#[0-9]+' "$EVO/ISSUES_TODAY.md" | tr -d '#' | sort -u); do
      gh issue comment "$n" --repo "$REPO" \
        --body "🤖 Evolution opened a $([ -n "$DRAFT" ] && echo 'draft ')PR addressing this: ${PR_URL:-(see branch $BRANCH)}. Tests $([ "$TESTS" = pass ] && echo 'passed ✅' || echo 'need review ⚠️'). A human will review before merge." >/dev/null 2>&1 || true
    done
    echo "FINISH OK"
    ;;

  release)
    # OPERATOR-TRIGGERED end-to-end: merge a reviewed PR, pull latest into the working tree,
    # then recompile + redeploy Docker so what's running == main. NEVER called by the autonomous
    # loop — the loop only ever opens a PR (finish). You run this when you're satisfied with a PR.
    #   scripts/mutate-ide.sh release            # merge the PR for the current branch
    #   scripts/mutate-ide.sh release 5          # merge PR #5
    [ -n "$REPO" ] || die "no REPO"
    PR="${2:-}"
    if [ -z "$PR" ]; then
      PR="$(gh pr view --repo "$REPO" --json number --jq .number 2>/dev/null)"
      [ -n "$PR" ] || die "no PR for the current branch — pass a PR number: release <N>"
    fi

    # Safety gate: only release an OPEN PR. Two GitHub-lag traps here, both fixed:
    #  1) `.mergeable` is null right after creation → don't gate on it (display only).
    #  2) Even `.state` can come back EMPTY for a few seconds right after a PR is created (the release
    #     runs immediately after `finish` opens the PR). So RETRY the state read briefly instead of
    #     bailing on the first empty result.
    STATE=""
    for _try in 1 2 3 4 5 6; do
      STATE="$(gh pr view "$PR" --repo "$REPO" --json state --jq '.state' 2>/dev/null)"
      [ -n "$STATE" ] && break
      echo "→ PR #$PR state not ready yet (try $_try) — waiting 3s…"; sleep 3
    done
    MERGEABLE="$(gh pr view "$PR" --repo "$REPO" --json mergeable --jq '.mergeable // "UNKNOWN"' 2>/dev/null)"
    echo "→ PR #$PR state: ${STATE:-<unknown>} (mergeable: ${MERGEABLE:-UNKNOWN})"
    [ "$STATE" = "OPEN" ] || die "PR #$PR is not OPEN (state=${STATE:-<empty>}) — nothing to release"

    echo "→ merging PR #$PR (squash, delete branch)"
    gh pr merge "$PR" --repo "$REPO" --squash --delete-branch || die "merge failed (conflicts? checks? perms?)"

    # ALWAYS pull latest into the working tree — all changes live in GitHub now.
    echo "→ syncing working tree to origin/$DEFAULT_BRANCH"
    git checkout "$DEFAULT_BRANCH" 2>/dev/null || git checkout -B "$DEFAULT_BRANCH"
    git fetch origin "$DEFAULT_BRANCH" --quiet || die "fetch failed"
    git reset --hard "origin/$DEFAULT_BRANCH" || die "could not fast-forward to origin/$DEFAULT_BRANCH"
    echo "→ now at: $(git log --oneline -1)"

    # Recompile + redeploy so the running stack matches main.
    # PIN the project name to 'advisory' — otherwise compose derives it from the cwd (which differs
    # in a worktree / WSL path like /mnt/g/...), spins up a SECOND stack, and its nexus collides on
    # port 8081 ("port is already allocated"). -p advisory always targets the real running stack.
    if command -v docker >/dev/null 2>&1; then
      echo "→ docker compose -p advisory build api console"
      docker compose -p advisory build api console || die "docker build failed"
      echo "→ docker compose -p advisory up -d api console (no-deps: don't touch nexus/mssql)"
      docker compose -p advisory up -d --no-deps api console || die "docker up failed"
      echo "RELEASE OK — merged #$PR, pulled $DEFAULT_BRANCH, rebuilt + redeployed."
    else
      echo "RELEASE OK — merged #$PR and pulled $DEFAULT_BRANCH. (docker not found; recompile manually.)"
    fi
    ;;

  *)
    echo "usage: $0 {setup|finish|release [PR#]}"; exit 1 ;;
esac
