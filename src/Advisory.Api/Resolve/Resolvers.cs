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
    private readonly ILogger<PyPiResolver> _log;
    public Ecosystem Ecosystem => Ecosystem.PyPI;
    public PyPiResolver(IHttpClientFactory f, ILogger<PyPiResolver> log) { _http = f.CreateClient("resolve"); _log = log; }

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
                    if (string.IsNullOrEmpty(name)) continue;
                    // An exact "==X" pins the version; anything else (a range like ">=1.21,<3", or no
                    // constraint) means pip would install the NEWEST version that satisfies it. Evaluating
                    // "*" made OSV return every advisory for the package (old CVEs pip would never hit) and
                    // blocked safe trees (#170). Resolve unpinned/range deps to the real latest version.
                    var pinned = Regex.Match(spec, @"==\s*([0-9][\w.\-]*)").Groups[1].Value;
                    var ver = !string.IsNullOrEmpty(pinned) ? pinned : await LatestVersionAsync(name, ct);
                    deps.Add(new PackageRef(Ecosystem.PyPI, name, string.IsNullOrEmpty(ver) ? "*" : ver));
                }
            }
        }
        catch { /* unresolved node still scanned at its own level */ }
        return deps;
    }

    // The package's newest version, per PyPI's own info.version (what `pip install <name>` resolves to
    // for an unconstrained/range dependency). Cached per resolve-tree walk to avoid refetching.
    private readonly Dictionary<string, string?> _latestCache = new(StringComparer.OrdinalIgnoreCase);
    private async Task<string?> LatestVersionAsync(string name, CancellationToken ct)
    {
        if (_latestCache.TryGetValue(name, out var cached)) return cached;
        string? latest = null;
        // Two quick attempts — these lightweight metadata lookups sometimes lose the race for the shared
        // resolve client under load; a transient cancel must not degrade to "*" (which over-flags CVEs).
        for (var attempt = 0; attempt < 2 && string.IsNullOrEmpty(latest); attempt++)
        {
            try
            {
                using var doc = JsonDocument.Parse(await _http.GetStringAsync($"https://pypi.org/pypi/{name}/json", ct));
                if (doc.RootElement.TryGetProperty("info", out var info) && info.TryGetProperty("version", out var v))
                    latest = v.GetString();
            }
            catch when (attempt == 0) { /* transient — one quick retry below */ }
            catch (Exception ex) { _log.LogDebug("resolve latest {Name} failed: {Err}", name, ex.Message); }
        }
        // Only cache a SUCCESS. A transient timeout/cancel must not stick as null (which would fall back
        // to "*" forever on this singleton) — leave it uncached so a later evaluation retries and gets the
        // real version. Falling back to "*" makes OSV return every advisory for the package (#170).
        if (!string.IsNullOrEmpty(latest)) { _latestCache[name] = latest; _log.LogDebug("resolve latest {Name} -> {Ver}", name, latest); }
        return latest;
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
