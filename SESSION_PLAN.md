# Session Plan — Day 12

## Ticket #82 — Add GET /api/pid (process id, anonymous)

**Control:** NIST SSDF RV.1 (operational diagnostics — identify which OS process is serving requests)
**Impact:** Medium — enables process-level diagnostics when multiple instances run behind a load balancer.
**Urgency:** Low — no security regression, but operationally useful for instance identification.

### Task

Add `GET /api/pid` returning `HTTP 200 { "pid": <integer> }`, anonymous.

**Minimum change:**
1. One additional `MapGet("/api/pid", ...)` in `Program.cs` after the existing `/api/time` route (line 156).
2. Use `Environment.ProcessId` — zero-allocation, no extra namespace, available since .NET 5.
3. Tests in `HealthTests.cs`:
   - `Pid_returns_200` — asserts HTTP 200.
   - `Pid_returns_positive_integer` — asserts `pid` property exists and is > 0.

No controller, no service. A single-line endpoint, test-first.

### Routing
- Research phase: Groq (openai/gpt-oss-120b) — 1,099 tokens. Recommended `Environment.ProcessId` over `Process.GetCurrentProcess().Id`.
- Planning phase: Groq (openai/gpt-oss-120b) — 1,695 tokens. Confirmed location and test shape.
- Execution + documentation: Claude (inline).
