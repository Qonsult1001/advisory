#!/usr/bin/env bash
# Advisory rollout installer (Linux / macOS). One command to build + start the whole stack.
set -euo pipefail
cd "$(dirname "$0")"

echo "── Advisory rollout ──────────────────────────────────────────────"

# 1. Container engine present? Prefer Docker, fall back to Podman.
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  DC="docker compose"
elif command -v podman >/dev/null 2>&1 && podman compose version >/dev/null 2>&1; then
  DC="podman compose"
elif command -v podman-compose >/dev/null 2>&1; then
  DC="podman-compose"
else
  echo "✗ No container engine found. Install Docker (https://docs.docker.com/get-docker/) or Podman"
  echo "  (https://podman.io/) with the compose plugin, then re-run."
  exit 1
fi
echo "→ Using: $DC"

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
if [ "${1:-}" = "--scanners" ]; then
  echo "→ Starting the full stack incl. the optional scanners…"
  $DC $SCAN_FILES up -d
else
  echo "→ Starting the core stack (scanner images built but left stopped — start later with --scanners)…"
  $DC up -d
fi

# 4. Wait for the API to answer.
echo "→ Waiting for the API to come up…"
API_PORT="$(grep -E '^API_PORT=' .env | cut -d= -f2 | tr -d '\r' || true)"; API_PORT="${API_PORT:-5000}"
for i in $(seq 1 30); do
  if curl -sf "http://localhost:${API_PORT}/api/health" >/dev/null 2>&1; then ok=1; break; fi
  sleep 4
done

CONSOLE_PORT="$(grep -E '^CONSOLE_PORT=' .env | cut -d= -f2 | tr -d '\r' || true)"; CONSOLE_PORT="${CONSOLE_PORT:-8088}"
NEXUS_PORT="$(grep -E '^NEXUS_PORT=' .env | cut -d= -f2 | tr -d '\r' || true)"; NEXUS_PORT="${NEXUS_PORT:-8081}"

echo
echo "── Done ──────────────────────────────────────────────────────────"
if [ "${ok:-}" = 1 ]; then echo "✓ Stack is up."; else echo "⚠ API didn't answer yet — Nexus can take 1–2 min on first boot. Check: $DC logs -f api"; fi
echo
echo "  Console (curation team):  http://localhost:${CONSOLE_PORT}"
echo "  Nexus repo (developers):  http://localhost:${NEXUS_PORT}"
echo "  API health:               http://localhost:${API_PORT}/api/health"
echo
echo "  Next: open the console, then follow docs/manual/TUTORIAL-gate-your-first-package.md"
echo "  Stop:  $DC down      Logs: $DC logs -f"
