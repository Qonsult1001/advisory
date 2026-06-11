using System.Text.Json;
using Advisory.Api.Models;

namespace Advisory.Api.Catalog;

/// <summary>
/// Operational risk of one package version — JFrog Xray's documented model
/// (EOL/deprecated, Version Age, Number of New Versions, project Health), computed from
/// free registry data (npm registry time map, PyPI release upload times).
/// Severity algorithm mirrors docs.jfrog.com/security/docs/operational-risk exactly.
/// </summary>
public record OperationalRisk(
    string Severity,            // High / Medium / Low / None / Unknown
    string? RiskReason,         // e.g. "EOL", "Version Age", "Number of new versions"
    bool Eol,                   // deprecated / explicitly end-of-life
    string? EolReason,
    double? VersionAgeMonths,   // months since this version was released
    int? NewerVersions,         // versions released after this one
    int? ReleasesLastYear,      // cadence — healthy >= 2/yr per JFrog model
    string? ReleaseDate,        // this version's release date
    string? LatestVersion,
    string? LatestReleaseDate,
    string? License,            // declared license (for the legal gate)
    // per-factor severities, so the UI can show the full JFrog risk table
    string AgeSeverity, string NewVersionsSeverity, string HealthSeverity,
    string? RepoUrl = null);    // source repository, for the OpenSSF scorecard gate

/// <summary>
/// Computes OperationalRisk for npm + PyPI (the ecosystems with free full release-history APIs;
/// same ecosystems JFrog documents for operational risk: "Currently supported: NPM and Maven").
/// Other ecosystems return null — the gate records the dimension as Skipped, never silently clean.
/// </summary>
public class OpRiskService
{
    private readonly HttpClient _http;
    public OpRiskService(IHttpClientFactory f) => _http = f.CreateClient("oprisk");

    public bool Supports(Ecosystem e) => e is Ecosystem.npm or Ecosystem.PyPI;

    public async Task<OperationalRisk?> AnalyzeAsync(Ecosystem eco, string name, string? version, CancellationToken ct)
    {
        try
        {
            return eco switch
            {
                Ecosystem.npm => await Npm(name, version, ct),
                Ecosystem.PyPI => await PyPi(name, version, ct),
                _ => null,
            };
        }
        catch { return null; }
    }

    private async Task<OperationalRisk?> Npm(string name, string? version, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(
            $"https://registry.npmjs.org/{Uri.EscapeDataString(name)}", ct));
        var root = doc.RootElement;
        var latest = root.TryGetProperty("dist-tags", out var dt) && dt.TryGetProperty("latest", out var l) ? l.GetString() : null;
        var resolved = version ?? latest;
        if (resolved is null) return null;

        // time map: version -> ISO publish date (plus created/modified keys we skip).
        var releases = new List<(string Ver, DateTimeOffset At)>();
        if (root.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.Object)
            foreach (var p in time.EnumerateObject())
                if (p.Name != "created" && p.Name != "modified" && DateTimeOffset.TryParse(p.Value.GetString(), out var at))
                    releases.Add((p.Name, at));

        bool deprecated = false; string? depReason = null; string? license = null;
        if (root.TryGetProperty("versions", out var vs) && vs.ValueKind == JsonValueKind.Object
            && vs.TryGetProperty(resolved, out var vd) && vd.ValueKind == JsonValueKind.Object)
        {
            deprecated = vd.TryGetProperty("deprecated", out var de);
            depReason = deprecated && de.ValueKind == JsonValueKind.String ? de.GetString() : (deprecated ? "version deprecated by maintainer" : null);
            license = vd.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.String ? lic.GetString() : null;
        }
        license ??= root.TryGetProperty("license", out var rl) && rl.ValueKind == JsonValueKind.String ? rl.GetString() : null;
        var repo = root.TryGetProperty("repository", out var rp) && rp.ValueKind == JsonValueKind.Object
            && rp.TryGetProperty("url", out var ru) ? ru.GetString() : null;

