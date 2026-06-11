using System.Text.Json;
using PkgFirewall.Api.Models;
using PkgFirewall.Api.VulnSources;

namespace PkgFirewall.Api.Catalog;

// --- Catalog DTOs (the JFrog-style package overview, from open sources only) ---

public record CatalogVersion(string Version, string? Published, bool Deprecated);
public record CatalogMaintainer(string Name, string? Email);
public record ScorecardCheck(string Name, int? Score, string? Reason);
public record CatalogScorecard(double? Overall, string? Date, IReadOnlyList<ScorecardCheck> Checks,
    string? Source = null,   // "OpenSSF" or "deps.dev"
    long? Stars = null,      // GitHub stars (from deps.dev) — a signal even when no full scorecard
    string? RepoUrl = null); // the resolved repository, so the UI can link it
public record CatalogVuln(string Id, string Severity, double? Cvss, string? Summary,
    string? FixedVersion, bool KnownExploited, IReadOnlyList<AdvisoryRef>? References,
    double? Epss = null, IReadOnlyList<string>? Aliases = null, IReadOnlyList<string>? Cwes = null);

public record CatalogOverview(
    string Ecosystem,
    string Name,
    string? Version,            // resolved (latest if not specified)
    string? Description,
    string? License,
    string? Homepage,
    string? Repository,
    string? LatestVersion,
    int VersionCount,
    IReadOnlyList<CatalogVersion> RecentVersions,
    IReadOnlyList<string> AllVersions,
    IReadOnlyList<CatalogMaintainer> Maintainers,
    long? DownloadsLastMonth,
    bool Deprecated,
    string? DeprecatedReason,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<CatalogVuln> Vulnerabilities,
    CatalogScorecard? Scorecard,
    string Verdict,            // Clean / Vulnerable / Caution
    IReadOnlyList<string> Notes,
    OperationalRisk? OperationalRisk = null);  // JFrog Xray-style operational-risk analysis

/// <summary>
/// Aggregates a JFrog-Catalog-style package overview entirely from FREE public sources:
/// npm registry + npm downloads API, PyPI JSON API, OSV.dev (vulns), CISA KEV (exploited flag),
/// and OpenSSF Scorecard (health). npm + PyPI are wired live; other ecosystems return a
/// "supported soon" overview. No vendor data, no licence cost.
/// </summary>
public class CatalogService
{
    private readonly HttpClient _http;
    private readonly IHttpClientFactory _factory;
    private readonly OsvSource _osv;
    private readonly KevSource _kev;
    private readonly EpssSource _epss;
    private readonly OpRiskService _opRisk;

    public CatalogService(IHttpClientFactory f, OsvSource osv, KevSource kev, EpssSource epss, OpRiskService opRisk)
    {
        _http = f.CreateClient("catalog");
        _factory = f;
        _osv = osv; _kev = kev; _epss = epss; _opRisk = opRisk;
    }

    // Every ecosystem is live: OSV covers vulnerabilities for all; rich metadata is fetched
    // per-registry where a free API exists.
    public bool IsLiveEcosystem(Ecosystem e) => e is Ecosystem.npm or Ecosystem.PyPI;

    // --- Package search (autocomplete + results list) ---
    private List<string>? _pypiNames;   // cached PyPI project names (lazy)
    private readonly SemaphoreSlim _pypiLock = new(1, 1);

    public record SearchHit(string Name, string Ecosystem, string? Description, int? VersionCount, string? LatestVersion);

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(Ecosystem eco, string query, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchHit>();
        return eco == Ecosystem.npm ? await SearchNpm(query, limit, ct) : await SearchPyPi(query, limit, ct);
    }

