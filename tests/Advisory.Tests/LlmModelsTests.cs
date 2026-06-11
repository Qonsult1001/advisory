using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the fix for issue #2: GET /v1/models must reflect the gateway's enabled state. When the
/// LLM gateway is disabled by policy, the model list must be empty (consistent with the chat
/// endpoint returning 403) — a client must not discover usable models from a disabled gateway.
/// </summary>
public class LlmModelsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public LlmModelsTests(WebApplicationFactory<Program> f) => _client = f.CreateClient();

    private static async Task<int> ModelCount(HttpResponseMessage resp)
    {
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetArrayLength();
    }

    [Fact]
    public async Task Models_lists_models_when_gateway_enabled()
    {
        // Default policy has the gateway enabled → models are advertised.
        var get = await _client.GetAsync("/v1/models");
        get.EnsureSuccessStatusCode();
        Assert.True(await ModelCount(get) > 0);
    }

    [Fact]
    public async Task Models_is_empty_when_gateway_disabled()
    {
        // Disable the gateway via policy, then confirm /v1/models advertises nothing.
        var cur = await (await _client.GetAsync("/api/policy")).Content.ReadFromJsonAsync<JsonElement>();
        var policy = cur.GetProperty("policy");
        // Round-trip the policy with Llm.Enabled = false.
        var node = System.Text.Json.Nodes.JsonNode.Parse(policy.GetRawText())!;
        node["llm"]!["enabled"] = false;
        var put = await _client.PutAsync("/api/policy",
            new StringContent(node.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        put.EnsureSuccessStatusCode();

        var get = await _client.GetAsync("/v1/models");
        get.EnsureSuccessStatusCode();
        Assert.Equal(0, await ModelCount(get));

        // restore enabled so other tests/state are unaffected
        node["llm"]!["enabled"] = true;
        await _client.PutAsync("/api/policy", new StringContent(node.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
    }
}
