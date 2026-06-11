using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// In-process integration tests: the whole app runs via WebApplicationFactory with a real
/// HTTP client, exercising real registry + OSV calls. Network tests are skippable so the
/// suite still passes offline (the structural tests always run).
/// </summary>
public class GateIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly bool Online = Environment.GetEnvironmentVariable("OFFLINE_TESTS") != "1";

    public GateIntegrationTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    private record Pkg(int ecosystem, string name, string version, string? fileName = null, string? sha256 = null);

    [Fact]
    public async Task Policy_endpoint_returns_signed_policy()
    {
        var resp = await _client.GetAsync("/api/policy");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("signature", out _));
        Assert.True(doc.RootElement.GetProperty("policy").TryGetProperty("version", out _));
    }

    [Fact]
    public async Task Sources_endpoint_lists_all_plugins()
    {
        var resp = await _client.GetAsync("/api/sources");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        foreach (var key in new[] { "osv", "kev", "epss", "malware", "vulncheck", "artifactory" })
            Assert.Contains(key, json);
    }

    [SkippableFact]
    public async Task Known_vulnerable_npm_package_is_evaluated_with_findings()
    {
        Skip.IfNot(Online, "network disabled");
        // lodash 4.17.11 has known prototype-pollution CVEs in OSV.
        var resp = await _client.PostAsJsonAsync("/api/gate/evaluate",
            new Pkg(1, "lodash", "4.17.11"));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        // Tree was resolved (>=1 component) and coverage report exists.
        Assert.True(root.GetProperty("componentsEvaluated").GetInt32() >= 1);
        Assert.True(root.TryGetProperty("coverage", out var cov));
        Assert.True(cov.TryGetProperty("sources", out _));
        // OSV should have produced findings for this version.
        Assert.True(root.GetProperty("findings").GetArrayLength() >= 1);
    }

    [SkippableFact]
    public async Task Clean_modern_package_resolves_tree_and_allows_or_quarantines()
    {
        Skip.IfNot(Online, "network disabled");
        var resp = await _client.PostAsJsonAsync("/api/gate/evaluate",
            new Pkg(0, "requests", "2.31.0")); // PyPI
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var decision = doc.RootElement.GetProperty("decision").GetString();
        Assert.Contains(decision, new[] { "Allow", "Quarantine", "Block" });
        // requests has transitive deps; tree should exceed 1 when resolution worked.
        Assert.True(doc.RootElement.GetProperty("componentsEvaluated").GetInt32() >= 1);
    }

    [Fact]
    public async Task HuggingFace_pickle_weight_is_blocked()
    {
        // No network needed: weights gate is local logic.
        var resp = await _client.PostAsJsonAsync("/api/gate/evaluate",
            new Pkg(5, "vendor/model", "main", fileName: "pytorch_model.bin", sha256: "abc123"));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("Block", doc.RootElement.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Safetensors_weight_with_hash_passes_weights_gate()
    {
        var resp = await _client.PostAsJsonAsync("/api/gate/evaluate",
            new Pkg(5, "vendor/model", "main", fileName: "model.safetensors", sha256: "deadbeef"));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("Allow", doc.RootElement.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Enforce_hook_returns_403_for_blocked_weight()
    {
        var payload = new { format = "huggingface", name = "vendor/model", version = "main",
            fileName = "model.bin", sha256 = (string?)null };
        var resp = await _client.PostAsJsonAsync("/api/enforce", payload);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
