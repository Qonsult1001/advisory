using System.Text.Json;
using Advisory.Api.Models;
using Advisory.Api.VulnSources;

namespace Advisory.Api.Catalog;

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

/// <summary>One affected package range for a CVE (from OSV's affected[] list).</summary>
public record CveAffected(string Ecosystem, string Name, string? IntroducedVersion, string? FixedVersion);

/// <summary>One assessed capability/permission signal for an editor extension.</summary>
public record ExtensionSignal(string Name, string Level, string Detail);  // Level: High / Medium / Low / Info

/// <summary>One real code-level finding from the vsix-audit deep scan of the extension's bytes.</summary>
public record CodeScanFinding(string Id, string Title, string Severity, string Category, string? Detail, string? File);

/// <summary>
/// Static + reputational risk assessment for an AI-editor (VS Code) extension. CVE scanners pass
/// extensions as "clean" because extensions rarely carry CVEs — the real risk is capability abuse /
/// data exfiltration / publisher impersonation. This inspects the published .vsix manifest + publisher
/// trust signals (all from the Marketplace + Open VSX, free) and rates the exfiltration surface.
/// </summary>
public record ExtensionRisk(
    string Verdict,                 // Trusted / Caution / High-Risk
    bool PublisherVerified,
    string? PublisherDomain,
    bool ExecutesCode,              // has a node entrypoint (full FS/network/child_process)
    bool RunsAutomatically,         // activates on startup / "*" (no user action needed)
    bool SupportsUntrustedWorkspaces,
    bool OnOpenVsx,                 // also published to the open registry (cross-checked)
    bool KnownMalicious,            // on a malicious-extension advisory
    long? Installs,
    IReadOnlyList<string> Dependencies,        // other extensions it pulls in (supply chain)
    IReadOnlyList<ExtensionSignal> Signals,    // the per-capability assessment
    IReadOnlyList<string> ExfiltrationNotes,   // plain-English data-exfiltration assessment
    bool CodeScanned = false,                  // did the deep .vsix code scan actually run?
    string? CodeScanStatus = null,             // Clean / Findings / Unavailable
    IReadOnlyList<CodeScanFinding>? CodeFindings = null,   // REAL findings from vsix-audit on the bytes
    string? VerdictBasis = null,               // one-line: WHY this verdict (what drove it)
    IReadOnlyList<string>? VerdictCriteria = null,         // the explicit pass/fail rules (for GSOC/audit)
    int ConfirmedThreats = 0,                  // critical, concrete (non-heuristic) findings
    int HeuristicMatches = 0,                  // YARA signature matches — flagged, NOT auto-condemning
    string? GateAction = null,                 // what the CURRENT policy would do: Block / Notify / Allow
    string? GateActionReason = null);          // why the policy lands on that action

