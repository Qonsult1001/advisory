using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Advisory.Api.Gate;
using Advisory.Api.Models;
using Advisory.Api.Resolve;

namespace Advisory.Api.Scan;

/// <summary>One vulnerability in a stored scan, with the full dependency path to it.</summary>
public record ScanVuln(
    string Id, string Severity, double? Cvss, string? Summary, string? FixedVersion,
    bool KnownExploited, string Component, IReadOnlyList<string> ImpactPath,
    IReadOnlyList<string>? Aliases, IReadOnlyList<string>? Cwes, IReadOnlyList<AdvisoryRef>? References);

/// <summary>A stored, dated scan of one artifact — the indexed result, not a fresh-on-open compute.</summary>
public record StoredScan(
    string Repository, Ecosystem Ecosystem, string Name, string Version, string? FileName,
    string Decision, string Verdict, int ComponentsScanned,
    int Critical, int High, int Medium, int Low,
    IReadOnlyList<ScanVuln> Vulnerabilities,
    IReadOnlyList<ScanComponent> Sbom,
    DateTimeOffset ScannedAt);

public record ScanComponent(string Name, string Version, int Depth, string? Parent, string Relation);

/// <summary>
/// Persistent scan index. Stores the full gate result per artifact (keyed repo|name|version) so the
/// Scans List shows real counts + a "Latest Scan" timestamp, and the artifact view reads a stored
/// scan instead of recomputing. Mirrors how Xray pre-indexes. Backed by a JSON file (ScanIndexPath),
/// loaded on startup; re-scan is explicit (ScanArtifactAsync) or on first view if absent.
/// </summary>
public class ScanStore
{
    private readonly string _path;
    private readonly IServiceScopeFactory _scopes;
    private readonly IEnumerable<IDependencyResolver> _resolvers;
    private readonly ConcurrentDictionary<string, StoredScan> _scans = new(StringComparer.OrdinalIgnoreCase);
    // Operator-revoked packages: an explicit denylist. A revoked package is removed from approved AND
    // must never be re-promoted by the bridge — it stays held in quarantine until explicitly cleared.
    private readonly ConcurrentDictionary<string, byte> _revoked = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _io = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    public ScanStore(IConfiguration cfg, IServiceScopeFactory scopes, IEnumerable<IDependencyResolver> resolvers)
    {
        _path = cfg["ScanIndexPath"] ?? "scans.json";
        _scopes = scopes; _resolvers = resolvers;
        Load();
    }

    private static string Key(string repo, string name, string version) => $"{repo}|{name}|{version}";
    private static string RevKey(Ecosystem eco, string name, string version) => $"{eco}|{name}|{version}";

    public StoredScan? Get(string repo, string name, string version)
        => _scans.TryGetValue(Key(repo, name, version), out var s) ? s : null;

    /// <summary>Mark a package as operator-revoked (denylisted) — the bridge will not re-promote it.</summary>
    public void MarkRevoked(Ecosystem eco, string name, string version)
    { _revoked[RevKey(eco, name, version)] = 1; Persist(); }

    /// <summary>Clear a revocation so the package can flow through the gate again.</summary>
    public void ClearRevoked(Ecosystem eco, string name, string version)
    { _revoked.TryRemove(RevKey(eco, name, version), out _); Persist(); }

    public bool IsRevoked(Ecosystem eco, string name, string version)
        => _revoked.ContainsKey(RevKey(eco, name, version));

    /// <summary>Wipe all scan history AND revocations (the operator "reset demo data" action).</summary>
    public void ClearAll()
    {
        _scans.Clear();
        _revoked.Clear();
        Persist();
    }

