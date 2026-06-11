using System.Collections.Concurrent;

namespace PkgFirewall.Api.Catalog;

/// <summary>A model that has been pulled into a repository for consumption.</summary>
public record ConsumedModel(string Repo, string ModelId, string Version, string File, string Format, DateTimeOffset PulledAt);

/// <summary>
/// Tracks models pulled into repositories through the firewall. "Pull into repository" on an
/// approved model lands it here (status Approved in Detection). A simulated shadow-AI drop lands an
/// UNapproved model here too — so the Shadow AI Detection sweep has something real to show even when
/// the Nexus repos hold no model files yet.
/// </summary>
public class ConsumedModelStore
{
    private readonly ConcurrentDictionary<string, ConsumedModel> _items = new();

    public IReadOnlyList<ConsumedModel> List() => _items.Values.OrderByDescending(x => x.PulledAt).ToList();

    public ConsumedModel Add(string repo, string modelId, string version, string file, string format)
    {
        var m = new ConsumedModel(repo, modelId, version, file, format, DateTimeOffset.UtcNow);
        _items[$"{repo}|{modelId}|{file}"] = m;
        return m;
    }

    public bool Remove(string repo, string modelId)
    {
        var keys = _items.Keys.Where(k => k.StartsWith($"{repo}|{modelId}|", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var k in keys) _items.TryRemove(k, out _);
        return keys.Count > 0;
    }
}