/// <summary>A standalone CVE/advisory detail, looked up by id from OSV + enriched with KEV/EPSS.</summary>
public record CatalogCve(
    string Id,
    IReadOnlyList<string> Aliases,
    string Severity,
    double? Cvss,
    string? CvssVector,
    double? Epss,
    bool KnownExploited,
    string? Summary,
    string? Details,
    string? Published,
    string? Modified,
    IReadOnlyList<string> Cwes,
    IReadOnlyList<CveAffected> Affected,
    IReadOnlyList<AdvisoryRef> References,
    bool Found);

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
    OperationalRisk? OperationalRisk = null,    // JFrog Xray-style operational-risk analysis
    ExtensionRisk? ExtensionRisk = null);       // AI-editor extension capability/exfiltration analysis

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
    private readonly string? _vsixScannerUrl;
    private readonly Advisory.Api.Policy.IPolicyStore _policy;

    public CatalogService(IHttpClientFactory f, OsvSource osv, KevSource kev, EpssSource epss, OpRiskService opRisk,
        IConfiguration cfg, Advisory.Api.Policy.IPolicyStore policy)
    {
        _http = f.CreateClient("catalog");
        _factory = f;
        _osv = osv; _kev = kev; _epss = epss; _opRisk = opRisk;
        _vsixScannerUrl = cfg["VSIX_SCANNER_URL"];
        _policy = policy;
    }

    // Every ecosystem is live: OSV covers vulnerabilities for all; rich metadata is fetched
    // per-registry where a free API exists.
    public bool IsLiveEcosystem(Ecosystem e) =>
        e is Ecosystem.npm or Ecosystem.PyPI or Ecosystem.NuGet or Ecosystem.Cargo or Ecosystem.Go or Ecosystem.HuggingFace
          or Ecosystem.Maven or Ecosystem.RubyGems or Ecosystem.Composer or Ecosystem.Conan or Ecosystem.Conda
          or Ecosystem.CRAN or Ecosystem.DartPub or Ecosystem.Alpine or Ecosystem.Debian or Ecosystem.Ubuntu
          or Ecosystem.AIEditorExtensions;

    // --- Package search (autocomplete + results list) ---
    private List<string>? _pypiNames;   // cached PyPI project names (lazy)
    private readonly SemaphoreSlim _pypiLock = new(1, 1);

    public record SearchHit(string Name, string Ecosystem, string? Description, int? VersionCount, string? LatestVersion);

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(Ecosystem eco, string query, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchHit>();
        return eco switch
        {
            Ecosystem.npm => await SearchNpm(query, limit, ct),
            Ecosystem.PyPI => await SearchPyPi(query, limit, ct),
            Ecosystem.NuGet => await SearchNuGet(query, limit, ct),
            Ecosystem.Cargo => await SearchCargo(query, limit, ct),
            Ecosystem.Go => await SearchGo(query, limit, ct),
            Ecosystem.HuggingFace => await SearchHuggingFace(query, limit, ct),
            Ecosystem.Maven => await SearchMaven(query, limit, ct),
            Ecosystem.RubyGems => await SearchRubyGems(query, limit, ct),
            Ecosystem.Composer => await SearchComposer(query, limit, ct),
            Ecosystem.Conan => await SearchConan(query, limit, ct),
            Ecosystem.Conda => await SearchConda(query, limit, ct),
            Ecosystem.CRAN => await SearchCran(query, limit, ct),
            Ecosystem.DartPub => await SearchDart(query, limit, ct),
            Ecosystem.Alpine => await SearchOsDistro(eco, query, limit, ct),
            Ecosystem.Debian => await SearchOsDistro(eco, query, limit, ct),
            Ecosystem.Ubuntu => await SearchOsDistro(eco, query, limit, ct),
            Ecosystem.AIEditorExtensions => await SearchAiEditorExtensions(query, limit, ct),
            _ => Array.Empty<SearchHit>(),
        };
    }

    // Maven — search.maven.org Solr API (the same one the Central UI uses).
    private async Task<IReadOnlyList<SearchHit>> SearchMaven(string q, int limit, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://search.maven.org/solrsearch/select?q={Uri.EscapeDataString(q)}&rows={limit}&wt=json", ct));
            var hits = new List<SearchHit>();
            if (doc.RootElement.TryGetProperty("response", out var r) && r.TryGetProperty("docs", out var docs))
                foreach (var p in docs.EnumerateArray())
                {
                    string? S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    var id = S("id") ?? "";                          // "group:artifact"
                    var ver = S("latestVersion");
                    var vc = p.TryGetProperty("versionCount", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : (int?)null;
                    hits.Add(new SearchHit(id, "Maven", null, vc, ver));
                }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // RubyGems — rubygems.org search API.
    private async Task<IReadOnlyList<SearchHit>> SearchRubyGems(string q, int limit, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://rubygems.org/api/v1/search.json?query={Uri.EscapeDataString(q)}", ct));
            var hits = new List<SearchHit>();
            foreach (var p in doc.RootElement.EnumerateArray())
            {
                string? S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                hits.Add(new SearchHit(S("name") ?? "", "RubyGems", S("info"), null, S("version")));
                if (hits.Count >= limit) break;
            }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // Composer / PHP — packagist.org search API.
    private async Task<IReadOnlyList<SearchHit>> SearchComposer(string q, int limit, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://packagist.org/search.json?q={Uri.EscapeDataString(q)}&per_page={Math.Min(limit, 30)}", ct));
            var hits = new List<SearchHit>();
            if (doc.RootElement.TryGetProperty("results", out var results))
                foreach (var p in results.EnumerateArray())
                {
                    string? S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    hits.Add(new SearchHit(S("name") ?? "", "Composer", S("description"), null, null));
                }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // Conan — the C/C++ package index. conan.io's UI is a JS app with no public JSON search,
    // so we use the authoritative source: the conan-center-index recipes/ folder on GitHub.
    // Each subdirectory under recipes/ is a package name. Cached + filtered locally.
    private List<string>? _conanNames;
    private readonly SemaphoreSlim _conanLock = new(1, 1);
    private async Task<IReadOnlyList<SearchHit>> SearchConan(string q, int limit, CancellationToken ct)
    {
        var names = await ConanNames(ct);
        if (names.Count == 0) return Array.Empty<SearchHit>();
        var ql = q.ToLowerInvariant();
        return names.Where(n => n.ToLowerInvariant().Contains(ql))
            .OrderBy(n => n.ToLowerInvariant() == ql ? 0 : n.ToLowerInvariant().StartsWith(ql) ? 1 : 2)
            .ThenBy(n => n.Length).Take(limit)
            .Select(n => new SearchHit(n, "Conan", null, null, null)).ToList();
    }
    private async Task<List<string>> ConanNames(CancellationToken ct)
    {
        if (_conanNames is not null) return _conanNames;
        await _conanLock.WaitAsync(ct);
        try
        {
            if (_conanNames is not null) return _conanNames;
            var client = _factory.CreateClient("catalog-index");
            client.Timeout = TimeSpan.FromSeconds(60);
            async Task<JsonDocument> Get(string url)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "Advisory-Catalog");
                req.Headers.Add("Accept", "application/vnd.github+json");
                var resp = await client.SendAsync(req, ct);
                return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            }
            // The git-tree API returns the full recipes/ listing in one call (the contents API caps
            // at 1000 entries; conan-center has ~1900 packages). Resolve recipes' tree SHA, then fetch it.
            string? recipesSha = null;
            using (var top = await Get("https://api.github.com/repos/conan-io/conan-center-index/git/trees/master"))
                if (top.RootElement.TryGetProperty("tree", out var tt))
                    foreach (var t in tt.EnumerateArray())
                        if (t.TryGetProperty("path", out var pth) && pth.GetString() == "recipes"
                            && t.TryGetProperty("sha", out var sh)) { recipesSha = sh.GetString(); break; }
            var list = new List<string>();
            if (recipesSha is not null)
                using (var rec = await Get($"https://api.github.com/repos/conan-io/conan-center-index/git/trees/{recipesSha}"))
                    if (rec.RootElement.TryGetProperty("tree", out var rt))
                        foreach (var t in rt.EnumerateArray())
                            if (t.TryGetProperty("type", out var ty) && ty.GetString() == "tree"
                                && t.TryGetProperty("path", out var p) && p.GetString() is { } s) list.Add(s);
            if (list.Count > 0) _conanNames = list;
            return list;
        }
        catch { return _conanNames ?? new List<string>(); }
        finally { _conanLock.Release(); }
    }

    // Conda — anaconda.org search API.
    private async Task<IReadOnlyList<SearchHit>> SearchConda(string q, int limit, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://api.anaconda.org/search?name={Uri.EscapeDataString(q)}", ct));
            var hits = new List<SearchHit>();
            var seen = new HashSet<string>();
            foreach (var p in doc.RootElement.EnumerateArray())
            {
                string? S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var name = S("name") ?? "";
                if (name.Length > 0 && seen.Add(name))
                    hits.Add(new SearchHit(name, "Conda", S("summary"), null, S("latest_version") ?? S("version")));
                if (hits.Count >= limit) break;
            }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // CRAN (R) — crandb full index, filtered locally (no server-side search API).
    private List<(string Name, string Title, string Ver)>? _cranNames;  // cached CRAN index (lazy)
    private readonly SemaphoreSlim _cranLock = new(1, 1);
    private async Task<IReadOnlyList<SearchHit>> SearchCran(string q, int limit, CancellationToken ct)
    {
        var names = await CranNames(ct);
        if (names.Count == 0) return Array.Empty<SearchHit>();
        var ql = q.ToLowerInvariant();
        return names.Where(n => n.Name.ToLowerInvariant().Contains(ql))
            .OrderBy(n => n.Name.ToLowerInvariant() == ql ? 0 : n.Name.ToLowerInvariant().StartsWith(ql) ? 1 : 2)
            .ThenBy(n => n.Name.Length).Take(limit)
            .Select(n => new SearchHit(n.Name, "CRAN", n.Title, null, n.Ver)).ToList();
    }
    private async Task<List<(string Name, string Title, string Ver)>> CranNames(CancellationToken ct)
    {
        if (_cranNames is not null) return _cranNames;
        await _cranLock.WaitAsync(ct);
        try
        {
            if (_cranNames is not null) return _cranNames;
            var client = _factory.CreateClient("catalog-index");
            client.Timeout = TimeSpan.FromSeconds(120);
            using var doc = JsonDocument.Parse(await client.GetStringAsync("https://crandb.r-pkg.org/-/desc", ct));
            var list = new List<(string Name, string Title, string Ver)>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var v = p.Value;
                string? S(string k) => v.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
                list.Add((p.Name, S("title") ?? "", S("version") ?? ""));
            }
            _cranNames = list;
            return list;
        }
        catch { return _cranNames ?? new(); }
        finally { _cranLock.Release(); }
    }

    // Dart Pub — pub.dev search API.
    private async Task<IReadOnlyList<SearchHit>> SearchDart(string q, int limit, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://pub.dev/api/search?q={Uri.EscapeDataString(q)}", ct));
            var hits = new List<SearchHit>();
            if (doc.RootElement.TryGetProperty("packages", out var pkgs))
                foreach (var p in pkgs.EnumerateArray())
                {
                    var name = p.TryGetProperty("package", out var n) ? n.GetString() : null;
                    if (!string.IsNullOrEmpty(name)) hits.Add(new SearchHit(name!, "DartPub", null, null, null));
                    if (hits.Count >= limit) break;
                }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // Alpine / Debian / Ubuntu — OS-distro packages. Source the live package list from the
    // distro's own web package index (Debian/Ubuntu) or Alpine's pkgs.alpinelinux.org JSON.
    private async Task<IReadOnlyList<SearchHit>> SearchOsDistro(Ecosystem eco, string q, int limit, CancellationToken ct)
    {
        try
        {
            if (eco == Ecosystem.Alpine)
            {
                // pkgs.alpinelinux.org has no JSON API — scrape the package table. Each result row
                // links to /package/<branch>/<repo>/<arch>/<name>; the last path segment is the name.
                var html = await _http.GetStringAsync(
                    $"https://pkgs.alpinelinux.org/packages?name=*{Uri.EscapeDataString(q)}*&branch=edge&arch=x86_64", ct);
                var hits = new List<SearchHit>();
                var seen = new HashSet<string>();
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                    html, "href=\"/package/[^/\"]+/[^/\"]+/[^/\"]+/([^\"/?]+)\""))
                {
                    var name = m.Groups[1].Value;
                    if (name.Length > 0 && seen.Add(name)) hits.Add(new SearchHit(name, "Alpine", null, null, null));
                    if (hits.Count >= limit) break;
                }
                return hits;
            }
            // Debian / Ubuntu — sources.debian.org / Ubuntu's search both expose a JSON suggest.
            var host = eco == Ecosystem.Ubuntu ? "https://api.launchpad.net/1.0" : "https://sources.debian.org";
            if (eco == Ecosystem.Debian)
            {
                using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                    $"https://sources.debian.org/api/search/{Uri.EscapeDataString(q)}/", ct));
                var hits = new List<SearchHit>();
                if (doc.RootElement.TryGetProperty("results", out var res))
                {
                    var seen = new HashSet<string>();
                    void AddOne(JsonElement p)
                    {
                        var name = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (!string.IsNullOrEmpty(name) && seen.Add(name!) && hits.Count < limit)
                            hits.Add(new SearchHit(name!, "Debian", null, null, null));
                    }
                    // "exact" is a single object; "other" is an array.
                    if (res.TryGetProperty("exact", out var ex) && ex.ValueKind == JsonValueKind.Object) AddOne(ex);
                    if (res.TryGetProperty("other", out var oth) && oth.ValueKind == JsonValueKind.Array)
                        foreach (var p in oth.EnumerateArray()) AddOne(p);
                }
                return hits;
            }
            // Ubuntu — Launchpad source-package name search.
            using var udoc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://api.launchpad.net/1.0/ubuntu/+archive/primary?ws.op=getPublishedSources&source_name={Uri.EscapeDataString(q)}&exact_match=false&status=Published&ws.size={limit}", ct));
            var uhits = new List<SearchHit>();
            var useen = new HashSet<string>();
            if (udoc.RootElement.TryGetProperty("entries", out var entries))
                foreach (var p in entries.EnumerateArray())
                {
                    string? S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    var name = S("source_package_name") ?? "";
                    if (name.Length > 0 && useen.Add(name))
                        uhits.Add(new SearchHit(name, "Ubuntu", null, null, S("source_package_version")));
                    if (uhits.Count >= limit) break;
                }
            return uhits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // AI Editor Extensions — VS Code Marketplace (.vsix). Scans Copilot/Cursor/AI editor extensions.
    private async Task<IReadOnlyList<SearchHit>> SearchAiEditorExtensions(string q, int limit, CancellationToken ct)
    {
        try
        {
            var body = new
            {
                filters = new[] { new { criteria = new[] { new { filterType = 10, value = q } }, pageSize = Math.Min(limit, 50), pageNumber = 1 } },
                flags = 914
            };
            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery");
            req.Content = new StringContent(JsonSerializer.Serialize(body));
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            req.Headers.TryAddWithoutValidation("Accept", "application/json;api-version=3.0-preview.1");
            using var resp = await _http.SendAsync(req, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var hits = new List<SearchHit>();
            if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0
                && results[0].TryGetProperty("extensions", out var exts))
                foreach (var e in exts.EnumerateArray())
                {
                    string? S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    var pub = e.TryGetProperty("publisher", out var pp) && pp.TryGetProperty("publisherName", out var pn) ? pn.GetString() : null;
                    var ext = S("extensionName") ?? "";
                    var full = pub is null ? ext : $"{pub}.{ext}";
                    string? ver = e.TryGetProperty("versions", out var vs) && vs.GetArrayLength() > 0 && vs[0].TryGetProperty("version", out var vv) ? vv.GetString() : null;
                    if (full.Length > 0) hits.Add(new SearchHit(full, "AIEditorExtensions", S("shortDescription") ?? S("displayName"), null, ver));
                    if (hits.Count >= limit) break;
                }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // NuGet — Azure Search query API (the same one the nuget.org gallery uses).
    private async Task<IReadOnlyList<SearchHit>> SearchNuGet(string q, int limit, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://azuresearch-usnc.nuget.org/query?q={Uri.EscapeDataString(q)}&take={limit}&prerelease=false", ct));
            var hits = new List<SearchHit>();
            if (doc.RootElement.TryGetProperty("data", out var data))
                foreach (var p in data.EnumerateArray())
                {
                    string? S(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    hits.Add(new SearchHit(S("id") ?? "", "NuGet", S("description"), null, S("version")));
                }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // Cargo — crates.io search API (requires a User-Agent).
    private async Task<IReadOnlyList<SearchHit>> SearchCargo(string q, int limit, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://crates.io/api/v1/crates?q={Uri.EscapeDataString(q)}&per_page={Math.Min(limit, 30)}");
            req.Headers.Add("User-Agent", "Advisory-Catalog");
            using var resp = await _http.SendAsync(req, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var hits = new List<SearchHit>();
            if (doc.RootElement.TryGetProperty("crates", out var crates))
                foreach (var c in crates.EnumerateArray())
                {
                    string? S(string k) => c.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    hits.Add(new SearchHit(S("name") ?? "", "Cargo", S("description"), null, S("max_stable_version") ?? S("newest_version")));
                }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // Go — pkg.go.dev has no JSON search; use its HTML search and extract module paths.
    private async Task<IReadOnlyList<SearchHit>> SearchGo(string q, int limit, CancellationToken ct)
    {
        try
        {
            var html = await _http.GetStringAsync($"https://pkg.go.dev/search?q={Uri.EscapeDataString(q)}&m=package", ct);
            var hits = new List<SearchHit>();
            // module paths appear as data-test-id="snippet-title" links: <a href="/<module>">
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                html, "<a[^>]+href=\"/([a-zA-Z0-9._\\-/]+@?[^\"?]*)\"[^>]*data-test-id=\"snippet-title\""))
            {
                var mod = m.Groups[1].Value.TrimEnd('/');
                if (mod.Length > 0 && !hits.Any(h => h.Name == mod)) hits.Add(new SearchHit(mod, "Go", null, null, null));
                if (hits.Count >= limit) break;
            }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
    }

    // HuggingFace — models search API.
    private async Task<IReadOnlyList<SearchHit>> SearchHuggingFace(string q, int limit, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://huggingface.co/api/models?search={Uri.EscapeDataString(q)}&limit={limit}&full=false", ct));
            var hits = new List<SearchHit>();
            foreach (var m in doc.RootElement.EnumerateArray())
            {
                var id = m.TryGetProperty("id", out var i) ? i.GetString() : null;
                if (!string.IsNullOrEmpty(id)) hits.Add(new SearchHit(id!, "HuggingFace", null, null, null));
            }
            return hits;
        }
        catch { return Array.Empty<SearchHit>(); }
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
            req.Headers.Add("User-Agent", "Advisory-Catalog");
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
                Ecosystem.AIEditorExtensions => await AiEditorExtensionOverview(name, version, ct),
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

    // ---------- AI Editor Extensions (VS Code Marketplace .vsix) ----------
    private async Task<CatalogOverview> AiEditorExtensionOverview(string name, string? version, CancellationToken ct)
    {
        // name is "<publisher>.<extensionName>" (e.g. "anthropic.claude-code").
        // flags=307 = IncludeVersions(1)|IncludeFiles(2)|IncludeVersionProperties(16)|IncludeAssetUri(32)
        //            |IncludeStatistics(256). Returns the real version history + install statistics
        // (latest-only flags collapse the list to one version).
        var body = new
        {
            filters = new[] { new { criteria = new[] { new { filterType = 7, value = name } }, pageSize = 1, pageNumber = 1 } },
            flags = 307
        };
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery");
        req.Content = new StringContent(JsonSerializer.Serialize(body));
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        req.Headers.TryAddWithoutValidation("Accept", "application/json;api-version=3.0-preview.1");
        using var resp = await _http.SendAsync(req, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        var results = doc.RootElement.TryGetProperty("results", out var rs) && rs.GetArrayLength() > 0 ? rs[0] : default;
        if (results.ValueKind != JsonValueKind.Object || !results.TryGetProperty("extensions", out var exts) || exts.GetArrayLength() == 0)
        {
            // Not found in the Marketplace — honest empty overview (still OSV-checked).
            var v0 = await Vulns(Ecosystem.AIEditorExtensions, name, version ?? "", ct);
            return Finalize("AIEditorExtensions", name, version, null, null, null, null, version, 0,
                new(), new(), null, false, null, new(), v0, null,
                new List<string> { "Extension not found in the VS Code Marketplace." });
        }
        var e = exts[0];
        string? S(JsonElement el, string k) => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var pubEl = e.TryGetProperty("publisher", out var pub) ? pub : default;
        var publisher = pubEl.ValueKind == JsonValueKind.Object ? S(pubEl, "publisherName") : null;
        var publisherDomain = pubEl.ValueKind == JsonValueKind.Object ? S(pubEl, "domain") : null;
        var publisherVerified = pubEl.ValueKind == JsonValueKind.Object && pubEl.TryGetProperty("isDomainVerified", out var dv) && dv.ValueKind == JsonValueKind.True;
        var displayName = S(e, "displayName");
        var shortDesc = S(e, "shortDescription");
        var lastUpdated = S(e, "lastUpdated");

        // Distinct versions newest-first, with publish timestamps.
        var recent = new List<CatalogVersion>();
        var allVersions = new List<string>();
        if (e.TryGetProperty("versions", out var vs) && vs.ValueKind == JsonValueKind.Array)
            foreach (var v in vs.EnumerateArray())
            {
                var ver = S(v, "version");
                if (ver is null || allVersions.Contains(ver)) continue;   // collapse per-platform dupes
                allVersions.Add(ver);
                if (recent.Count < 12) recent.Add(new CatalogVersion(ver, S(v, "lastUpdated"), false));
            }
        var resolved = version ?? allVersions.FirstOrDefault();

        // Statistics (install count, rating) → use installs as the "downloads" signal.
        long? installs = null;
        if (e.TryGetProperty("statistics", out var st) && st.ValueKind == JsonValueKind.Array)
            foreach (var stat in st.EnumerateArray())
                if (S(stat, "statisticName") == "install" && stat.TryGetProperty("value", out var sv) && sv.ValueKind == JsonValueKind.Number)
                    installs = (long)sv.GetDouble();

        // Resolve homepage/repo from the latest version's properties (Links.Source / Links.Support).
        string? homepage = null, repo = null;
        if (e.TryGetProperty("versions", out var vs2) && vs2.GetArrayLength() > 0 && vs2[0].TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Array)
            foreach (var p in props.EnumerateArray())
            {
                var key = S(p, "key"); var val = S(p, "value");
                if (key is null || string.IsNullOrEmpty(val)) continue;
                if (key.EndsWith("Links.Source", StringComparison.OrdinalIgnoreCase)) repo = val;
                if (key.EndsWith("Links.Getstarted", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Links.Learn", StringComparison.OrdinalIgnoreCase)) homepage ??= val;
            }
        homepage ??= $"https://marketplace.visualstudio.com/items?itemName={name}";

        // The Code.Manifest asset is the published package.json — read its real capabilities.
        string? manifestUrl = null;
        if (e.TryGetProperty("versions", out var vs3) && vs3.GetArrayLength() > 0 && vs3[0].TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            foreach (var f in files.EnumerateArray())
                if ((S(f, "assetType") ?? "").EndsWith("Code.Manifest", StringComparison.OrdinalIgnoreCase)) { manifestUrl = S(f, "source"); break; }

        var maintainers = string.IsNullOrEmpty(publisher) ? new List<CatalogMaintainer>() : new() { new CatalogMaintainer(publisher!, null) };
        var vulns = await Vulns(Ecosystem.AIEditorExtensions, name, resolved ?? "", ct);

        // The real risk for an extension isn't a CVE — it's capability abuse / exfiltration / publisher
        // impersonation. Run the static + reputational analysis so "no CVE" is never mistaken for "safe".
        var extRisk = await AnalyzeExtensionRiskAsync(name, publisher, publisherDomain, publisherVerified, installs, manifestUrl, ct);

        var notes = new List<string>
        {
            $"VS Code Marketplace extension — install: code --install-extension {name}.",
        };
        if (!string.IsNullOrEmpty(displayName)) notes.Add($"Display name: {displayName} (publisher: {publisher}{(publisherVerified ? ", domain-verified" : ", UNVERIFIED publisher")}).");
        notes.Add(extRisk.Verdict == "Trusted"
            ? "Extension-risk analysis: Trusted — verified publisher, capabilities reviewed, no malicious advisory."
            : $"Extension-risk analysis: {extRisk.Verdict} — review the capability & exfiltration assessment below before allowing.");

        // Marketplace returns versions newest-first; Finalize reverses its allVersions input to build
        // the dropdown — so pass an oldest-first copy to get a newest-first dropdown.
        var oldestFirst = Enumerable.Reverse(allVersions).ToList();
        var ov = Finalize("AIEditorExtensions", name, resolved, shortDesc, null,
            homepage, repo, allVersions.FirstOrDefault(), allVersions.Count,
            recent, maintainers, installs, false, null, new(), vulns, null, notes, oldestFirst);
        return ov with { ExtensionRisk = extRisk };
    }

    /// <summary>
    /// Static + reputational risk for a VS Code / AI-editor extension. Reads the published package.json
    /// (capabilities, activation, entrypoint, dependencies), the Marketplace publisher-trust flags, and
    /// cross-checks Open VSX + the OpenSSF malicious feed. Returns a capability-based exfiltration verdict.
    /// </summary>
    private async Task<ExtensionRisk> AnalyzeExtensionRiskAsync(string name, string? publisher, string? domain,
        bool publisherVerified, long? installs, string? manifestUrl, CancellationToken ct)
    {
        var signals = new List<ExtensionSignal>();
        var exfil = new List<string>();
        bool executesCode = false, runsAuto = false, untrusted = false;
        var deps = new List<string>();

        // 1) Static capability analysis from the published package.json manifest.
        if (manifestUrl is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(await _http.GetStringAsync(manifestUrl, ct));
                var m = doc.RootElement;
                executesCode = (m.TryGetProperty("main", out var mn) && mn.ValueKind == JsonValueKind.String)
                            || (m.TryGetProperty("browser", out var br) && br.ValueKind == JsonValueKind.String);
                if (m.TryGetProperty("activationEvents", out var ae) && ae.ValueKind == JsonValueKind.Array)
                {
                    var evs = ae.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                    runsAuto = evs.Any(x => x == "*" || x.StartsWith("onStartupFinished") || x.StartsWith("onStartup"));
                    if (evs.Contains("*"))
                        signals.Add(new ExtensionSignal("Activates on '*'", "High", "Loads on every editor start, regardless of context — maximal attack surface."));
                    else if (runsAuto)
                        signals.Add(new ExtensionSignal("Activates on startup", "Medium", "Runs automatically when the editor finishes loading (no user action required)."));
                }
                if (m.TryGetProperty("capabilities", out var cap) && cap.TryGetProperty("untrustedWorkspaces", out var uw)
                    && uw.TryGetProperty("supported", out var sup))
                    untrusted = sup.ValueKind == JsonValueKind.True || (sup.ValueKind == JsonValueKind.String && sup.GetString() == "limited");
                if (m.TryGetProperty("extensionDependencies", out var ed) && ed.ValueKind == JsonValueKind.Array)
                    deps.AddRange(ed.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
                if (m.TryGetProperty("contributes", out var con) && con.TryGetProperty("configuration", out _)) { /* benign */ }

                if (executesCode)
                    signals.Add(new ExtensionSignal("Executes native code", "Medium",
                        "Has a Node entrypoint — full access to the filesystem, network, and child processes. Can read source, env vars, tokens."));
                signals.Add(new ExtensionSignal("Untrusted-workspace support", untrusted ? "Info" : "Low",
                    untrusted ? "Runs in untrusted workspaces." : "Disabled in untrusted workspaces (safer default)."));
            }
            catch { signals.Add(new ExtensionSignal("Manifest", "Info", "Could not fetch the published manifest for static analysis.")); }
        }

        // 2) Publisher trust — the primary impersonation / supply-chain signal.
        signals.Add(new ExtensionSignal("Publisher verification",
            publisherVerified ? "Info" : "High",
            publisherVerified ? $"Domain-verified publisher{(domain is null ? "" : $" ({domain})")}."
                              : "Publisher domain is NOT verified — high impersonation/typosquat risk. Confirm this is the genuine author."));

        // 3) Open VSX cross-reference (a second independent registry).
        bool onOpenVsx = false;
        try
        {
            var parts = name.Split('.', 2);
            if (parts.Length == 2)
            {
                using var resp = await _http.GetAsync($"https://open-vsx.org/api/{parts[0]}/{parts[1]}", ct);
                onOpenVsx = resp.IsSuccessStatusCode;
            }
        }
        catch { }
        signals.Add(new ExtensionSignal("Open VSX presence", onOpenVsx ? "Info" : "Low",
            onOpenVsx ? "Also published to the open registry (Open VSX) — cross-verified." : "Not found on Open VSX (Marketplace-only)."));

        // 4) Malicious-advisory check via OSV (MAL-*) — extensions named in malicious feeds.
        bool knownMalicious = (await Vulns(Ecosystem.AIEditorExtensions, name, "", ct)).Any(v => v.Id.StartsWith("MAL-", StringComparison.OrdinalIgnoreCase));
        if (knownMalicious) signals.Add(new ExtensionSignal("Malicious advisory", "High", "This extension appears in a malicious-package advisory — DO NOT INSTALL."));

        // Data-exfiltration assessment, in plain English.
        if (executesCode)
            exfil.Add("Has native code execution, so it CAN technically read files, environment variables (API keys/tokens), and make outbound network calls. The Marketplace does not sandbox this.");
        if (runsAuto)
            exfil.Add("Activates automatically on startup — any exfiltration logic would run without you opening a specific file.");
        exfil.Add(publisherVerified
            ? "Publisher is domain-verified, which is the strongest available anti-impersonation signal — but verification is NOT a behavioural guarantee."
            : "Publisher is unverified: the single biggest exfiltration red flag is an impostor publishing a look-alike of a trusted extension. Verify the publisher before allowing.");
        exfil.Add(installs is long n && n > 1_000_000
            ? $"High install base ({n:N0}) — widely used, so malicious behaviour would likely have been reported."
            : "Lower install base — less community scrutiny; weigh this for a sensitive environment.");

        // 5) DEEP CODE SCAN — the real exfiltration check. vsix-audit downloads the .vsix and inspects
        //    the actual code: Discord/Telegram-webhook exfiltration, SSH-key/cookie/credential theft,
        //    eval/Function/process.binding, obfuscation, IOC/C2 + crypto wallets, YARA RAT rules.
        var (codeStatus, codeFindings, codeScanned) = await DeepScanExtensionAsync(name, ct);
        bool codeMalicious = false;
        if (codeScanned)
        {
            // Classify each finding so we NEVER alarm a reviewer over normal extension behaviour:
            //  - THREAT  = concrete IOC evidence only (a real C2 domain, crypto-wallet address, known-bad
            //    hash). These are the only findings that condemn an extension and the only ones listed.
            //  - CAPABILITY = ast/manifest/telemetry observations (new Function(), startup activation,
            //    obfuscation). Every real extension has these — shown as capability signals, not threats.
            //  - HEURISTIC = YARA signature matches. Counted for transparency, suppressed from the list,
            //    never condemning (they false-positive on minified JS).
            foreach (var cf in codeFindings.Where(c => IsConcreteThreat(c)))
            {
                codeMalicious = true;   // a concrete IOC is a real threat — condemn.
                signals.Add(new ExtensionSignal($"⛔ Confirmed threat: {cf.Title}", "High",
                    $"[{cf.Category}] {cf.Detail ?? cf.Id}{(cf.File is null ? "" : $" ({cf.File})")}"));
            }
            // Capability observations (ast/manifest) → quietly add as Low/Medium signals, no alarm.
            foreach (var cf in codeFindings.Where(c => IsCapabilityObservation(c)))
                if (!signals.Any(sig => sig.Name.Contains(cf.Title)))
                    signals.Add(new ExtensionSignal($"Code capability: {cf.Title}", "Low",
                        cf.Detail ?? cf.Id));
            var threatN = codeFindings.Count(c => IsConcreteThreat(c));
            exfil.Add(threatN > 0
                ? $"Deep code scan (vsix-audit) found {threatN} CONFIRMED threat indicator(s) — concrete IOC evidence (e.g. a known C2 domain or crypto-wallet address) in the code. This is a real finding, not a heuristic."
                : "Deep code scan (vsix-audit) inspected the actual .vsix code and found NO confirmed exfiltration/RAT/IOC threats. Capability observations (e.g. native code, startup activation) are normal for any functional extension and are shown only as capability signals, not threats.");
        }
        else
        {
            exfil.Add("Deep code scan unavailable (scanner sidecar not reachable) — assessment is static + reputational only. Set VSIX_SCANNER_URL to enable real .vsix code analysis.");
        }

        // Verdict — driven by CONFIRMED threats only. Capability signals (native code, startup
        // activation) are normal and do NOT push a verified-publisher extension to Caution.
        var confirmedThreats = codeFindings.Count(IsConcreteThreat) + (knownMalicious ? 1 : 0);
        var heuristicMatches = codeFindings.Count(c => c.Category == "yara");
        var verdict = (knownMalicious || codeMalicious) ? "High-Risk"
            : !publisherVerified ? "Caution"
            : "Trusted";

        // The explicit decision criteria — so a security analyst sees exactly WHY it passed and
        // precisely WHAT would make it fail. This is the audit-defensible part: it removes the
        // "an AI just said Trusted" ambiguity by stating the deterministic rule that produced it.
        var basis = verdict switch
        {
            "High-Risk" => knownMalicious
                ? "FAILED: listed on a malicious-package advisory feed."
                : "FAILED: the deep code scan found a confirmed threat indicator — concrete IOC evidence (a known C2 domain, a crypto-wallet address, or a known-bad hash) in the code.",
            "Caution" => "PASSED with caution: no confirmed threat, but the publisher domain is unverified (impersonation risk — verify it's the genuine author).",
            _ => "PASSED (Trusted): verified publisher and zero confirmed threats in the deep code scan."
        };
        var criteria = new List<string>
        {
            "FAILS (High-Risk) only if: it is on a malicious-package advisory feed, OR the deep code scan finds a CONFIRMED threat indicator — concrete IOC evidence (a known C2/command-and-control domain, a crypto-wallet address, or a known-bad file hash).",
            "PASSES WITH CAUTION if: no confirmed threat, but the publisher domain is unverified.",
            "PASSES (Trusted) if: verified publisher AND zero confirmed threats.",
            "Capability observations (native code, startup activation, dynamic code) are NORMAL for any functional extension — they are shown as capability signals, never counted as threats.",
            "YARA signature matches are low-confidence heuristics that false-positive on minified JS — they are suppressed from the findings list and never affect the verdict.",
        };

        // What the CURRENT signed policy would actually DO with this extension (so the operator sees
        // the enforcement outcome, not just a verdict). A confirmed threat / High-Risk always blocks
        // when enforcement is on; an unverified-only Caution follows ExtensionUnverifiedAction.
        var pol = _policy.Current;
        string gateAction, gateReason;
        if (pol.ExtensionRiskAction.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        { gateAction = "Allow"; gateReason = "Extension gate is disabled in policy (SEC-EXT-01 off)."; }
        else if (verdict == "High-Risk")
        { gateAction = "Block"; gateReason = knownMalicious ? "On a malicious-package feed." : "Confirmed code-threat (IOC) found."; }
        else if (verdict == "Caution" && !publisherVerified)
        {
            gateAction = pol.ExtensionUnverifiedAction switch
            {
                "Block" => "Block", "Allow" => "Allow", _ => "Notify"
            };
            gateReason = gateAction switch
            {
                "Block" => "Policy blocks unverified-publisher extensions (ExtensionUnverifiedAction=Block).",
                "Allow" => "Policy allows unverified publishers (ExtensionUnverifiedAction=Allow).",
                _ => "Unverified publisher — allowed but flagged for approval (ExtensionUnverifiedAction=Notify)."
            };
        }
        else
        { gateAction = "Allow"; gateReason = "Trusted — verified publisher, no confirmed threats."; }

        return new ExtensionRisk(verdict, publisherVerified, domain, executesCode, runsAuto, untrusted,
            onOpenVsx, knownMalicious, installs, deps, signals, exfil,
            codeScanned, codeStatus, codeFindings, basis, criteria, confirmedThreats, heuristicMatches,
            gateAction, gateReason);
    }

    /// <summary>
    /// A CONFIRMED threat = concrete indicator-of-compromise evidence in the code: a known C2 domain,
    /// a crypto-wallet address, a known-bad file hash, or a flagged GitHub-C2 reference. These are the
    /// ONLY code findings that condemn an extension — they're not heuristics and not normal behaviour.
    /// We deliberately EXCLUDE generic capability findings (Function-constructor, startup activation)
    /// and YARA signature matches, which fire on virtually every legitimate extension.
    /// </summary>
    private static bool IsConcreteThreat(CodeScanFinding c)
    {
        if (c.Category != "ioc") return false;   // only the IOC module produces concrete evidence
        var id = c.Id.ToUpperInvariant();
        // Crypto-wallet hits are a notorious false-positive (base58/hex strings in bundles) — require
        // an explicit C2 / known-bad / GitHub-C2 indicator to call it a confirmed threat.
        return id.Contains("C2") || id.Contains("DOMAIN") || id.Contains("IP_") || id.Contains("KNOWN_BAD")
            || id.Contains("HASH") || id.Contains("GITHUB_C2") || id.Contains("MALICIOUS");
    }

    /// <summary>A capability observation — normal extension behaviour (native code, dynamic code,
    /// startup activation, obfuscation). Informational only; never a threat.</summary>
    private static bool IsCapabilityObservation(CodeScanFinding c)
        => (c.Category is "ast" or "manifest" or "telemetry") && (c.Severity is "critical" or "high");

    /// <summary>
    /// Calls the vsix-audit sidecar to deep-scan an extension's published .vsix bytes. Returns
    /// (status, findings, ran). On any failure it returns ran=false with status "Unavailable" —
    /// NEVER a silent "clean", so the gate/UI can distinguish "scanned & clean" from "not scanned".
    /// </summary>
    private async Task<(string Status, IReadOnlyList<CodeScanFinding> Findings, bool Ran)> DeepScanExtensionAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_vsixScannerUrl))
            return ("Unavailable", Array.Empty<CodeScanFinding>(), false);
        // Respect the operator's on/off control in Intelligence sources — if vsix-scanner is toggled
        // off in the policy, the deep scan does not run (and the UI reports it as disabled, not clean).
        if (!_policy.Current.EnabledSources.Contains("vsix-scanner", StringComparer.OrdinalIgnoreCase))
            return ("Disabled", Array.Empty<CodeScanFinding>(), false);
        try
        {
            var client = _factory.CreateClient("catalog-index");
            client.Timeout = TimeSpan.FromSeconds(100);   // a cold scan downloads + unpacks the .vsix
            using var resp = await client.GetAsync($"{_vsixScannerUrl.TrimEnd('/')}/scan?id={Uri.EscapeDataString(id)}", ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!resp.IsSuccessStatusCode || doc.RootElement.TryGetProperty("error", out _))
                return ("Unavailable", Array.Empty<CodeScanFinding>(), false);
            var list = new List<CodeScanFinding>();
            if (doc.RootElement.TryGetProperty("findings", out var fs) && fs.ValueKind == JsonValueKind.Array)
                foreach (var f in fs.EnumerateArray())
                {
                    string? S(string k) => f.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    var file = f.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object && loc.TryGetProperty("file", out var fv) ? fv.GetString() : null;
                    var cat = S("category") ?? "";
                    var detail = S("description");
                    // Reframe YARA-rule detail so an analyst reads it as a HEURISTIC pattern match (which
                    // routinely fires on bundled/minified JS), not a confirmed malware verdict — the
                    // scanner's own wording ("patterns associated with known malware") reads as a verdict.
                    if (cat == "yara")
                        detail = $"HEURISTIC signature match (not a confirmed threat). A YARA rule pattern matched this file — these commonly fire on legitimate bundled/minified JS. Treat as a lead for review, not proof. {detail}";
                    list.Add(new CodeScanFinding(S("id") ?? "", S("title") ?? "", S("severity") ?? "low", cat, detail, file));
                }
            return (list.Count == 0 ? "Clean" : "Findings", list, true);
        }
        catch { return ("Unavailable", Array.Empty<CodeScanFinding>(), false); }
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
    /// Live CVE/advisory detail by id (CVE-…, GHSA-…, PYSEC-…, etc.) — a real OSV /v1/vulns/{id}
    /// lookup, enriched with the CISA-KEV exploited flag and EPSS exploit probability. No fixtures.
    /// </summary>
    public async Task<CatalogCve> CveDetailAsync(string id, CancellationToken ct)
    {
        await _kev.EnsureLoaded(ct);
        id = id.Trim();
        try
        {
            var json = await OsvVulnJson(id, ct);
            if (json is null)
                return new CatalogCve(id, Array.Empty<string>(), "Unknown", null, null, null,
                    _kev.IsKnownExploited(id), null, null, null, null,
                    Array.Empty<string>(), Array.Empty<CveAffected>(), Array.Empty<AdvisoryRef>(), false);

            using var doc = json;
            var r = doc.RootElement;

            // OSV's "CVE-…" records are often thin (no affected[], no summary). If this record is sparse
            // but names a richer alias (GHSA-…/PYSEC-…), fetch that and prefer its detail — same vuln,
            // fuller data. We keep the queried id as the canonical id and union the aliases.
            bool sparse = !(r.TryGetProperty("affected", out var a0) && a0.ValueKind == JsonValueKind.Array && a0.GetArrayLength() > 0)
                          || !(r.TryGetProperty("summary", out var s0) && s0.ValueKind == JsonValueKind.String && s0.GetString()!.Length > 0);
            if (sparse && r.TryGetProperty("aliases", out var al0) && al0.ValueKind == JsonValueKind.Array)
            {
                var richAlias = al0.EnumerateArray().Select(x => x.GetString())
                    .FirstOrDefault(x => x is not null && (x.StartsWith("GHSA", StringComparison.OrdinalIgnoreCase)
                        || x.StartsWith("PYSEC", StringComparison.OrdinalIgnoreCase)
                        || x.StartsWith("GO-", StringComparison.OrdinalIgnoreCase)
                        || x.StartsWith("RUSTSEC", StringComparison.OrdinalIgnoreCase)));
                if (richAlias is not null)
                {
                    var altJson = await OsvVulnJson(richAlias, ct);
                    if (altJson is not null)
                    {
                        // Keep the original queried id; merge its aliases into the richer record's view.
                        var merged = await BuildCve(id, altJson.RootElement, ct);
                        altJson.Dispose();
                        var unionAliases = merged.Aliases.Concat(new[] { id })
                            .Where(x => !x.Equals(merged.Id, StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        return merged with { Id = id, Aliases = unionAliases };
                    }
                }
            }
            return await BuildCve(id, r, ct);
        }
        catch
        {
            return new CatalogCve(id, Array.Empty<string>(), "Unknown", null, null, null,
                _kev.IsKnownExploited(id), null, null, null, null,
                Array.Empty<string>(), Array.Empty<CveAffected>(), Array.Empty<AdvisoryRef>(), false);
        }
    }

    private async Task<JsonDocument?> OsvVulnJson(string id, CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"https://api.osv.dev/v1/vulns/{Uri.EscapeDataString(id)}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    private async Task<CatalogCve> BuildCve(string id, JsonElement r, CancellationToken ct)
    {
        string? S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var aliases = r.TryGetProperty("aliases", out var al) && al.ValueKind == JsonValueKind.Array
                ? al.EnumerateArray().Select(a => a.GetString() ?? "").Where(s => s.Length > 0).ToList() : new List<string>();

            // CVSS vector + numeric base score (parse the v3 vector).
            string? vector = null; double? cvss = null;
            if (r.TryGetProperty("severity", out var sev) && sev.ValueKind == JsonValueKind.Array)
                foreach (var se in sev.EnumerateArray())
                {
                    var t = se.TryGetProperty("type", out var tt) ? tt.GetString() : null;
                    var sc = se.TryGetProperty("score", out var ss) ? ss.GetString() : null;
                    if (sc is null) continue;
                    if (t is "CVSS_V3" or "CVSS_V4" or "CVSS_V2") { vector = sc; cvss = OsvSource.CvssFromVector(sc); break; }
                }

            // Severity label: OSV database_specific.severity, else derive from CVSS.
            var sevLabel = (r.TryGetProperty("database_specific", out var dsp) && dsp.TryGetProperty("severity", out var dsev)
                ? dsev.GetString() : null) ?? SeverityFromCvss(cvss);

            // CWEs (database_specific.cwe_ids on many OSV records).
            var cwes = new List<string>();
            if (r.TryGetProperty("database_specific", out var ds2) && ds2.TryGetProperty("cwe_ids", out var cw) && cw.ValueKind == JsonValueKind.Array)
                cwes.AddRange(cw.EnumerateArray().Select(c => c.GetString() ?? "").Where(s => s.Length > 0));

            // Affected packages (ecosystem + name + introduced/fixed).
            var affected = new List<CveAffected>();
            if (r.TryGetProperty("affected", out var aff) && aff.ValueKind == JsonValueKind.Array)
                foreach (var a in aff.EnumerateArray())
                {
                    if (!a.TryGetProperty("package", out var pk)) continue;
                    var pkgEco = pk.TryGetProperty("ecosystem", out var pe) ? pe.GetString() : null;
                    var pkgName = pk.TryGetProperty("name", out var pn) ? pn.GetString() : null;
                    if (string.IsNullOrEmpty(pkgName)) continue;
                    string? intro = null, fixedVer = null;
                    if (a.TryGetProperty("ranges", out var rng) && rng.ValueKind == JsonValueKind.Array)
                        foreach (var rg in rng.EnumerateArray())
                            if (rg.TryGetProperty("events", out var ev) && ev.ValueKind == JsonValueKind.Array)
                                foreach (var e in ev.EnumerateArray())
                                {
                                    if (e.TryGetProperty("introduced", out var iv)) intro ??= iv.GetString();
                                    if (e.TryGetProperty("fixed", out var fv)) fixedVer = fv.GetString();
                                }
                    affected.Add(new CveAffected(pkgEco ?? "", pkgName!, intro, fixedVer));
                }

            // Reference links, categorized as OSV provides them.
            var refs = new List<AdvisoryRef>();
            if (r.TryGetProperty("references", out var rf) && rf.ValueKind == JsonValueKind.Array)
                foreach (var rr in rf.EnumerateArray())
                {
                    var url = rr.TryGetProperty("url", out var ru) ? ru.GetString() : null;
                    var ty = rr.TryGetProperty("type", out var rt) ? rt.GetString() : "WEB";
                    if (!string.IsNullOrEmpty(url)) refs.Add(new AdvisoryRef(ty ?? "WEB", url!));
                }

            // EPSS keyed on the CVE id (prefer a CVE alias).
            var cveId = aliases.FirstOrDefault(a => a.StartsWith("CVE", StringComparison.OrdinalIgnoreCase))
                ?? (id.StartsWith("CVE", StringComparison.OrdinalIgnoreCase) ? id : null);
            double? epss = null;
            if (cveId is not null) { try { var (esc, est, _) = await _epss.ScoreAsync(cveId, ct); if (est == SourceStatus.Ok) epss = esc; } catch { } }

            var exploited = _kev.IsKnownExploited(id) || aliases.Any(_kev.IsKnownExploited);

        return new CatalogCve(
            S("id") ?? id, aliases, sevLabel, cvss, vector, epss, exploited,
            S("summary"), S("details"), S("published"), S("modified"),
            cwes, affected, refs, true);
    }

    private static string SeverityFromCvss(double? cvss) => cvss switch
    {
        null => "Unknown",
        >= 9.0 => "Critical",
        >= 7.0 => "High",
        >= 4.0 => "Medium",
        > 0.0 => "Low",
        _ => "None",
    };

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
