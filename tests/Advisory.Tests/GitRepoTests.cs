using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the fix for issue #30: GET /api/scans/git-repositories must return the manually-linked
/// list (always configured:true — no GITHUB_OWNER dependency). POST and DELETE let admins
/// add/remove repos without exposing the operator's full private repo inventory.
///
/// Read-only tests share a factory. Write tests get their own isolated factory instances to
/// avoid policy state leaking between tests.
/// </summary>
public class GitRepoReadTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public GitRepoReadTests(WebApplicationFactory<Program> f) => _client = f.CreateClient();

    [Fact]
    public async Task GitRepositories_returns_200()
    {
        var resp = await _client.GetAsync("/api/scans/git-repositories");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GitRepositories_always_configured_and_empty_by_default()
    {
        // No GITHUB_OWNER needed — the endpoint is always available; list is empty until an admin links repos.
        var resp = await _client.GetAsync("/api/scans/git-repositories");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("configured", out var cfg));
        Assert.Equal(JsonValueKind.True, cfg.ValueKind);
        Assert.True(root.TryGetProperty("repositories", out var repos));
        Assert.Equal(JsonValueKind.Array, repos.ValueKind);
        Assert.Equal(0, repos.GetArrayLength());
    }
}

public class GitRepoLinkTests
{
    // Each test gets a fresh factory so in-memory policy state does not leak.
    private static HttpClient NewClient() => new WebApplicationFactory<Program>().CreateClient();

    [Fact]
    public async Task GitRepositories_link_then_list()
    {
        var client = NewClient();
        var body = new { fullName = "testorg/my-service", url = "https://github.com/testorg/my-service", defaultBranch = "main", visibility = "private" };
        var post = await client.PostAsJsonAsync("/api/scans/git-repositories", body);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var resp = await client.GetAsync("/api/scans/git-repositories");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var repos = doc.RootElement.GetProperty("repositories");
        Assert.True(repos.GetArrayLength() > 0);
        var names = Enumerable.Range(0, repos.GetArrayLength())
            .Select(i => repos[i].GetProperty("fullName").GetString())
            .ToList();
        Assert.Contains("testorg/my-service", names);
    }

    [Fact]
    public async Task GitRepositories_unlink_removes_repo()
    {
        var client = NewClient();
        var body = new { fullName = "testorg/to-remove", url = "https://github.com/testorg/to-remove" };
        await client.PostAsJsonAsync("/api/scans/git-repositories", body);

        var del = await client.DeleteAsync("/api/scans/git-repositories/testorg/to-remove");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);

        var resp = await client.GetAsync("/api/scans/git-repositories");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var repos = doc.RootElement.GetProperty("repositories");
        var names = Enumerable.Range(0, repos.GetArrayLength())
            .Select(i => repos[i].GetProperty("fullName").GetString())
            .ToList();
        Assert.DoesNotContain("testorg/to-remove", names);
    }
}
