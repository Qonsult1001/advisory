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

# Also copy the human docs LOOSE into handoff/ so IT can read them WITHOUT extracting the tarball
# (and so these never drift from the source — this mirror is regenerated on every package run).
echo "→ Copying docs loose into handoff/docs/ (readable without extracting)…"
mkdir -p "$HANDOFF/docs"
cp deploy/docs/TUTORIAL-gate-your-first-package.md "$HANDOFF/docs/" 2>/dev/null || true
cp deploy/docs/HOW-TO-GUIDES.md                    "$HANDOFF/docs/" 2>/dev/null || true
cp deploy/docs/CONSOLE-USER-MANUAL.md              "$HANDOFF/docs/" 2>/dev/null || true
cp deploy/README.md                                "$HANDOFF/docs/RUNBOOK.md" 2>/dev/null || true
cp deploy/PODMAN-DEPLOYMENT-NOTES.md               "$HANDOFF/PODMAN-DEPLOYMENT-NOTES.md" 2>/dev/null || true

cat > "$HANDOFF/INSTALL.md" <<TXT
# Advisory - install (Podman)

You have one file: \`$OUT\`. It contains the full Advisory firewall, pre-configured and ready to run.

> New here? Read \`README.md\` in this folder first - it indexes everything. The guides this page points
> to (\`deploy/docs/...\`, \`deploy/README.md\`) live inside the tarball once you extract it, and are
> **also copied loose in this folder** as \`docs/\` and \`docs/RUNBOOK.md\` so you can read them now.

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
./install.sh --podman            # force Podman; add --scanners to also start the scanners
\`\`\`
\`--podman\` guarantees Podman is used (important if Docker Desktop's WSL integration is also present -
without it the installer might pick Docker). It is already configured; the installer builds the images,
starts the stack, and prints the URLs. Watch for \`Using: podman compose\` (or \`podman-compose\`).

## After it starts
- Console (curation team):  http://<this-host>:8088
- Nexus repo (developers):  http://<this-host>:8081

Open the console, click **Continue**, then follow the in-app **? Guide** (top bar) or
deploy/docs/TUTORIAL-gate-your-first-package.md.

To let developers install through the firewall (their normal \`pip\`/\`npm\`, redirected via the proxy) and
to enforce it org-wide, see deploy/docs/HOW-TO-GUIDES.md guide 6 (incl. the *"For IT"* enforcement part).

## Commands (in advisory-rollout/deploy)

Use whichever compose command your host has. If \`podman compose\` says "unrecognized command", your
host uses standalone \`podman-compose\` (pip-installed at ~/.local/bin - ensure it's on PATH with
\`export PATH="\\\$HOME/.local/bin:\\\$PATH"\`).
\`\`\`
podman compose ps          # (plugin)      status
podman compose logs -f     #               logs
podman compose down        #               stop
# or, if you use standalone podman-compose:
podman-compose ps
podman-compose logs -f
podman-compose down
\`\`\`
Full ops/SSO/troubleshooting: deploy/README.md.
TXT

# Top-level index so whoever receives handoff/ knows what to open and in what order.
cat > "$HANDOFF/README.md" <<TXT
# Advisory firewall - IT handoff

This folder is everything you need to install and use the Advisory software-supply-chain firewall.
Read this page first; it tells you what to open and in what order.

## What's in this folder

| File | What it is | When to read it |
|------|-----------|-----------------|
| \`$OUT\` | The whole product, pre-configured (console, gate API, Nexus repo, scanners). | This is what you install. |
| \`INSTALL.md\` | The **quick install** - three commands to get the stack running on Podman. | **Start here** to stand it up. |
| \`PODMAN-DEPLOYMENT-NOTES.md\` | Podman-specific notes: verified requirements, fixes already applied, harmless warnings. | If anything looks off during install. |
| \`docs/TUTORIAL-gate-your-first-package.md\` | **Step-by-step guide** - zero to gating one real package, one safe path. | **After install**, first time in the console. |
| \`docs/HOW-TO-GUIDES.md\` | **How-to guides** - one goal per task (set policy, gate, approve, exceptions, **point developers' pip/npm at the firewall + enforce it org-wide via IT policy**, reports, add ecosystems). | Day-to-day, for one specific thing. |
| \`docs/CONSOLE-USER-MANUAL.md\` | Full reference manual - everything you see and do. | Reference / look-up. |
| \`docs/RUNBOOK.md\` | Operations runbook - network egress allowlist, ports, SSO, scanners, troubleshooting. | Before go-live and for ongoing ops. |

## Do this, in order

1. **Install** - follow \`INSTALL.md\`. When it finishes you'll see \`Stack is up.\` and three URLs
   (console \`:8088\`, Nexus \`:8081\`, API health \`:5000\`).
2. **Learn it by doing** - open the console and follow \`docs/TUTORIAL-gate-your-first-package.md\`.
   (The console also has a built-in **? Guide** button in the top bar.)
3. **Operate it** - use \`docs/HOW-TO-GUIDES.md\` for individual tasks and \`docs/RUNBOOK.md\` for
   networking/ports/SSO. Keep \`docs/CONSOLE-USER-MANUAL.md\` as your reference.
4. **Wire up your developers** - so their normal \`pip install\` / \`npm install\` go through the firewall.
   See \`docs/HOW-TO-GUIDES.md\` guide 6 (developer setup **and** the *"For IT - enforce the redirect for
   everyone"* section for pushing it org-wide via Group Policy / Intune / a network block).

## The two guides, in one line each

- **Step-by-step guide** (\`docs/TUTORIAL-gate-your-first-package.md\`) - "I've never used this; walk me
  from nothing to my first success." One guaranteed path.
- **How-to guides** (\`docs/HOW-TO-GUIDES.md\`) - "I know the basics; I just need to do *this one thing*."
  Each guide is a single goal.

## Security note for whoever hands this over

The tarball is **pre-configured with live API keys** (Groq, VulnCheck, OpenRouter). Treat this folder
as a secret in transit, and **rotate those keys after the handoff**.
TXT

echo "✓ Done: $HANDOFF/$OUT  (+ README.md, INSTALL.md, docs/)"
echo "  Hand the whole handoff/ folder to IT. Tell them to open README.md first."