        return Compute(resolved, latest, releases, deprecated, depReason, license, repo);
    }

    private async Task<OperationalRisk?> PyPi(string name, string? version, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(
            $"https://pypi.org/pypi/{Uri.EscapeDataString(name)}/json", ct));
        var info = doc.RootElement.GetProperty("info");
        var latest = info.TryGetProperty("version", out var lv) ? lv.GetString() : null;
        var resolved = version ?? latest;
        if (resolved is null) return null;

        var releases = new List<(string Ver, DateTimeOffset At)>();
        if (doc.RootElement.TryGetProperty("releases", out var rel) && rel.ValueKind == JsonValueKind.Object)
            foreach (var p in rel.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0
                    && p.Value[0].TryGetProperty("upload_time_iso_8601", out var ut)
                    && DateTimeOffset.TryParse(ut.GetString(), out var at))
                    releases.Add((p.Name, at));

        bool yanked = false; string? yankReason = null;
        if (doc.RootElement.TryGetProperty("releases", out var rel2) && rel2.TryGetProperty(resolved, out var files)
            && files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0
            && files[0].TryGetProperty("yanked", out var y) && y.ValueKind == JsonValueKind.True)
        { yanked = true; yankReason = files[0].TryGetProperty("yanked_reason", out var yr) ? yr.GetString() ?? "release yanked" : "release yanked"; }

        var license = info.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.String ? lic.GetString() : null;
        string? repo = null;
        if (info.TryGetProperty("project_urls", out var pu) && pu.ValueKind == JsonValueKind.Object)
            foreach (var p in pu.EnumerateObject())
                if (p.Name.Contains("source", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("repo", StringComparison.OrdinalIgnoreCase))
                { repo = p.Value.GetString(); break; }
        return Compute(resolved, latest, releases, yanked, yankReason, license, repo);
    }

    /// <summary>OpenSSF Scorecard overall score for a GitHub repo URL (null when unpublished).</summary>
    public async Task<double?> ScorecardScoreAsync(string? repoUrl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repoUrl) || !repoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)) return null;
        var i = repoUrl.IndexOf("github.com", StringComparison.OrdinalIgnoreCase);
        var parts = repoUrl[(i + "github.com".Length)..].TrimStart('/', ':').Replace(".git", "")
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(
                $"https://api.securityscorecards.dev/projects/github.com/{parts[0]}/{parts[1]}", ct));
            return doc.RootElement.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number
                ? sc.GetDouble() : null;
        }
        catch { return null; }
    }

    /// <summary>JFrog's documented severity model, factor by factor, then the combine table.</summary>
    private static OperationalRisk Compute(string resolved, string? latest,
        List<(string Ver, DateTimeOffset At)> releases, bool eol, string? eolReason, string? license,
        string? repoUrl = null)
    {
        var now = DateTimeOffset.UtcNow;
        var thisRel = releases.FirstOrDefault(r => r.Ver == resolved);
        var hasDate = thisRel.Ver is not null;
        double? ageMonths = hasDate ? (now - thisRel.At).TotalDays / 30.44 : null;
        int? newer = hasDate ? releases.Count(r => r.At > thisRel.At) : null;
        var latestRel = releases.OrderByDescending(r => r.At).FirstOrDefault();
        int releasesLastYear = releases.Count(r => r.At > now.AddYears(-1));

        // Factor severities (docs: age = months/10; new versions = count/2; health = cadence/yr).
        string ageSev = ageMonths is null ? "Unknown" : (ageMonths / 10) switch
        {
            >= 4 => "High",
            > 2 => "Medium",
            > 1 => "Low",
            _ => "None",
        };
        string newSev = newer is null ? "Unknown" : (newer / 2.0) switch
        {
            >= 6 => "High",
            >= 4 => "Medium",
            >= 2 => "Low",
            _ => "None",
        };
        // Health: unhealthy <= 1 release/yr; no data presumed healthy.
        string healthSev = releases.Count == 0 ? "None" : releasesLastYear <= 1 ? "High" : "None";

        // Combine table (docs rows 1–9): EOL trumps; then health; then worst of age/new-versions.
        string sev; string? reason;
        if (eol) { sev = "High"; reason = "EOL"; }
        else if (healthSev == "High") { sev = "High"; reason = "Health"; }
        else
        {
            int Rank(string s) => s switch { "High" => 3, "Medium" => 2, "Low" => 1, _ => 0 };
            var worst = Rank(newSev) >= Rank(ageSev) ? newSev : ageSev;
            sev = worst == "Unknown" ? "Unknown" : worst;
            reason = worst is "None" or "Unknown" ? null
                : Rank(newSev) >= Rank(ageSev) ? "Number of new versions" : "Version age";
        }

        return new OperationalRisk(sev, reason, eol, eolReason,
            ageMonths is double m ? Math.Round(m, 1) : null, newer, releasesLastYear,
            hasDate ? thisRel.At.ToString("yyyy-MM-dd") : null,
            latest, latestRel.Ver is not null ? latestRel.At.ToString("yyyy-MM-dd") : null,
            license, ageSev, newSev, healthSev, repoUrl);
    }

    /// <summary>Days since the resolved version was released — drives the immature-version curation gate.</summary>
    public static double? VersionAgeDays(OperationalRisk r)
        => r.ReleaseDate is not null && DateTimeOffset.TryParse(r.ReleaseDate, out var at)
            ? (DateTimeOffset.UtcNow - at).TotalDays : null;
}
