using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the fix for issue #27: GET /api/health must be reachable without authentication
/// and return 200 with status "ok".
/// </summary>
public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public HealthTests(WebApplicationFactory<Program> f) => _client = f.CreateClient();

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
}
