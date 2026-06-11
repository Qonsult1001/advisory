# Session Plan — Day 5 (2026-06-11)

## Ticket #27 — Add GET /api/health liveness endpoint

**Control mapping:** Operational availability / production-readiness (NIST SSDF RV.1). A liveness
endpoint lets orchestrators and uptime monitors detect a dead process without hitting a
business endpoint. No security control is touched.

**Impact:** Medium — blocked production deployment to any container orchestrator.
**Urgency:** Medium — small, well-scoped gap.
**Score:** Medium × Medium → address this session.

### Task 1: Write test first

Add `tests/Advisory.Tests/HealthTests.cs`:
- `GET /api/health` returns `200`
- Response JSON contains `status: "ok"`

### Task 2: Add minimal-API health route in Program.cs

After `app.MapControllers()`, add:
```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "Advisory.Api" }))
   .AllowAnonymous();
```

No security control touched. No existing endpoints changed.

### Build verification
```
dotnet build src/Advisory.Api/Advisory.Api.csproj -c Release --nologo
dotnet test tests/Advisory.Tests/Advisory.Tests.csproj --nologo
```
