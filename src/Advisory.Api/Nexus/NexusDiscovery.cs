namespace Advisory.Api.Nexus;

/// <summary>
/// Dynamic discovery (ADR 0001): the set of ecosystems the firewall gates is whatever exists in
/// Nexus right now — not a hardcoded list. Enumerate the repositories, keep the "*-quarantine" ones
/// that map to a known ecosystem by prefix, and list each. Shared by the real client and tests.
/// </summary>
public static class NexusDiscovery
{
    /// <summary>Repo names that are quarantine repos for a known ecosystem (by prefix, per ADR 0001).</summary>
    public static async Task<IReadOnlyList<string>> QuarantineReposAsync(INexusClient nexus, CancellationToken ct)
    {
        var repos = await nexus.ListRepositoriesAsync(ct);
        var result = new List<string>();
        foreach (var r in repos)
        {
            if (!r.Name.EndsWith("-quarantine", StringComparison.OrdinalIgnoreCase)) continue;
            if (NexusEcosystems.TryFromRepoName(r.Name, out _)) result.Add(r.Name);
        }
        return result;
    }

    /// <summary>List the components across every discovered quarantine repo.</summary>
    public static async Task<IReadOnlyList<NexusComponent>> DiscoverQuarantineAsync(INexusClient nexus, CancellationToken ct)
    {
        var items = new List<NexusComponent>();
        foreach (var repo in await QuarantineReposAsync(nexus, ct))
            items.AddRange(await nexus.ListComponentsAsync(repo, ct));
        return items;
    }
}