    private async Task<IReadOnlyList<SearchHit>> SearchNpm(string q, int limit, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://registry.npmjs.org/-/v1/search?text={Uri.EscapeDataString(q)}&size={limit}", ct));
            var hits = new List<SearchHit>();
            if (doc.RootElement.TryGetProperty("objects", out var objs))
                foreach (var o in objs.EnumerateArray())
                {
                    var p = o.GetProperty("package");
                    string? S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    hits.Add(new SearchHit(S("name") ?? "", "npm", S("description"), null, S("version")));
                }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    private async Task<IReadOnlyList<SearchHit>> SearchPyPi(string q, int limit, CancellationToken ct)
    {
        var names = await PyPiNames(ct);
        if (names.Count == 0) return Array.Empty<SearchHit>();
        var ql = q.ToLowerInvariant();
        // exact/prefix first, then substring; cap before enrich.
        var matched = names.Where(n => n.ToLowerInvariant().Contains(ql))
            .OrderBy(n => n.ToLowerInvariant() == ql ? 0 : n.ToLowerInvariant().StartsWith(ql) ? 1 : 2)
            .ThenBy(n => n.Length).Take(limit).ToList();
        // light enrich: just name + ecosystem (avoid N live calls); detail page fetches the rest.
        return matched.Select(n => new SearchHit(n, "PyPI", null, null, null)).ToList();
    }

    private async Task<List<string>> PyPiNames(CancellationToken ct)
    {
        if (_pypiNames is not null) return _pypiNames;
        await _pypiLock.WaitAsync(ct);
        try
        {
            if (_pypiNames is not null) return _pypiNames;
            // Dedicated client with a long timeout — the simple index is large (~10MB, ~25s).
            var client = _factory.CreateClient("catalog-index");
            client.Timeout = TimeSpan.FromSeconds(120);
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://pypi.org/simple/");
            req.Headers.Add("Accept", "application/vnd.pypi.simple.v1+json");
            req.Headers.Add("User-Agent", "PkgFirewall-Catalog");
            using var resp = await client.SendAsync(req, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var list = new List<string>();
            if (doc.RootElement.TryGetProperty("projects", out var pr))
                foreach (var p in pr.EnumerateArray())
                    if (p.TryGetProperty("name", out var n) && n.GetString() is { } s) list.Add(s);
            _pypiNames = list;
            return list;
        }
        catch { return _pypiNames ?? new List<string>(); }
        finally { _pypiLock.Release(); }
    }

    public async Task<CatalogOverview> OverviewAsync(Ecosystem eco, string name, string? version, CancellationToken ct)
    {
        try
        {
            var ov = eco switch
            {
                Ecosystem.npm => await NpmOverview(name, version, ct),
                Ecosystem.PyPI => await PyPiOverview(name, version, ct),
                Ecosystem.NuGet => await NuGetOverview(name, version, ct),
                Ecosystem.Cargo => await CargoOverview(name, version, ct),
                Ecosystem.Go => await GoOverview(name, version, ct),
                Ecosystem.HuggingFace => await HuggingFaceOverview(name, version, ct),
                _ => await VulnsOnlyOverview(eco, name, version, ct),
            };
            // JFrog-style operational risk (EOL, version age, # new versions, cadence health).
            if (_opRisk.Supports(eco))
            {
                try { ov = ov with { OperationalRisk = await _opRisk.AnalyzeAsync(eco, name, ov.Version, ct) }; }
                catch { /* advisory dimension — never kill the overview */ }
            }
            return ov;
        }
        catch (Exception ex)
        {
            // Metadata fetch failed — still return whatever OSV gives us, never a dead screen.
            try
            {
                var vulns = await Vulns(eco, name, version ?? "", ct);
                return Finalize(eco.ToString(), name, version, null, null, null, null, version, 0,
                    new(), new(), null, false, null, new(), vulns, null,
                    new List<string> { $"Metadata unavailable ({ex.Message}); showing vulnerabilities only." });
            }
            catch
            {
                return new CatalogOverview(eco.ToString(), name, version, null, null, null, null, null, 0,
                    Array.Empty<CatalogVersion>(), Array.Empty<string>(), Array.Empty<CatalogMaintainer>(), null, false, null,
                    Array.Empty<string>(), Array.Empty<CatalogVuln>(), null, "Unknown",
                    new[] { $"Could not load package: {ex.Message}" });
            }
        }
    }

    /// <summary>Fallback: vulnerabilities only (OSV), no registry metadata. Used if an ecosystem has no free metadata API.</summary>
    private async Task<CatalogOverview> VulnsOnlyOverview(Ecosystem eco, string name, string? version, CancellationToken ct)
    {
        var vulns = await Vulns(eco, name, version ?? "", ct);
        return Finalize(eco.ToString(), name, version, null, null, null, null, version, 0,
            new(), new(), null, false, null, new(), vulns, null,
            new List<string> { "Rich metadata not available for this ecosystem; vulnerabilities shown via OSV." });
    }

    // ---------- npm ----------
    private async Task<CatalogOverview> NpmOverview(string name, string? version, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"https://registry.npmjs.org/{Uri.EscapeDataString(name)}", ct));
        var root = doc.RootElement;
        var latest = root.TryGetProperty("dist-tags", out var dt) && dt.TryGetProperty("latest", out var l) ? l.GetString() : null;
        var resolved = version ?? latest;

        var versionsEl = root.TryGetProperty("versions", out var v) ? v : default;
        var timeEl = root.TryGetProperty("time", out var t) ? t : default;
        var allVersions = versionsEl.ValueKind == JsonValueKind.Object
            ? versionsEl.EnumerateObject().Select(p => p.Name).ToList() : new List<string>();

        var recent = allVersions.AsEnumerable().Reverse().Take(8).Select(ver =>
        {
            string? published = timeEl.ValueKind == JsonValueKind.Object && timeEl.TryGetProperty(ver, out var pt) ? pt.GetString() : null;
            bool dep = versionsEl.TryGetProperty(ver, out var vv) && vv.TryGetProperty("deprecated", out _);
            return new CatalogVersion(ver, published, dep);
        }).ToList();

        // Resolved-version document for description/license/deps/deprecation.
        JsonElement verDoc = default;
        if (resolved is not null && versionsEl.ValueKind == JsonValueKind.Object) versionsEl.TryGetProperty(resolved, out verDoc);
        string? Str(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;

        var desc = Str(verDoc, "description") ?? Str(root, "description");
        var license = Str(verDoc, "license") ?? Str(root, "license");
        var homepage = Str(verDoc, "homepage") ?? Str(root, "homepage");
        var repo = root.TryGetProperty("repository", out var rp) && rp.TryGetProperty("url", out var ru) ? ru.GetString() : null;
        var deprecated = verDoc.ValueKind == JsonValueKind.Object && verDoc.TryGetProperty("deprecated", out var depEl);
        var depReason = deprecated ? (Str(verDoc, "deprecated")) : null;

        var maintainers = root.TryGetProperty("maintainers", out var ms) && ms.ValueKind == JsonValueKind.Array
            ? ms.EnumerateArray().Select(m => new CatalogMaintainer(m.TryGetProperty("name", out var mn) ? mn.GetString() ?? "" : "",
                m.TryGetProperty("email", out var me) ? me.GetString() : null)).ToList()
            : new List<CatalogMaintainer>();

        var deps = verDoc.ValueKind == JsonValueKind.Object && verDoc.TryGetProperty("dependencies", out var dd) && dd.ValueKind == JsonValueKind.Object
            ? dd.EnumerateObject().Select(p => $"{p.Name}@{p.Value.GetString()}").ToList() : new List<string>();

        long? downloads = await NpmDownloads(name, ct);
        var vulns = await Vulns(Ecosystem.npm, name, resolved ?? "", ct);
        var scorecard = await Scorecard(repo ?? homepage, ct);

        var notes = new List<string>();
        if (deprecated) notes.Add("Package version is deprecated.");
        return Finalize("npm", name, resolved, desc, license, homepage, repo, latest, allVersions.Count,
            recent, maintainers, downloads, deprecated, depReason, deps, vulns, scorecard, notes, allVersions);
    }

    private async Task<long?> NpmDownloads(string name, CancellationToken ct)
    {
        try
        {
            using var d = JsonDocument.Parse(await _http.GetStringAsync($"https://api.npmjs.org/downloads/point/last-month/{Uri.EscapeDataString(name)}", ct));
            return d.RootElement.TryGetProperty("downloads", out var dl) ? dl.GetInt64() : null;
        }
        catch { return null; }
    }

    // ---------- PyPI ----------
    private async Task<CatalogOverview> PyPiOverview(string name, string? version, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"https://pypi.org/pypi/{Uri.EscapeDataString(name)}/json", ct));
        var info = doc.RootElement.GetProperty("info");
        string? Str(string p) => info.TryGetProperty(p, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;

        var latest = Str("version");
        var resolved = version ?? latest;
        var releasesEl = doc.RootElement.TryGetProperty("releases", out var rel) ? rel : default;
        var allVersions = releasesEl.ValueKind == JsonValueKind.Object ? releasesEl.EnumerateObject().Select(p => p.Name).ToList() : new List<string>();
        var recent = allVersions.AsEnumerable().Reverse().Take(8).Select(ver =>
        {
            string? published = null;
            if (releasesEl.TryGetProperty(ver, out var files) && files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0)
                published = files[0].TryGetProperty("upload_time_iso_8601", out var ut) ? ut.GetString() : null;
            return new CatalogVersion(ver, published, false);
        }).ToList();

        var repo = Str("home_page");
        if (info.TryGetProperty("project_urls", out var pu) && pu.ValueKind == JsonValueKind.Object)
            foreach (var p in pu.EnumerateObject())
                if (p.Name.Contains("source", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("repo", StringComparison.OrdinalIgnoreCase))
                    { repo = p.Value.GetString(); break; }

        var maintainers = new List<CatalogMaintainer>();
        if (Str("author") is { Length: > 0 } a) maintainers.Add(new CatalogMaintainer(a, Str("author_email")));

        var deps = info.TryGetProperty("requires_dist", out var rd) && rd.ValueKind == JsonValueKind.Array
            ? rd.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => x.Length > 0).Take(40).ToList() : new List<string>();

        var vulns = await Vulns(Ecosystem.PyPI, name, resolved ?? "", ct);
        var scorecard = await Scorecard(repo, ct);
        return Finalize("PyPI", name, resolved, Str("summary"), Str("license"), Str("home_page"), repo, latest,
            allVersions.Count, recent, maintainers, null, false, null, deps, vulns, scorecard, new List<string>(), allVersions);
    }

