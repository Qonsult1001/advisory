using System.Collections.Concurrent;
using Advisory.Api.Models;
using Advisory.Api.Nexus;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins dynamic discovery (#152, ADR 0001): the quarantine sweep is driven by Nexus's live repo
/// list — every "*-quarantine" repo is polled, mapped to its ecosystem by prefix; non-quarantine
/// repos and unknown-prefix quarantine repos are ignored. No hardcoded ecosystem list.
/// </summary>
public class NexusDiscoveryTests
{
    /// <summary>A fake that records which repos got listed, and returns one component per repo so the
    /// caller can observe the discovered ecosystem mapping.</summary>
    private sealed class DiscoveryNexus : INexusClient
    {
        private readonly List<NexusRepo> _repos;
        public DiscoveryNexus(IEnumerable<string> repoNames)
            => _repos = repoNames.Select(n => new NexusRepo(n, "", "proxy", "", 0, null, null)).ToList();

        public ConcurrentBag<string> Listed = new();
        public bool IsConfigured => true;

        public Task<IReadOnlyList<NexusRepo>> ListRepositoriesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NexusRepo>>(_repos);

        public Task<IReadOnlyList<NexusComponent>> ListComponentsAsync(string repo, CancellationToken ct)
        {
            Listed.Add(repo);
            // emit a component only if the repo maps to an ecosystem, mirroring real client behaviour
            var comps = new List<NexusComponent>();
            if (NexusEcosystems.TryFromRepoName(repo, out var eco))
                comps.Add(new NexusComponent($"id-{repo}", eco, repo, "1.0", null, null, ""));
            return Task.FromResult<IReadOnlyList<NexusComponent>>(comps);
        }

        // ListQuarantineAsync delegates to the real discovery default (extension method under test).
        public Task<IReadOnlyList<NexusComponent>> ListQuarantineAsync(CancellationToken ct)
            => NexusDiscovery.DiscoverQuarantineAsync(this, ct);

        public Task<byte[]> DownloadAsync(string url, CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
        public Task PromoteAsync(NexusComponent c, byte[] b, CancellationToken ct) => Task.CompletedTask;
        public Task HoldAsync(NexusComponent c, string reason, CancellationToken ct) => Task.CompletedTask;
        public Task<ProvisionResult> ProvisionAsync(Ecosystem eco, CancellationToken ct) => Task.FromResult(new ProvisionResult(true, false, null));
        public Task<int> DeprovisionAsync(Ecosystem eco, CancellationToken ct) => Task.FromResult(0);
        public Task<IReadOnlySet<string>> ExistingRepoNamesAsync(CancellationToken ct) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<bool> IsReachableAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RevokeApprovedAsync(Ecosystem eco, string name, string version, CancellationToken ct) => Task.FromResult(true);
        public Task<int> EmptyFirewallReposAsync(CancellationToken ct) => Task.FromResult(0);
    }

    [Fact]
    public async Task Discovers_and_polls_every_quarantine_repo_by_prefix()
    {
        var nexus = new DiscoveryNexus(new[]
        {
            "maven-quarantine", "cran-quarantine", "rubygems-quarantine",
            "maven-approved",            // approved repos are NOT polled for quarantine
            "maven-central",             // a Nexus default — ignored
            "totallyunknown-quarantine", // unknown prefix — discovered but maps to nothing
        });

        var comps = await nexus.ListQuarantineAsync(CancellationToken.None);
        var ecos = comps.Select(c => c.Ecosystem).ToHashSet();

        // Every known quarantine repo was polled...
        Assert.Contains("maven-quarantine", nexus.Listed);
        Assert.Contains("cran-quarantine", nexus.Listed);
        Assert.Contains("rubygems-quarantine", nexus.Listed);
        // ...and produced correctly-mapped components.
        Assert.Contains(Ecosystem.Maven, ecos);
        Assert.Contains(Ecosystem.CRAN, ecos);
        Assert.Contains(Ecosystem.RubyGems, ecos);
        // approved + default + unknown repos did not contribute components.
        Assert.DoesNotContain("maven-approved", nexus.Listed);
        Assert.DoesNotContain("maven-central", nexus.Listed);
    }

    [Fact]
    public async Task Debian_and_Ubuntu_quarantine_map_to_distinct_ecosystems()
    {
        var nexus = new DiscoveryNexus(new[] { "debian-quarantine", "ubuntu-quarantine" });
        var comps = await nexus.ListQuarantineAsync(CancellationToken.None);
        var ecos = comps.Select(c => c.Ecosystem).ToHashSet();
        Assert.Contains(Ecosystem.Debian, ecos);
        Assert.Contains(Ecosystem.Ubuntu, ecos);
    }
}
