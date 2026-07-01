#!/usr/bin/env bash
# Advisory rollout installer (Linux / macOS). One command to build + start the whole stack.
set -euo pipefail
cd "$(dirname "$0")"

echo "── Advisory rollout ──────────────────────────────────────────────"

# 1. Pick the container engine. You can FORCE one with --podman or --docker (as the first argument);
#    otherwise it auto-detects, preferring Podman (this is a Podman-targeted rollout). Note: if Docker
#    Desktop's WSL integration is active, a plain 'docker' may exist even on a "Podman" host — the
#    --podman flag guarantees Podman is used regardless.
FORCE=""
for a in "$@"; do case "$a" in --podman) FORCE=podman;; --docker) FORCE=docker;; esac; done

pick_podman() {
  if command -v podman >/dev/null 2>&1 && podman compose version >/dev/null 2>&1; then DC="podman compose"; return 0
  elif command -v podman-compose >/dev/null 2>&1; then DC="podman-compose"; return 0
  else return 1; fi
}
pick_docker() { if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then DC="docker compose"; return 0; else return 1; fi; }

if [ "$FORCE" = podman ]; then
  pick_podman || { echo "✗ --podman requested but Podman + a compose tool (podman compose / podman-compose) not found."; exit 1; }
elif [ "$FORCE" = docker ]; then
  pick_docker || { echo "✗ --docker requested but 'docker compose' not found."; exit 1; }
else
  # Auto: prefer Podman (Podman-targeted rollout), fall back to Docker.
  pick_podman || pick_docker || {
    echo "✗ No container engine found. Install Podman (https://podman.io/) or Docker"
    echo "  (https://docs.docker.com/get-docker/) with a compose tool, then re-run."; exit 1; }
fi
echo "→ Using: $DC   (force with --podman or --docker if this isn't what you want)"

# 2. .env present?
if [ ! -f .env ]; then
  echo "→ No .env found — creating one from .env.example."
  cp .env.example .env
  echo "✗ Edit .env now (set SQL_SA_PASSWORD at minimum), then re-run ./install.sh"
  exit 1
fi

# 3. Build + start. The optional scanners are a SEPARATE overlay file (works on any compose version,
#    unlike profiles). With --scanners we include it (build + start the scanners too); without it, the
#    scanner images are still built so they exist on the host for later, but left stopped.
SCAN_FILES="-f docker-compose.yml -f docker-compose.scanners.yml"
echo "→ Building all images from source, incl. scanners (first run takes a few minutes)…"
$DC $SCAN_FILES build || { echo "  (scanner build skipped — building core only)"; $DC build; }
WANT_SCANNERS=""
for a in "$@"; do [ "$a" = "--scanners" ] && WANT_SCANNERS=1; done
if [ "$WANT_SCANNERS" = 1 ]; then
  echo "→ Starting the full stack incl. the optional scanners…"
  $DC $SCAN_FILES up -d
else
  echo "→ Starting the core stack (scanner images built but left stopped — start later with --scanners)…"
  $DC up -d
fi

# Read a port from .env, stripping any inline "# comment" and whitespace.
envport() { grep -E "^$1=" .env | head -1 | cut -d= -f2 | sed 's/#.*//' | tr -d ' \r\t'; }
API_PORT="$(envport API_PORT)"; API_PORT="${API_PORT:-5000}"
CONSOLE_PORT="$(envport CONSOLE_PORT)"; CONSOLE_PORT="${CONSOLE_PORT:-8088}"
NEXUS_PORT="$(envport NEXUS_PORT)"; NEXUS_PORT="${NEXUS_PORT:-8081}"

# 4. Wait for the API to answer. Nexus + SQL cold-boot can take a few minutes on the first run, so
#    wait up to ~5 minutes before warning.
echo "→ Waiting for the API to come up (first boot can take a few minutes)…"
for i in $(seq 1 75); do
  if curl -sf "http://localhost:${API_PORT}/api/health" >/dev/null 2>&1; then ok=1; break; fi
  sleep 4
done

echo
echo "── Done ──────────────────────────────────────────────────────────"
if [ "${ok:-}" = 1 ]; then
  echo "✓ Stack is up."
else
  echo "⚠ API hasn't answered yet — Nexus + SQL can take a few minutes on first boot. It may still be"
  echo "  coming up. Check: $DC logs -f   (wait, then open the console below)."
fi
echo
echo "  Console (curation team):  http://localhost:${CONSOLE_PORT}"
echo "  Nexus repo (developers):  http://localhost:${NEXUS_PORT}"
echo "  API health:               http://localhost:${API_PORT}/api/health"
echo
echo "  Next: open the console, then follow docs/TUTORIAL-gate-your-first-package.md"
echo "  Stop:  $DC down      Logs: $DC logs -f"
