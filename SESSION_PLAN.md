# Session Plan — Day 8

## Ticket #53 — Add GET /api/health/live (anonymous liveness probe)

**Control:** NIST SSDF RV.1 (operational availability)
**Impact:** Medium — without a dedicated liveness path, orchestrators that probe `/live` mark the
pod unhealthy even when the API is serving traffic.
**Urgency:** Low-Medium — operational gap, not a gate security regression.

### Task

Add `GET /api/health/live` returning `HTTP 200 { "status": "ok" }`, anonymous.

**Minimum change:**
1. One additional `MapGet` in `Program.cs` beside the existing `/api/health` route.
2. Two tests in `HealthTests.cs`:
   - `HealthLive_returns_200`
   - `HealthLive_returns_status_ok`

No controller, no service, no new files. A single-line endpoint, test-first.
