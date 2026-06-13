# Session Plan — Day 10

## Ticket #69 — Add GET /api/uptime (uptimeSeconds, anonymous)

**Control:** NIST SSDF RV.1 (operational availability — monitors need to know how long the API has been running)
**Impact:** Medium — enables uptime monitoring and restart detection.
**Urgency:** Low — no security regression, but operationally useful.

### Task

Add `GET /api/uptime` returning `HTTP 200 { "uptimeSeconds": <double >= 0> }`, anonymous.

**Minimum change:**
1. Add a `System.Diagnostics.Stopwatch` started at app boot in `Program.cs`.
2. One additional `MapGet("/api/uptime", ...)` in `Program.cs` beside the existing health/version routes.
3. Tests in `HealthTests.cs`:
   - `Uptime_returns_200`
   - `Uptime_returns_nonnegative_uptimeSeconds`

No controller, no service. A static Stopwatch + a single-line endpoint, test-first.

### Routing
- Research phase: Groq (openai/gpt-oss-120b) — 635 tokens
- Planning phase: Groq (openai/gpt-oss-120b) — 1580 tokens
- Execution phase: Claude (inline) — applies the edits
- Documentation phase: Claude (inline)