    // ---------- NuGet (api.nuget.org) ----------
    private async Task<CatalogOverview> NuGetOverview(string name, string? version, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        // Registration index gives versions; flat-container gives the full version list.
        var versions = new List<string>();
        try
        {
            using var idx = JsonDocument.Parse(await _http.GetStringAsync($"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(lower)}/index.json", ct));
            if (idx.RootElement.TryGetProperty("versions", out var vs) && vs.ValueKind == JsonValueKind.Array)
                versions = vs.EnumerateArray().Select(e => e.GetString()).Where(x => x is not null).Cast<string>().ToList();
        }
        catch { }
        var latest = versions.LastOrDefault();
        var resolved = version ?? latest;

        // Catalog/registration entry for description, license, project URL.
        string? desc = null, license = null, project = null, repo = null; var maintainers = new List<CatalogMaintainer>();
        try
        {
            using var reg = JsonDocument.Parse(await _http.GetStringAsync($"https://api.nuget.org/v3/registration5-gz-semver2/{Uri.EscapeDataString(lower)}/index.json", ct));
            // Walk to the latest catalog entry.
            if (reg.RootElement.TryGetProperty("items", out var pages) && pages.GetArrayLength() > 0)
            {
                var lastPage = pages[pages.GetArrayLength() - 1];
                if (lastPage.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    var entry = items[items.GetArrayLength() - 1].GetProperty("catalogEntry");
                    desc = Prop(entry, "description"); license = Prop(entry, "licenseExpression") ?? Prop(entry, "licenseUrl");
                    project = Prop(entry, "projectUrl"); repo = project;
                    var authors = Prop(entry, "authors");
                    if (!string.IsNullOrEmpty(authors)) maintainers.Add(new CatalogMaintainer(authors, null));
                }
            }
        }
        catch { }

        var recent = versions.AsEnumerable().Reverse().Take(8).Select(v => new CatalogVersion(v, null, false)).ToList();
        var vulns = await Vulns(Ecosystem.NuGet, name, resolved ?? "", ct);
        var scorecard = await Scorecard(repo ?? project, ct);
        return Finalize("NuGet", name, resolved, desc, license, project, repo, latest, versions.Count,
            recent, maintainers, null, false, null, new(), vulns, scorecard, new());
    }

