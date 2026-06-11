using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the fix for issue #10: GET /api/scans/git-repositories must exist, return 200, and
/// report configured:false when GITHUB_OWNER is not set (the normal test-env state).
/// Mirrors the pattern used by the Nexus /api/scans/repositories endpoint for unconfigured state.
/// </summary>
public class GitRepoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public GitRepoTests(WebApplicationFactory<Program> f) => _client = f.CreateClient();

    [Fact]
    public async Task GitRepositories_returns_200()
    {
        var resp = await _client.GetAsync("/api/scans/git-repositories");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GitRepositories_unconfigured_returns_empty_list()
    {
        // No GITHUB_OWNER / EVOLUTION_REPO in test env → configured: false, repositories: []
        var resp = await _client.GetAsync("/api/scans/git-repositories");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("configured", out var cfg));
        Assert.Equal(JsonValueKind.False, cfg.ValueKind);
        Assert.True(root.TryGetProperty("repositories", out var repos));
        Assert.Equal(JsonValueKind.Array, repos.ValueKind);
        Assert.Equal(0, repos.GetArrayLength());
    }
}
