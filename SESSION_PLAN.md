# Session Plan — Day 11

## Ticket #73 — Add GET /api/env (environment name, anonymous)

**Control:** NIST SSDF RV.1 (operational diagnostics — operators need to confirm which environment a running instance belongs to)
**Impact:** Medium — enables environment identification for diagnostics and deployment verification.
**Urgency:** Low — no security regression, but operationally useful for multi-environment deployments.

### Task

Add `GET /api/env` returning `HTTP 200 { "environment": "<non-empty string>" }` (e.g. "Production"/"Development"), anonymous.

**Minimum change:**
1. One additional `MapGet("/api/env", ...)` in `Program.cs` beside the existing health/version/uptime routes.
2. Read the environment name from `IWebHostEnvironment.EnvironmentName` (already available on `app` as `app.Environment.EnvironmentName`).
3. Tests in `HealthTests.cs`:
   - `Env_returns_200`
   - `Env_returns_nonempty_environment`

No controller, no service. A single-line endpoint accessing the built-in environment name, test-first.

### Routing
- All phases: Cursor CLI (inline) — all edits and documentation by this agent.
