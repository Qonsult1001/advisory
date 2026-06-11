using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Advisory.Api.Audit;
using Advisory.Api.Gate;
using Advisory.Api.Models;
using Advisory.Api.Nexus;
using Advisory.Api.Policy;
using Advisory.Api.Research;
using Advisory.Api.Resolve;
using Advisory.Api.Scan;
using Advisory.Api.VulnSources;
using Advisory.Api.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Verifies the interception logic offline with a fake Nexus: a clean package is promoted,
/// a malicious pickle weight is held. No network, no real Nexus.
/// </summary>
public class PromotionBridgeTests
{
    private sealed class FakeNexus : INexusClient
    {
        public bool IsConfigured => true;
        public List<NexusComponent> Queue = new();
        public ConcurrentBag<string> Promoted = new();
        public ConcurrentBag<string> Held = new();
        public Task<IReadOnlyList<NexusComponent>> ListQuarantineAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NexusComponent>>(Queue);
        public Task<IReadOnlyList<NexusRepo>> ListRepositoriesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NexusRepo>>(new List<NexusRepo>());
        public Task<IReadOnlyList<NexusComponent>> ListComponentsAsync(string repo, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NexusComponent>>(Queue);
        public Task<byte[]> DownloadAsync(string url, CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
        public Task PromoteAsync(NexusComponent c, byte[] b, CancellationToken ct) { Promoted.Add(c.Name); return Task.CompletedTask; }
        public Task HoldAsync(NexusComponent c, string reason, CancellationToken ct) { Held.Add(c.Name); return Task.CompletedTask; }
    }

    public static ServiceProvider BuildGate()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["PolicyPath"] = Path.GetTempFileName(),
            ["AuditPath"] = Path.GetTempFileName(),
            ["WormPath"] = Path.GetTempFileName(),
        }).Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddHttpClient();
        sc.AddLogging();
        sc.AddSingleton<IPolicyStore, PolicyStore>();
        sc.AddSingleton<IWormSink, FileWormSink>();
        sc.AddSingleton<IAuditLog, AuditLog>();
        sc.AddSingleton<KevSource>(); sc.AddSingleton<EpssSource>();
        sc.AddSingleton<PickleScanner>();
        sc.AddSingleton<SecretScanner>();
        sc.AddSingleton<IacScanner>();
        sc.AddSingleton<ReachabilityAnalyzer>();
        sc.AddSingleton<Advisory.Api.Catalog.OpRiskService>();
        sc.AddSingleton<IGroqClient, GroqClient>();
        sc.AddSingleton<IResearchAgent, ClaudeResearchAgent>();
        sc.AddSingleton<Advisory.Api.Integrations.IItsmNotifier, Advisory.Api.Integrations.ItsmWebhook>();
        // register enrichment sources as IVulnSource (HuggingFace path doesn't call them);
        // DI composes IEnumerable<IVulnSource> itself. No resolvers => HF path needs none.
        sc.AddSingleton<IVulnSource>(sp => sp.GetRequiredService<KevSource>());
        sc.AddSingleton<IVulnSource>(sp => sp.GetRequiredService<EpssSource>());
        sc.AddSingleton<ICurrentUser, TestSystemUser>();
        sc.AddScoped<IGateEngine, GateEngine>();
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task Malicious_pickle_weight_is_held_not_promoted()
    {
        var provider = BuildGate();
        var nexus = new FakeNexus
        {
            Queue = { new NexusComponent("id1", Ecosystem.HuggingFace, "vendor/model", "main",
                "pytorch_model.bin", null, "") }   // pickle + no hash => block
        };
        var bridge = new PromotionBridge(nexus, provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["NEXUS_POLL_SECONDS"]="1"}).Build(),
            NullLogger<PromotionBridge>.Instance);

        using var cts = new CancellationTokenSource();
        var run = bridge.StartAsync(cts.Token);
        await Task.Delay(1500);
        cts.Cancel();

        Assert.Contains("vendor/model", nexus.Held);
        Assert.DoesNotContain("vendor/model", nexus.Promoted);
    }

    [Fact]
    public async Task Clean_safetensors_weight_is_promoted()
    {
        var provider = BuildGate();
        var nexus = new FakeNexus
        {
            Queue = { new NexusComponent("id2", Ecosystem.HuggingFace, "vendor/clean", "main",
                "model.safetensors", "deadbeef", "http://x") }   // safetensors + hash => allow
        };
        var bridge = new PromotionBridge(nexus, provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["NEXUS_POLL_SECONDS"]="1"}).Build(),
            NullLogger<PromotionBridge>.Instance);

        using var cts = new CancellationTokenSource();
        await bridge.StartAsync(cts.Token);
        await Task.Delay(1500);
        cts.Cancel();

        Assert.Contains("vendor/clean", nexus.Promoted);
        Assert.DoesNotContain("vendor/clean", nexus.Held);
    }
}


internal sealed class TestSystemUser : Advisory.Api.Auth.ICurrentUser
{
    public string Name => "system";
    public string ObjectId => "system";
    public IReadOnlyList<string> Roles => System.Array.Empty<string>();
}
