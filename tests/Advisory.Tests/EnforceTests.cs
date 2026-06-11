using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the Nexus enforcement contract: POST /api/enforce returns 200 (serve) for a clean
/// artifact and 403 (block) for one that violates policy. This is the exact response Nexus Pro's
/// pre-download webhook / a fronting proxy keys off to allow or refuse a package.
/// </summary>
public class EnforceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public EnforceTests(WebApplicationFactory<Program> f) => _client = f.CreateClient();

    [Fact]
    public async Task Enforce_blocks_pickle_weight_with_403()
    {
        var payload = new { format = "huggingface", name = "vendor/model", version = "main",
            fileName = "pytorch_model.bin", sha256 = (string?)null };
        var resp = await _client.PostAsJsonAsync("/api/enforce", payload);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Enforce_allows_safetensors_weight_with_200()
    {
        var payload = new { format = "huggingface", name = "vendor/model", version = "main",
            fileName = "model.safetensors", sha256 = "deadbeef" };
        var resp = await _client.PostAsJsonAsync("/api/enforce", payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
