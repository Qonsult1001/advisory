using System.Text.Json;
using System.Text.RegularExpressions;
using Advisory.Api.Models;

namespace Advisory.Api.Resolve;

/// <summary>Shared BFS tree-walk: cycle-safe, depth-bounded, dedup by name@version.</summary>
public abstract class TreeWalker
{
    protected abstract Task<IEnumerable<PackageRef>> DirectDepsAsync(PackageRef pkg, CancellationToken ct);

    protected async Task<IReadOnlyList<DepNode>> WalkAsync(
        PackageRef root, Ecosystem eco, int maxDepth, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = new List<DepNode>();
        var queue = new Queue<(PackageRef pkg, int depth, string? parent)>();
        queue.Enqueue((root, 0, null));

        while (queue.Count > 0)
        {
            var (pkg, depth, parent) = queue.Dequeue();
            var key = $"{pkg.Name}@{pkg.Version}";
            if (!seen.Add(key)) continue;                 // cycle / dup guard
            nodes.Add(new DepNode(pkg, depth, parent));
            if (depth >= maxDepth) continue;

            foreach (var dep in await DirectDepsAsync(pkg, ct))
                queue.Enqueue((dep, depth + 1, pkg.Name));
        }
        return nodes;
    }
}

/// <summary>PyPI resolver — reads dependency metadata from the JSON API.</summary>
public class PyPiResolver : TreeWalker, IDependencyResolver
{
    private readonly HttpClient _http;
    public Ecosystem Ecosystem => Ecosystem.PyPI;
    public PyPiResolver(IHttpClientFactory f) => _http = f.CreateClient("resolve");

    public Task<IReadOnlyList<DepNode>> ResolveAsync(PackageRef root, int maxDepth, CancellationToken ct)
        => WalkAsync(root, Ecosystem.PyPI, maxDepth, ct);

    protected override async Task<IEnumerable<PackageRef>> DirectDepsAsync(PackageRef pkg, CancellationToken ct)
    {
        var deps = new List<PackageRef>();
        try
        {
            var url = $"https://pypi.org/pypi/{pkg.Name}/{pkg.Version}/json";
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
            if (doc.RootElement.GetProperty("info").TryGetProperty("requires_dist", out var rd)
                && rd.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rd.EnumerateArray())
                {
                    var spec = item.GetString();
                    if (string.IsNullOrEmpty(spec)) continue;
                    // skip optional/extra deps; keep core runtime deps
                    if (spec.Contains("extra ==", StringComparison.OrdinalIgnoreCase)) continue;
                    var name = Regex.Match(spec, @"^[A-Za-z0-9_.\-]+").Value;
                    var ver = Regex.Match(spec, @"==\s*([0-9][\w.\-]*)").Groups[1].Value;
                    if (!string.IsNullOrEmpty(name))
                        deps.Add(new PackageRef(Ecosystem.PyPI, name,
                            string.IsNullOrEmpty(ver) ? "*" : ver));
                }
            }
        }
        catch { /* unresolved node still scanned at its own level */ }
        return deps;
    }
}

/// <summary>npm resolver — reads the dependencies map from the registry.</summary>
public class NpmResolver : TreeWalker, IDependencyResolver
{
    private readonly HttpClient _http;
    public Ecosystem Ecosystem => Ecosystem.npm;
    public NpmResolver(IHttpClientFactory f) => _http = f.CreateClient("resolve");

    public Task<IReadOnlyList<DepNode>> ResolveAsync(PackageRef root, int maxDepth, CancellationToken ct)
        => WalkAsync(root, Ecosystem.npm, maxDepth, ct);

    protected override async Task<IEnumerable<PackageRef>> DirectDepsAsync(PackageRef pkg, CancellationToken ct)
    {
        var deps = new List<PackageRef>();
        try
        {
            var url = $"https://registry.npmjs.org/{pkg.Name}/{pkg.Version}";
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
            if (doc.RootElement.TryGetProperty("dependencies", out var d)
                && d.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in d.EnumerateObject())
                {
                    var ver = Regex.Match(p.Value.GetString() ?? "", @"[0-9][\w.\-]*").Value;
                    deps.Add(new PackageRef(Ecosystem.npm, p.Name,
                        string.IsNullOrEmpty(ver) ? "*" : ver));
                }
            }
        }
        catch { }
        return deps;
    }
}