    // ---------- Cargo (crates.io) ----------
    private async Task<CatalogOverview> CargoOverview(string name, string? version, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"https://crates.io/api/v1/crates/{Uri.EscapeDataString(name)}", ct));
        var crate = doc.RootElement.GetProperty("crate");
        string? P(string p) => Prop(crate, p);
        var latest = P("max_stable_version") ?? P("newest_version");
        var resolved = version ?? latest;
        long? downloads = crate.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Number ? dl.GetInt64() : null;
        var repo = P("repository");
        var versions = doc.RootElement.TryGetProperty("versions", out var vs) && vs.ValueKind == JsonValueKind.Array
            ? vs.EnumerateArray().Take(8).Select(v => new CatalogVersion(Prop(v, "num") ?? "", Prop(v, "created_at"), false)).ToList()
            : new List<CatalogVersion>();
        var vulns = await Vulns(Ecosystem.Cargo, name, resolved ?? "", ct);
        var scorecard = await Scorecard(repo, ct);
        return Finalize("Cargo", name, resolved, P("description"), null, P("homepage") ?? repo, repo, latest,
            (crate.TryGetProperty("versions", out var allv) && allv.ValueKind == JsonValueKind.Array) ? allv.GetArrayLength() : versions.Count,
            versions, new(), downloads, false, null, new(), vulns, scorecard, new());
    }

    // ---------- Go (deps.dev) ----------
    private async Task<CatalogOverview> GoOverview(string name, string? version, CancellationToken ct)
    {
        // deps.dev free API. Get the default (latest) version first if none supplied.
        string? resolved = version;
        string? repo = null, license = null;
        try
        {
            using var pkg = JsonDocument.Parse(await _http.GetStringAsync($"https://api.deps.dev/v3/systems/GO/packages/{Uri.EscapeDataString(name)}", ct));
            if (pkg.RootElement.TryGetProperty("versions", out var vs) && vs.ValueKind == JsonValueKind.Array && vs.GetArrayLength() > 0)
            {
                var def = vs.EnumerateArray().FirstOrDefault(v => v.TryGetProperty("isDefault", out var d) && d.GetBoolean());
                if (def.ValueKind == JsonValueKind.Undefined) def = vs[vs.GetArrayLength() - 1];
                resolved ??= def.TryGetProperty("versionKey", out var vk) && vk.TryGetProperty("version", out var vv) ? vv.GetString() : null;
            }
        }
        catch { }
        var vulns = await Vulns(Ecosystem.Go, name, resolved ?? "", ct);
        // Go module path often IS the repo (github.com/...).
        if (name.StartsWith("github.com", StringComparison.OrdinalIgnoreCase)) repo = "https://" + name;
        var scorecard = await Scorecard(repo, ct);
        return Finalize("Go", name, resolved, null, license, repo, repo, resolved, 0,
            new(), new(), null, false, null, new(), vulns, scorecard,
            new List<string> { "Go metadata via deps.dev; module path used as repository." });
    }

    // ---------- Hugging Face (huggingface.co/api) ----------
    private async Task<CatalogOverview> HuggingFaceOverview(string name, string? version, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"https://huggingface.co/api/models/{name}", ct));
        var root = doc.RootElement;
        long? downloads = root.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Number ? dl.GetInt64() : null;
        var author = Prop(root, "author");
        var maintainers = string.IsNullOrEmpty(author) ? new List<CatalogMaintainer>() : new() { new CatalogMaintainer(author, null) };
        var sha = Prop(root, "sha");
        var notes = new List<string> { "Hugging Face model — gate it through the weights scanner (pickle/safetensors) before use." };
        if (root.TryGetProperty("gated", out var g) && g.ValueKind == JsonValueKind.True) notes.Add("Model is gated (access-restricted).");
        // OSV doesn't cover HF; vulns will be empty — that's expected.
        var vulns = await Vulns(Ecosystem.HuggingFace, name, version ?? "main", ct);
        var likes = root.TryGetProperty("likes", out var lk) && lk.ValueKind == JsonValueKind.Number ? (long?)lk.GetInt64() : null;
        return Finalize("HuggingFace", name, version ?? sha, Prop(root, "pipeline_tag"), null,
            $"https://huggingface.co/{name}", $"https://huggingface.co/{name}", "main", 0,
            new(), maintainers, downloads ?? likes, false, null, new(), vulns, null, notes);
    }

    private static string? Prop(JsonElement e, string p)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;

    // ---------- shared: vulns (OSV + KEV), scorecard (OpenSSF) ----------
    private async Task<List<CatalogVuln>> Vulns(Ecosystem eco, string name, string version, CancellationToken ct)
    {
        await _kev.EnsureLoaded(ct);
        var res = await _osv.QueryAsync(new PackageRef(eco, name, version), ct);
        var list = new List<CatalogVuln>();
        foreach (var f in res.Findings)
        {
            // EPSS keyed by CVE id (prefer a CVE alias over the GHSA id).
            var cve = f.Aliases?.FirstOrDefault(a => a.StartsWith("CVE", StringComparison.OrdinalIgnoreCase)) ?? f.Id;
            double? epss = null;
            try { var (sc, st, _) = await _epss.ScoreAsync(cve, ct); if (st == SourceStatus.Ok) epss = sc; } catch { }
            list.Add(new CatalogVuln(
                f.Id, f.Severity.ToString(), f.CvssScore, f.Summary, f.FixedVersion,
                _kev.IsKnownExploited(f.Id) || (f.Aliases?.Any(_kev.IsKnownExploited) ?? false),
                f.References, epss, f.Aliases, f.Cwes));
        }
        return list;
    }

    /// <summary>
    /// Project health. Resolves the GitHub slug from the package's repo URL, then:
    /// 1) OpenSSF Scorecard (pre-computed, full 18-check detail) if available;
    /// 2) deps.dev project (live, broader coverage) for stars + scorecard when OpenSSF has none.
    /// Always returns a card carrying the resolved repo URL + stars when a repo exists, so the UI
    /// can show "repo resolved, no scorecard published" rather than "no repo".
    /// </summary>
    private async Task<CatalogScorecard?> Scorecard(string? repoUrl, CancellationToken ct)
    {
        var slug = GithubSlug(repoUrl);
        if (slug is null) return null;
        var repo = $"https://github.com/{slug}";

        // 1) OpenSSF Scorecard — full check detail.
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync($"https://api.securityscorecards.dev/projects/github.com/{slug}", ct));
            var root = doc.RootElement;
            var overall = root.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : (double?)null;
            var date = root.TryGetProperty("date", out var d) ? d.GetString() : null;
            var checks = root.TryGetProperty("checks", out var ch) && ch.ValueKind == JsonValueKind.Array
                ? ch.EnumerateArray().Select(c => new ScorecardCheck(
                    c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    c.TryGetProperty("score", out var s2) && s2.ValueKind == JsonValueKind.Number ? s2.GetInt32() : null,
                    c.TryGetProperty("reason", out var r) ? r.GetString() : null)).ToList()
                : new List<ScorecardCheck>();
            if (overall is not null)
                return new CatalogScorecard(overall, date, checks, "OpenSSF", await Stars(slug, ct), repo);
        }
        catch { /* fall through to deps.dev */ }

        // 2) deps.dev project — stars always, scorecard when present.
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://api.deps.dev/v3/projects/github.com%2F{Uri.EscapeDataString(slug)}", ct));
            var root = doc.RootElement;
            long? stars = root.TryGetProperty("starsCount", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt64() : null;
            double? overall = null; var checks = new List<ScorecardCheck>();
            if (root.TryGetProperty("scorecard", out var scd) && scd.ValueKind == JsonValueKind.Object)
            {
                if (scd.TryGetProperty("overallScore", out var os) && os.ValueKind == JsonValueKind.Number) overall = os.GetDouble();
                if (scd.TryGetProperty("checks", out var ch) && ch.ValueKind == JsonValueKind.Array)
                    checks = ch.EnumerateArray().Select(c => new ScorecardCheck(
                        c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        c.TryGetProperty("score", out var s2) && s2.ValueKind == JsonValueKind.Number ? s2.GetInt32() : null,
                        c.TryGetProperty("documentation", out var doc2) && doc2.TryGetProperty("shortDescription", out var sd) ? sd.GetString() : null)).ToList();
            }
            return new CatalogScorecard(overall, null, checks, "deps.dev", stars, repo);
        }
        catch
        {
            // Repo resolved but no health data anywhere — still return the repo so UI links it.
            return new CatalogScorecard(null, null, new List<ScorecardCheck>(), null, null, repo);
        }
    }

    private async Task<long?> Stars(string slug, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://api.deps.dev/v3/projects/github.com%2F{Uri.EscapeDataString(slug)}", ct));
            return doc.RootElement.TryGetProperty("starsCount", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt64() : null;
        }
        catch { return null; }
    }

    private static string? GithubSlug(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.Contains("github.com", StringComparison.OrdinalIgnoreCase)) return null;
        var i = url.IndexOf("github.com", StringComparison.OrdinalIgnoreCase);
        var path = url[(i + "github.com".Length)..].TrimStart('/', ':').Replace(".git", "");
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : null;
    }

    private static CatalogOverview Finalize(string eco, string name, string? resolved, string? desc, string? license,
        string? homepage, string? repo, string? latest, int versionCount, List<CatalogVersion> recent,
        List<CatalogMaintainer> maintainers, long? downloads, bool deprecated, string? depReason,
        List<string> deps, List<CatalogVuln> vulns, CatalogScorecard? scorecard, List<string> notes,
        List<string>? allVersions = null)
    {
        var kev = vulns.Any(v => v.KnownExploited);
        var hi = vulns.Any(v => v.Severity is "High" or "Critical");
        var verdict = kev || hi ? "Vulnerable" : vulns.Count > 0 ? "Caution" : "Clean";
        if (kev) notes.Insert(0, "Contains a known-exploited (CISA KEV) vulnerability.");
        if (scorecard?.Overall is double o && o < 5) notes.Add($"Low OpenSSF Scorecard health ({o:0.0}/10).");
        // Newest-first version list for the dropdown.
        var versions = (allVersions ?? recent.Select(r => r.Version).ToList());
        versions = versions.AsEnumerable().Reverse().ToList();
        return new CatalogOverview(eco, name, resolved, desc, license, homepage, repo, latest, versionCount,
            recent, versions, maintainers, downloads, deprecated, depReason, deps, vulns, scorecard, verdict, notes);
    }
}
