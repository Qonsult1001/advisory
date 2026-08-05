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

    // ─────────────────────────── exposure ledger (recall / asset tracking) ───────────────────────────
    // Every artifact the proxy SERVES is recorded as an "exposure": a specific ASSET (developer machine)
    // now has that exact version installed. Enterprise asset detail is captured at serve time — enough for
    // a security team to LOCATE the machine (hostname/IP/MAC/OS), identify the OWNER (IT-issued developer,
    // department, asset tag), and prove REMEDIATION (first/last seen, pull count, resolved-by/at). Network
    // + request fields (IP, pip/python/os User-Agent) come from the HTTP request itself; the richer machine
    // fields (hostname/MAC/OS/dept/assetTag) come from the X-Advisory-Asset header IT injects into its
    // pushed pip config — absent header degrades gracefully to IP+token, missing fields render as unknown.
    // When a version is LATER revoked, every asset holding it flips to Recall=true → the org-wide "installed
    // copies that must be removed" worklist. Persisted so a restart never loses an open recall.

    /// <summary>Enterprise asset detail for one endpoint that pulled a package. Every field is optional —
    /// what's present depends on what IT injects; the proxy fills IP/UA fields itself.</summary>
    public record AssetInfo(
        string? Hostname, string? Ip, string? Mac, string? Os,
        string? Department, string? AssetTag, string? OsUser,
        string? PipVersion, string? PythonVersion, string? Platform,
        string? Project = null);   // the project/app this pull belongs to (from IT/CI config) — drives the per-project SBOM

    public record Exposure(
        Ecosystem Ecosystem, string Name, string Version,
        string User,                      // IT-issued developer identity (token) or "unattributed:<ip>"
        AssetInfo Asset,                  // enterprise machine detail
        DateTimeOffset FirstSeen, DateTimeOffset LastSeen, int PullCount,
        bool Recall, string? Cve, string? SafeVersion,
        bool Resolved, string? ResolvedBy, DateTimeOffset? ResolvedAt);

    private readonly ConcurrentDictionary<string, Exposure> _exposure = new(StringComparer.OrdinalIgnoreCase);
    private string ExposurePath => _path + ".exposure.json";

    // An exposure is keyed by the ASSET, not just the user — the same developer on two machines is two
    // installed copies to recall. Prefer a stable machine id (hostname → MAC → asset tag → IP), fall back
    // to the user identity so a tokenless/headerless pull still records a distinct row.
    private static string AssetId(AssetInfo a, string user)
        => a.Hostname ?? a.Mac ?? a.AssetTag ?? a.Ip ?? user;
    private static string ExpKey(Ecosystem eco, string name, string version, string assetId)
        => $"{eco}|{name}|{version}|{assetId}";

    /// <summary>Record that we served this exact version to this asset (it now has it installed). Idempotent
    /// per {eco,name,version,asset}; bumps last-seen + pull count and merges any newly-supplied asset fields
    /// on a re-pull.</summary>
    public void RecordServed(Ecosystem eco, string name, string version, string user, AssetInfo asset)
    {
        var key = ExpKey(eco, name, version, AssetId(asset, user));
        _exposure.AddOrUpdate(key,
            _ => new Exposure(eco, name, version, user, asset,
                              DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1,
                              false, null, null, false, null, null),
            (_, e) => e with { LastSeen = DateTimeOffset.UtcNow, PullCount = e.PullCount + 1, Asset = MergeAsset(e.Asset, asset) });
        Persist();
    }

    // Keep any field we already learned; fill blanks from the new pull (asset detail can arrive over time).
    private static AssetInfo MergeAsset(AssetInfo old, AssetInfo now) => new(
        old.Hostname ?? now.Hostname, old.Ip ?? now.Ip, old.Mac ?? now.Mac, old.Os ?? now.Os,
        old.Department ?? now.Department, old.AssetTag ?? now.AssetTag, old.OsUser ?? now.OsUser,
        now.PipVersion ?? old.PipVersion, now.PythonVersion ?? old.PythonVersion, now.Platform ?? old.Platform);

    /// <summary>A package version was revoked — flag every asset that has it for recall, attaching the CVE
    /// reason + recommended safe version. Returns the number of assets now on the recall list.</summary>
    public int FlagRecall(Ecosystem eco, string name, string version, string? cve, string? safeVersion)
    {
        int n = 0;
        foreach (var kv in _exposure)
        {
            var e = kv.Value;
            if (e.Ecosystem == eco && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && e.Version == version)
            {
                _exposure[kv.Key] = e with { Recall = true, Cve = cve ?? e.Cve, SafeVersion = safeVersion ?? e.SafeVersion,
                                             Resolved = false, ResolvedBy = null, ResolvedAt = null };
                n++;
            }
        }
        if (n > 0) Persist();
        return n;
    }

    /// <summary>Mark one asset's recall as handled (the vulnerable copy was removed / upgraded), recording
    /// WHO cleared it and WHEN for the audit trail. Keyed by the asset id captured at serve time.</summary>
    public void ResolveExposure(Ecosystem eco, string name, string version, string assetId, string resolvedBy)
    {
        var key = ExpKey(eco, name, version, assetId);
        if (_exposure.TryGetValue(key, out var e))
        { _exposure[key] = e with { Resolved = true, ResolvedBy = resolvedBy, ResolvedAt = DateTimeOffset.UtcNow }; Persist(); }
    }

    /// <summary>The recall worklist: served-then-revoked copies still on machines, newest last-seen first.</summary>
    public IReadOnlyList<Exposure> Recalls(bool includeResolved = true)
        => _exposure.Values.Where(e => e.Recall && (includeResolved || !e.Resolved))
                           .OrderByDescending(e => e.LastSeen).ToList();

    /// <summary>EVERY served package (the full install inventory), for the per-project SBOM. Unlike Recalls
    /// this includes clean, still-approved packages — an SBOM is the complete bill of materials, not just
    /// the problems.</summary>
    public IReadOnlyList<Exposure> AllExposures()
        => _exposure.Values.OrderBy(e => e.Asset?.Project ?? "").ThenBy(e => e.Name).ToList();

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
                if (exp is not null)
                    foreach (var e in exp)
                    {
                        // Skip records written before the asset model existed (Asset would be null) — they
                        // can't be keyed or displayed; a fresh serve re-records them with full detail.
                        if (e.Asset is null) continue;
                        _exposure[ExpKey(e.Ecosystem, e.Name, e.Version, AssetId(e.Asset, e.User))] = e;
                    }
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
