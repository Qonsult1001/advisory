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

    // Safe-version advice for a BLOCKED package (auto-gate-on-pull): "nearest safe" + "latest safe"
    // versions that actually passed the gate. In-memory observability — the console reads it to turn a
    // blocked verdict into "use this version instead".
    private readonly ConcurrentDictionary<string, (string? Nearest, string? Latest)> _safeVersions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Record the gate-verified safe alternatives for a blocked package version.</summary>
    public void SetSafeVersions(Ecosystem eco, string name, string version, string? nearest, string? latest)
        => _safeVersions[RevKey(eco, name, version)] = (nearest, latest);

    /// <summary>The recommended safe versions for a blocked package, or (null,null) if none recorded.</summary>
    public (string? Nearest, string? Latest) GetSafeVersions(Ecosystem eco, string name, string version)
        => _safeVersions.TryGetValue(RevKey(eco, name, version), out var v) ? v : (null, null);

    // Recent developer requests discovered by the log tailer (auto-gate-on-pull): who asked for what,
    // when. Latest-per-{eco,name} so the console can show a developer the fate of what their install
    // pulled in. In-memory, bounded — pure observability.
    public record DevRequest(Ecosystem Ecosystem, string Name, string? User, DateTimeOffset RequestedAt);
    private readonly ConcurrentDictionary<string, DevRequest> _requests = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Record that a developer requested a not-yet-approved package (from the request log).</summary>
    public void RecordRequest(Ecosystem eco, string name, string? user, DateTimeOffset at)
        => _requests[$"{eco}|{name}"] = new DevRequest(eco, name, user, at);

    /// <summary>Recent developer requests, newest first (for the console "recent requests" view).</summary>
    public IReadOnlyList<DevRequest> RecentRequests(int max = 100)
        => _requests.Values.OrderByDescending(r => r.RequestedAt).Take(max).ToList();

    // ─────────────────────────── exposure ledger (recall list) ───────────────────────────
    // Every artifact the proxy SERVES to a developer is recorded here as an "exposure": that developer
    // now has that exact version on their machine. If the package is LATER revoked (a fresh CVE caught by
    // the per-request re-gate), the matching exposures flip to Recall=true — the org-wide worklist of
    // "installed copies that must be removed", attributed to the developer (by IT-issued token) who pulled
    // it. This is what makes retroactive revocation actionable instead of silent. Persisted so a restart
    // doesn't lose an open recall.
    public record Exposure(Ecosystem Ecosystem, string Name, string Version, string User,
        DateTimeOffset ServedAt, bool Recall, string? Cve, string? SafeVersion, bool Resolved);

    private readonly ConcurrentDictionary<string, Exposure> _exposure = new(StringComparer.OrdinalIgnoreCase);
    private string ExposurePath => _path + ".exposure.json";
    private static string ExpKey(Ecosystem eco, string name, string version, string user)
        => $"{eco}|{name}|{version}|{user}";

    /// <summary>Record that we served this exact version to this developer (they now have it installed).
    /// Idempotent per {eco,name,version,user}; refreshes the served-at time on a re-pull.</summary>
    public void RecordServed(Ecosystem eco, string name, string version, string user)
    {
        var key = ExpKey(eco, name, version, user);
        _exposure.AddOrUpdate(key,
            _ => new Exposure(eco, name, version, user, DateTimeOffset.UtcNow, false, null, null, false),
            (_, e) => e with { ServedAt = DateTimeOffset.UtcNow });
        Persist();
    }

    /// <summary>A package version was revoked — flag every developer who has it for recall, attaching the
    /// CVE reason and the recommended safe version so the console can tell each of them exactly what to do.
    /// Returns the number of developers now on the recall list for this version.</summary>
    public int FlagRecall(Ecosystem eco, string name, string version, string? cve, string? safeVersion)
    {
        int n = 0;
        foreach (var kv in _exposure)
        {
            var e = kv.Value;
            if (e.Ecosystem == eco && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && e.Version == version)
            {
                _exposure[kv.Key] = e with { Recall = true, Cve = cve ?? e.Cve, SafeVersion = safeVersion ?? e.SafeVersion, Resolved = false };
                n++;
            }
        }
        if (n > 0) Persist();
        return n;
    }

    /// <summary>Mark one developer's recall as handled (they uninstalled / moved to the safe version).</summary>
    public void ResolveExposure(Ecosystem eco, string name, string version, string user)
    {
        var key = ExpKey(eco, name, version, user);
        if (_exposure.TryGetValue(key, out var e)) { _exposure[key] = e with { Resolved = true }; Persist(); }
    }

    /// <summary>The recall worklist: served-then-revoked copies still on machines, newest first. Only
    /// exposures flagged for recall (a real revocation) appear; resolved ones are included so the console
    /// can show progress, but callers can filter on Resolved.</summary>
    public IReadOnlyList<Exposure> Recalls(bool includeResolved = true)
        => _exposure.Values.Where(e => e.Recall && (includeResolved || !e.Resolved))
                           .OrderByDescending(e => e.ServedAt).ToList();

    /// <summary>Wipe all scan history AND revocations (the operator "reset demo data" action).</summary>
    public void ClearAll()
    {
        _scans.Clear();
        _revoked.Clear();
        _safeVersions.Clear();
        _exposure.Clear();
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
        // Always record at least the root package as a component, plus any components that carried a
        // finding — so the artifact view never shows "0 components" for something we actually scanned.
        var sbom = new List<ScanComponent> { new(pkg.Name, pkg.Version, 0, null, "root") };
        foreach (var tf in tree)
        {
            var name = tf.Component.Split('@')[0];
            if (!sbom.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                sbom.Add(new ScanComponent(name, tf.Component.Contains('@') ? tf.Component.Split('@')[1] : "", tf.Depth, pkg.Name, tf.Depth == 1 ? "Direct" : "Transitive"));
        }
        var scan = new StoredScan(repo, pkg.Ecosystem, pkg.Name, pkg.Version, pkg.FileName,
            result.Decision.ToString(), verdict, Math.Max(result.ComponentsEvaluated, sbom.Count),
            Sev("Critical"), Sev("High"), Sev("Medium"), Sev("Low"),
            vulns, sbom, DateTimeOffset.UtcNow);
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
        try
        {
            if (File.Exists(ExposurePath))
            {
                var exp = JsonSerializer.Deserialize<List<Exposure>>(File.ReadAllText(ExposurePath), Json);
                if (exp is not null) foreach (var e in exp) _exposure[ExpKey(e.Ecosystem, e.Name, e.Version, e.User)] = e;
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
            try { File.WriteAllText(ExposurePath, JsonSerializer.Serialize(_exposure.Values.ToList(), Json)); }
            catch { /* best-effort persistence */ }
        }
    }
}
