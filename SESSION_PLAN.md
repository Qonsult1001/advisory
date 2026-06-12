# Session Plan — Day 9

## Ticket #66 — Add GET /api/version (anonymous, service + version)

**Control:** NIST SSDF RV.1 (operational availability / deployment verification)
**Impact:** Medium — operators need to confirm which build is deployed.
**Urgency:** Low — no security regression, but useful for operational visibility.

### Task

Add `GET /api/version` returning `HTTP 200 { "service": "advisory", "version": "<assembly version>" }`, anonymous.

**Minimum change:**
1. One additional `MapGet` in `Program.cs` beside the existing health routes.
2. Tests in `HealthTests.cs` (or a new `VersionTests` section):
   - `Version_returns_200`
   - `Version_returns_service_advisory`
   - `Version_returns_nonempty_version`

No controller, no service. A single-line endpoint, test-first.
