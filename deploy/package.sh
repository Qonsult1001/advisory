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
# By default .env is EXCLUDED (don't ship secrets). Pass --include-keys to bundle the real deploy/.env
# so the deliverable arrives pre-configured and works out of the box (rotate those keys afterwards).
mkdir -p "$ROOT/deploy"
if [ "${1:-}" = "--include-keys" ] && [ -f deploy/.env ]; then
  echo "→ Including deploy/.env (live keys) in the bundle — ROTATE these keys after hand-off."
  ( cd deploy && tar --exclude='data' --exclude='package.sh' -cf - . ) | ( cd "$ROOT/deploy" && tar -xf - )
else
  ( cd deploy && tar --exclude='.env' --exclude='data' --exclude='package.sh' -cf - . ) | ( cd "$ROOT/deploy" && tar -xf - )
fi

# The build contexts the compose needs. Paths mirror the repo so compose's relative contexts resolve.
#   api + console build from repo root (Dockerfile, web/, src/, etc.); scanners from tools/.
echo "→ Copying build sources…"
# git archive exports ONLY tracked files (respects .gitignore) — so node_modules, dist, .env, data/,
# bin/obj never get in. This is required: shipping node_modules would break the image build.
if command -v git >/dev/null 2>&1 && git rev-parse --git-dir >/dev/null 2>&1; then
  git archive --format=tar HEAD \
    Dockerfile web src tools/privacy-filter tools/vsix-scanner tools/reachability \
    | ( cd "$ROOT" && tar -xf - )
else
  echo "  (no git — copying with excludes)"
  for src in Dockerfile web src tools/privacy-filter tools/vsix-scanner tools/reachability; do
    [ -e "$src" ] || continue
    mkdir -p "$ROOT/$(dirname "$src")"
    rsync -a --exclude 'node_modules' --exclude 'dist' --exclude '.env' --exclude 'bin' \
      --exclude 'obj' --exclude 'data' "$src" "$ROOT/$(dirname "$src")/" 2>/dev/null \
      || cp -r "$src" "$ROOT/$(dirname "$src")/"
  done
fi
# GITIGNORED build deps the API Dockerfile COPYs (so they're missed by git archive): the said binaries,
# the brain file, and RESEARCH.md. Copy them in explicitly, or the API image build fails on COPY.
echo "→ Adding gitignored build deps (said binaries, Advisory.said, RESEARCH.md)…"
for extra in tools/said/said-linux tools/said/said-orchestrate-linux Advisory.said RESEARCH.md; do
  if [ -e "$extra" ]; then
    mkdir -p "$ROOT/$(dirname "$extra")"
    cp "$extra" "$ROOT/$extra"
  else
    echo "  ! WARNING: $extra not found — the API image build will fail without it."
  fi
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

# Output into a dedicated handoff/ folder (kept out of git), with a tiny INSTALL.md beside the archive.
HANDOFF="$REPO_ROOT/handoff"
mkdir -p "$HANDOFF"
echo "→ Creating handoff/$OUT…"
( cd "$STAGE" && tar -czf - advisory-rollout ) > "$HANDOFF/$OUT"
rm -rf "$STAGE"

cat > "$HANDOFF/INSTALL.md" <<TXT
# Advisory - install (Podman)

You have one file: \`$OUT\`. It contains the full Advisory firewall, pre-configured and ready to run.

## Requirements
- Linux with **Podman 4.x or newer** plus a compose tool (\`podman compose\` or \`podman-compose\`).
  Docker also works; the installer auto-detects whichever you have. Check: \`podman --version\`.
- ~6 GB free disk, ~4 GB RAM. Outbound HTTPS on first run (see deploy/README.md section 1a).

> Use a current Podman - 3.4.x (old Ubuntu 22.04 default) has incomplete Compose support. Upgrade
> with the Kubic repo if needed; see deploy/README.md for the exact commands.

## Install
\`\`\`
tar -xzf $OUT
cd advisory-rollout/deploy
./install.sh            # add --scanners to also start the PII + extension scanners
\`\`\`
It is already configured and auto-detects Podman. The installer builds the images, starts the stack,
and prints the URLs.

## After it starts
- Console (curation team):  http://<this-host>:8088
- Nexus repo (developers):  http://<this-host>:8081

Open the console, click **Continue**, then follow the in-app **? Guide** (top bar) or
deploy/docs/TUTORIAL-gate-your-first-package.md.

## Commands (in advisory-rollout/deploy)
\`\`\`
podman compose ps          # status
podman compose logs -f     # logs
podman compose down        # stop
\`\`\`
Full ops/SSO/troubleshooting: deploy/README.md.
TXT

echo "✓ Done: $HANDOFF/$OUT  (+ INSTALL.md)"
echo "  Hand the whole handoff/ folder to IT. They run:  tar -xzf $OUT && cd advisory-rollout/deploy && ./install.sh"
