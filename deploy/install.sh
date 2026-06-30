#!/usr/bin/env bash
# Advisory rollout installer (Linux / macOS). One command to build + start the whole stack.
set -euo pipefail
cd "$(dirname "$0")"

echo "── Advisory rollout ──────────────────────────────────────────────"

# 1. Docker present?
if ! command -v docker >/dev/null 2>&1; then
  echo "✗ Docker is not installed. Install Docker Desktop / Docker Engine first: https://docs.docker.com/get-docker/"
  exit 1
fi
if ! docker compose version >/dev/null 2>&1; then
  echo "✗ 'docker compose' (v2) not available. Update Docker."
  exit 1
fi

# 2. .env present?
if [ ! -f .env ]; then
  echo "→ No .env found — creating one from .env.example."
  cp .env.example .env
  echo "✗ Edit .env now (set SQL_SA_PASSWORD at minimum), then re-run ./install.sh"
  exit 1
fi

# 3. Build + start.
echo "→ Building images from source (first run takes a few minutes)…"
docker compose up --build -d

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
if [ "${ok:-}" = 1 ]; then echo "✓ Stack is up."; else echo "⚠ API didn't answer yet — Nexus can take 1–2 min on first boot. Check: docker compose logs -f api"; fi
echo
echo "  Console (curation team):  http://localhost:${CONSOLE_PORT}"
echo "  Nexus repo (developers):  http://localhost:${NEXUS_PORT}"
echo "  API health:               http://localhost:${API_PORT}/api/health"
echo
echo "  Next: open the console, then follow docs/manual/TUTORIAL-gate-your-first-package.md"
echo "  Stop:  docker compose down      Logs: docker compose logs -f"
