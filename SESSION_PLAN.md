# Session Plan — Day 11

## Ticket #78 — Add GET /api/time (utc timestamp, anonymous)

**Control:** NIST SSDF RV.1 (operational diagnostics — clients need to detect clock skew between server and client)
**Impact:** Medium — enables clock-skew detection for distributed clients.
**Urgency:** Low — no security regression, but operationally useful for time-sensitive operations.

### Task

Add `GET /api/time` returning `HTTP 200 { "utc": "<ISO-8601 timestamp>" }`, anonymous.

**Minimum change:**
1. One additional `MapGet("/api/time", ...)` in `Program.cs` after the existing `/api/env` route (line 154).
2. Use `DateTimeOffset.UtcNow` — System.Text.Json serializes it as ISO-8601 with `Z` suffix automatically.
3. Tests in `HealthTests.cs`:
   - `Time_returns_200` — asserts HTTP 200.
   - `Time_returns_valid_utc_timestamp` — asserts `utc` property exists and parses as a valid `DateTimeOffset`.

No controller, no service. A single-line endpoint, test-first.

### Routing
- Research phase: Groq (openai/gpt-oss-120b) — 1,777 tokens. Recommended `DateTimeOffset.UtcNow` over `DateTime.UtcNow`.
- Planning phase: Groq (openai/gpt-oss-120b) — 1,704 tokens. Confirmed location and test shape.
- Execution + documentation: Claude (inline).
