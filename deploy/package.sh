#!/usr/bin/env bash
# Build the deliverable: a single advisory-rollout-<date>.tar.gz an IT team extracts on a Linux host
# and installs with ./install.sh. Bundles the deploy/ folder + exactly the source the compose builds
# from (the API, web console, and scanner build contexts) — nothing else from the repo.
#
# Run from the repo root:  ./deploy/package.sh   (or from deploy/:  ./package.sh)
set -euo pipefail

# Resolve repo root (one level above this script's deploy/ folder).
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

STAMP="$(date +%Y%m%d 2>/dev/null || echo build)"
OUT="advisory-rollout-${STAMP}.tar.gz"
STAGE="$(mktemp -d)"
ROOT="$STAGE/advisory-rollout"
mkdir -p "$ROOT"

echo "→ Staging the rollout bundle…"

# The deploy folder (compose, installers, env template, docs) — minus local-only files.
mkdir -p "$ROOT/deploy"
( cd deploy && tar --exclude='.env' --exclude='data' --exclude='package.sh' -cf - . ) | ( cd "$ROOT/deploy" && tar -xf - )

# The build contexts the compose needs. Paths mirror the repo so compose's relative contexts resolve.
#   api + console build from repo root (Dockerfile, web/, src/, etc.); scanners from tools/.
echo "→ Copying build sources…"
# Use git archive when available (respects .gitignore, smallest), else a filtered copy.
if command -v git >/dev/null 2>&1 && git rev-parse --git-dir >/dev/null 2>&1; then
  git archive --format=tar HEAD \
    Dockerfile web src tools/privacy-filter tools/vsix-scanner \
    $(git ls-files '*.csproj' '*.sln' 'src/**' 2>/dev/null | head -0) \
    2>/dev/null | ( cd "$ROOT" && tar -xf - ) || {
      echo "  (git archive partial — falling back to copy)"; }
fi
# Ensure the essentials are present even if git archive missed paths.
for p in Dockerfile web src tools/privacy-filter tools/vsix-scanner *.sln; do
  for match in $p; do
    [ -e "$match" ] || continue
    mkdir -p "$ROOT/$(dirname "$match")"
    cp -r "$match" "$ROOT/$(dirname "$match")/" 2>/dev/null || true
  done
done

# Top-level quick-start so whoever extracts it knows what to do.
cat > "$ROOT/INSTALL.txt" <<'TXT'
Advisory rollout
================
1. cd deploy
2. cp .env.example .env   &&   edit .env   (set SQL_SA_PASSWORD)
3. chmod +x install.sh    (if needed)
4. ./install.sh           (add --scanners to also START the optional scanners)
5. Open the console at http://<host>:8088  →  follow deploy/docs/TUTORIAL-gate-your-first-package.md

Requires Docker OR Podman (with the compose plugin). See deploy/README.md for full details.
TXT

chmod +x "$ROOT/deploy/install.sh" 2>/dev/null || true

echo "→ Creating $OUT…"
( cd "$STAGE" && tar -czf - advisory-rollout ) > "$REPO_ROOT/$OUT"
rm -rf "$STAGE"

echo "✓ Done: $REPO_ROOT/$OUT"
echo "  Hand this file to IT. They run:  tar -xzf $OUT && cd advisory-rollout/deploy && ./install.sh"