    public IReadOnlyList<StoredScan> ForRepository(string repo)
        => _scans.Values.Where(s => string.Equals(s.Repository, repo, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(s => s.ScannedAt).ToList();

    public IReadOnlyList<StoredScan> All() => _scans.Values.OrderByDescending(s => s.ScannedAt).ToList();

    /// <summary>Persist a decision the PromotionBridge already computed (no re-scan), so the Quarantine
    /// view can show, per package, what the pipeline did and why. Cheap + best-effort observability.</summary>
    public Task RecordDecisionAsync(string repo, PackageRef pkg, GateResult result)
    {
        var tree = result.TreeFindings ?? Array.Empty<TreeFinding>();
        var vulns = tree.Select(tf =>
        {
            var f = tf.Finding;
            return new ScanVuln(f.Id, f.Severity.ToString(), f.CvssScore, f.Summary, f.FixedVersion,
                f.KnownExploited, tf.Component, new[] { pkg.Name, tf.Component.Split('@')[0] }, f.Aliases, f.Cwes, f.References);
        }).ToList();
        int Sev(string s) => vulns.Count(v => v.Severity == s);
        var verdict = result.Decision == GateDecision.Allow
            ? (vulns.Count == 0 ? "Clean" : "Caution") : "Vulnerable";
        var scan = new StoredScan(repo, pkg.Ecosystem, pkg.Name, pkg.Version, pkg.FileName,
            result.Decision.ToString(), verdict, result.ComponentsEvaluated,
            Sev("Critical"), Sev("High"), Sev("Medium"), Sev("Low"),
            vulns, Array.Empty<ScanComponent>(), DateTimeOffset.UtcNow);
        _scans[Key(repo, pkg.Name, pkg.Version)] = scan;
        Persist();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Run the FULL gate (resolves the transitive tree, queries OSV per node), build a stored scan
    /// with per-vuln impact paths, persist it, and return it. This is the indexing step.
    /// </summary>
    public async Task<StoredScan> ScanArtifactAsync(string repo, PackageRef pkg, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IGateEngine>();
        var result = await gate.EvaluateAsync(pkg, ct);

        // Build a parent-lookup from the resolved tree to reconstruct multi-hop impact paths.
        var tree = result.TreeFindings ?? Array.Empty<TreeFinding>();
        // The gate also exposes ComponentsEvaluated; resolve the tree once more for the SBOM + parent map
        // (cheap: cached upstream). If a resolver exists, use it; else single-node.
        var resolver = _resolvers.FirstOrDefault(r => r.Ecosystem == pkg.Ecosystem);
        var nodes = resolver is not null ? await resolver.ResolveAsync(pkg, 8, ct)
                                         : new List<DepNode> { new(pkg, 0, null) };
        var parentByName = nodes.GroupBy(n => n.Package.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Parent, StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> PathTo(string componentName)
        {
            // Walk parent links from the component back to the root.
            var chain = new List<string> { componentName };
            var guard = 0; var cur = componentName;
            while (parentByName.TryGetValue(cur, out var parent) && parent is not null && guard++ < 32)
            {
                chain.Insert(0, parent); cur = parent;
            }
            if (chain.Count == 1) chain.Insert(0, pkg.Name); // at least root → component
            return chain;
        }

        var vulns = tree.Select(tf =>
        {
            var compName = tf.Component.Split('@')[0];
            var f = tf.Finding;
            return new ScanVuln(f.Id, f.Severity.ToString(), f.CvssScore, f.Summary, f.FixedVersion,
                f.KnownExploited, tf.Component, PathTo(compName), f.Aliases, f.Cwes, f.References);
        }).ToList();

        var sbom = nodes.Select(n => new ScanComponent(n.Package.Name, n.Package.Version, n.Depth, n.Parent,
            n.Depth == 0 ? "root" : n.Depth == 1 ? "Direct" : "Transitive")).ToList();

        int Sev(string s) => vulns.Count(v => v.Severity == s);
        var verdict = result.Decision == GateDecision.Allow
            ? (vulns.Count == 0 ? "Clean" : "Caution") : "Vulnerable";

        var scan = new StoredScan(repo, pkg.Ecosystem, pkg.Name, pkg.Version, pkg.FileName,
            result.Decision.ToString(), verdict, nodes.Count,
            Sev("Critical"), Sev("High"), Sev("Medium"), Sev("Low"),
            vulns, sbom, DateTimeOffset.UtcNow);

        _scans[Key(repo, pkg.Name, pkg.Version)] = scan;
        Persist();
        return scan;
    }

    /// <summary>Get the stored scan, running + indexing one if it's not present yet.</summary>
    public async Task<StoredScan> GetOrScanAsync(string repo, PackageRef pkg, CancellationToken ct)
        => Get(repo, pkg.Name, pkg.Version) ?? await ScanArtifactAsync(repo, pkg, ct);

    private string RevokedPath => _path + ".revoked.json";

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var list = JsonSerializer.Deserialize<List<StoredScan>>(File.ReadAllText(_path), Json);
                if (list is not null) foreach (var s in list) _scans[Key(s.Repository, s.Name, s.Version)] = s;
            }
        }
        catch { /* corrupt index → start fresh, never crash */ }
        try
        {
            if (File.Exists(RevokedPath))
            {
                var rev = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RevokedPath), Json);
                if (rev is not null) foreach (var k in rev) _revoked[k] = 1;
            }
        }
        catch { /* ignore */ }
    }

    private void Persist()
    {
        lock (_io)
        {
            try { File.WriteAllText(_path, JsonSerializer.Serialize(_scans.Values.ToList(), Json)); }
            catch { /* best-effort persistence */ }
            try { File.WriteAllText(RevokedPath, JsonSerializer.Serialize(_revoked.Keys.ToList(), Json)); }
            catch { /* best-effort persistence */ }
        }
    }
}
