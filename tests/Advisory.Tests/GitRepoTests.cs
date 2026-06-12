using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// A WebApplicationFactory that uses a unique temp policy file so that parallel write tests
/// cannot pollute the policy.json that read-only tests depend on being empty.
/// </summary>
public class IsolatedPolicyFactory : WebApplicationFactory<Program>
{
    private readonly string _tempPolicy = Path.GetTempFileName();
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        => builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyPath"] = _tempPolicy }));
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_tempPolicy)) File.Delete(_tempPolicy);
    }
}

/// <summary>
/// Pins the fix for issue #30: GET /api/scans/git-repositories must return the manually-linked
/// list (always configured:true — no GITHUB_OWNER dependency). POST and DELETE let admins
/// add/remove repos without exposing the operator's full private repo inventory.
///
/// Read-only tests share a factory backed by an isolated temp policy file so that write tests
/// running in parallel cannot pollute the state seen here.
/// </summary>
public class GitRepoReadTests : IClassFixture<IsolatedPolicyFactory>
{
    private readonly HttpClient _client;
    public GitRepoReadTests(IsolatedPolicyFactory f) => _client = f.CreateClient();

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
    // Each test gets a fresh factory with an isolated policy file to prevent concurrent write conflicts.
    private static HttpClient NewClient()
    {
        var tempPolicy = Path.GetTempFileName();
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyPath"] = tempPolicy })))
            .CreateClient();
    }

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
    public async Task GitRepoLink_RejectsBareName()
    {
        // A bare name (no '/') must be rejected — the scan route needs owner/repo.
        var client = NewClient();
        var body = new { fullName = "test", url = "https://github.com/test" };
        var resp = await client.PostAsJsonAsync("/api/scans/git-repositories", body);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GitRepoLink_RejectsMultiSegmentName()
    {
        // Three-segment names (org/repo/extra) must also be rejected.
        var client = NewClient();
        var body = new { fullName = "org/repo/extra", url = "https://github.com/org/repo/extra" };
        var resp = await client.PostAsJsonAsync("/api/scans/git-repositories", body);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
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

/// <summary>
/// Pins the fix for issue #33: linked git repositories must be scannable via
/// POST /api/scans/git-repositories/{owner}/{repo}/scan (SEC-SRC-01).
/// Unlinked repos must return 404 — the gate only scans what has been explicitly approved.
///
/// Each test uses an isolated temp policy file so it does not pollute the shared policy.json
/// that the read-only fixture in GitRepoReadTests depends on.
/// </summary>
public class GitRepoScanTests
{
    // Each test gets a fresh factory with a unique policy file to prevent disk-level state bleed.
    private static HttpClient NewClient()
    {
        var tempPolicy = Path.GetTempFileName();
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyPath"] = tempPolicy })))
            .CreateClient();
    }

    [Fact]
    public async Task GitRepoScan_returns_404_when_repo_not_linked()
    {
        var client = NewClient();
        // No POST to link this repo — must not be scannable.
        var resp = await client.PostAsJsonAsync("/api/scans/git-repositories/neverlinked/nosuchrepo/scan", new { });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GitRepoScan_returns_202_for_linked_repo()
    {
        var client = NewClient();
        await client.PostAsJsonAsync("/api/scans/git-repositories",
            new { fullName = "testorg/scan-target", url = "https://github.com/testorg/scan-target" });

        var resp = await client.PostAsJsonAsync("/api/scans/git-repositories/testorg/scan-target/scan", new { });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
    }

    [Fact]
    public async Task GitRepoScanResult_returns_200_with_expected_shape_after_scan_started()
    {
        var client = NewClient();
        await client.PostAsJsonAsync("/api/scans/git-repositories",
            new { fullName = "testorg/shape-check", url = "https://github.com/testorg/shape-check" });
        await client.PostAsJsonAsync("/api/scans/git-repositories/testorg/shape-check/scan", new { });

        var resp = await client.GetAsync("/api/scans/git-repositories/testorg/shape-check/scan");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("fullName", out _), "must have fullName");
        Assert.True(root.TryGetProperty("status", out var status), "must have status");
        Assert.Contains(status.GetString(), new[] { "Scanning", "Done", "Failed" });
        Assert.True(root.TryGetProperty("packagesFound", out _), "must have packagesFound");
    }

    [Fact]
    public async Task GitRepoScanResult_returns_404_when_scan_never_started()
    {
        var client = NewClient();
        await client.PostAsJsonAsync("/api/scans/git-repositories",
            new { fullName = "testorg/unscanned", url = "https://github.com/testorg/unscanned" });

        // Linked but never scanned → no result yet.
        var resp = await client.GetAsync("/api/scans/git-repositories/testorg/unscanned/scan");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
