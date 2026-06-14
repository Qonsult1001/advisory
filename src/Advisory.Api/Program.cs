// src/Advisory.Api/Program.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Health endpoints
app.MapGet("/api/health", () => Results.Json(new { status = "ok" }));
app.MapGet("/api/health/live", () => Results.Json(new { status = "ok" }));

// Version endpoint
app.MapGet("/api/version", () => Results.Json(new { service = "advisory", version = "1.0.0" }));

// Uptime endpoint (seconds since process start)
app.MapGet("/api/uptime", () =>
{
    var uptimeSec = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
    return Results.Json(new { uptime = uptimeSec });
});

// New host endpoint (machine name)
app.MapGet("/api/host", () => Results.Json(new { host = Environment.MachineName }));

app.Run();