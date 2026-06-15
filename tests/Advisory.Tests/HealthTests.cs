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
    public async Task Os_returns_nonempty_os()
    {
        var resp = await _client.GetAsync("/api/os");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("os", out var os));
        Assert.False(string.IsNullOrWhiteSpace(os.GetString()));
    }

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
                                    [Fact]
                                    public async Task Is64Bit_Returns_200()
                                    {
                                        var resp = await _client.GetAsync("/api/is64bit");
                                        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                                        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                                        Assert.True(doc.RootElement.TryGetProperty("is64bit", out var prop));
                                        Assert.True(prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False);
                                    }
                                [Fact]
                                    public async Task Logical_returns_200_and_positive()
                                    {
                                        var resp = await _client.GetAsync("/api/logical");
                                        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                                        var json = await resp.Content.ReadAsStringAsync();
                                        using var doc = JsonDocument.Parse(json);
                                        var logical = doc.RootElement.GetProperty("logical").GetInt32();
                                        Assert.True(logical > 0);
                                    }
                            [Fact]
                            public async Task Alloc2Endpoint_ReturnsNonNegativeAlloc()
                            {
                                var response = await _client.GetAsync("/api/alloc2");
                                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                                using var stream = await response.Content.ReadAsStreamAsync();
                                using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
                                Assert.True(doc.RootElement.TryGetProperty("alloc", out var allocProp));
                                Assert.True(allocProp.GetInt64() >= 0);
                            }
                        [Fact]
                        public async Task GetNumCpu_ReturnsPositiveValue()
                        {
                            var response = await _client.GetAsync("/api/numcpu");
                            response.EnsureSuccessStatusCode();
                            var json = await response.Content.ReadAsStringAsync();
                            var doc = JsonDocument.Parse(json);
                            var numcpu = doc.RootElement.GetProperty("numcpu").GetInt32();
                            Assert.True(numcpu > 0, $"Expected numcpu > 0 but was {numcpu}");
                        }
                    [Fact]
                    public async Task GetCpu_Returns200AndPositiveCount()
                    {
                        using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
                        var client = factory.CreateClient();

                        var response = await client.GetAsync("/api/cpu");
                        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var cpu = doc.RootElement.GetProperty("cpu").GetInt32();

                        Assert.True(cpu > 0, $"CPU count should be >0 but was {cpu}");
                    }
                [Fact]
                public async Task Get_Ticks_ReturnsPositiveTicks()
                {
                    var response = await _client.GetAsync("/api/ticks");
                    response.EnsureSuccessStatusCode();

                    var body = await response.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    var ticks = doc.RootElement.GetProperty("ticks").GetInt64();
                    Assert.True(ticks > 0);
                }
            [Fact]
            public async Task GetMemory_Returns200AndPositiveBytes()
            {
                var response = await _client.GetAsync("/api/memory");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                var payload = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<MemoryResponse>(response.Content);
                Assert.NotNull(payload);
                Assert.True(payload!.bytes > 0);
            }

            private class MemoryResponse
            {
                public long bytes { get; set; }
            }
        [Fact]
        public async Task GetThreads_Returns200AndPositiveCount()
        {
            var response = await _client.GetAsync("/api/threads");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            var payload = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            Assert.NotNull(payload);
            Assert.True(payload.ContainsKey("threads"));
            Assert.True(payload["threads"] > 0);
        }
    [Fact]
    public async Task GetCores_ReturnsPositiveCount()
    {
        var response = await _client.GetAsync("/api/cores");
        response.EnsureSuccessStatusCode();
        var jsonString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonString);
        var cores = doc.RootElement.GetProperty("cores").GetInt32();
        Assert.True(cores > 0, "Cores count should be greater than 0");
    }
}
