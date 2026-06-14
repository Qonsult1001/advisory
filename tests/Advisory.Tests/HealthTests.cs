// tests/Advisory.Tests/HealthTests.cs
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public HealthTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_returns_200()
    {
        var resp = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Health_returns_status_ok()
    {
        var resp = await _client.GetAsync("/api/health");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("status", out var status));
        Assert.Equal("ok", status.GetString());
    }

    [Fact]
    public async Task HealthLive_returns_200()
    {
        var resp = await _client.GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task HealthLive_returns_status_ok()
    {
        var resp = await _client.GetAsync("/api/health/live");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("status", out var status));
        Assert.Equal("ok", status.GetString());
    }

    // --- Issue #66: GET /api/version ---

    [Fact]
    public async Task Version_returns_200()
    {
        var resp = await _client.GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Version_returns_service_advisory()
    {
        var resp = await _client.GetAsync("/api/version");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("service", out var svc));
        Assert.Equal("advisory", svc.GetString());
    }

    [Fact]
    public async Task Version_returns_nonempty_version()
    {
        var resp = await _client.GetAsync("/api/version");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("version", out var ver));
        Assert.False(string.IsNullOrEmpty(ver.GetString()), "version must be non-empty");
    }

    // --- Issue #69: GET /api/uptime ---

    [Fact]
    public async Task Uptime_returns_200()
    {
        var resp = await _client.GetAsync("/api/uptime");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Uptime_returns_nonnegative_seconds()
    {
        var resp = await _client.GetAsync("/api/uptime");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("uptime", out var up));
        Assert.True(up.GetDouble() >= 0, "uptime must be non‑negative");
    }

    // --- New Issue: GET /api/host ---

    [Fact]
    public async Task Host_returns_200()
    {
        var resp = await _client.GetAsync("/api/host");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Host_returns_nonempty_host()
    {
        var resp = await _client.GetAsync("/api/host");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("host", out var host));
        Assert.False(string.IsNullOrWhiteSpace(host.GetString()), "host must be non‑empty");
    }
}
