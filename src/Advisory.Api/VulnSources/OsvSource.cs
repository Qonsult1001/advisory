using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Advisory.Api.Models;

namespace Advisory.Api.VulnSources;

/// <summary>OSV.dev — free, multi-ecosystem. Core CVE matcher. Health-aware.</summary>
public class OsvSource : IVulnSource
{
    private readonly HttpClient _http;
    public string Key => "osv";
    public bool IsAvailable => true;
    public OsvSource(IHttpClientFactory f) => _http = f.CreateClient("osv");

    private static string EcosystemName(Ecosystem e) => e switch
    {
        Ecosystem.PyPI => "PyPI", Ecosystem.npm => "npm", Ecosystem.NuGet => "NuGet",
        Ecosystem.Cargo => "crates.io", Ecosystem.Go => "Go",
        // Full JFrog-Catalog parity — OSV's exact ecosystem identifiers:
        Ecosystem.Maven => "Maven", Ecosystem.RubyGems => "RubyGems",
        Ecosystem.Composer => "Packagist", Ecosystem.Conan => "ConanCenter",
        Ecosystem.CRAN => "CRAN", Ecosystem.DartPub => "Pub",
        // Alpine/Debian/Ubuntu need a release suffix (Debian:12) supplied via OsvEcosystem;
        // bare distro names aren't OSV ecosystems, so they're handled by the per-image scan.
        _ => ""
    };

    public async Task<SourceResult> QueryAsync(PackageRef pkg, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        // A Docker-image package carries its real OSV ecosystem (Debian:12 / Alpine:v3.18 / Go / npm…)
        // in OsvEcosystem — use it directly so OS + language packages match real CVEs.
        var eco = !string.IsNullOrEmpty(pkg.OsvEcosystem) ? pkg.OsvEcosystem! : EcosystemName(pkg.Ecosystem);
        if (string.IsNullOrEmpty(eco))
            return new SourceResult(Key, SourceStatus.Skipped, Array.Empty<Finding>(),
                $"ecosystem {pkg.Ecosystem} not covered by OSV", sw.ElapsedMilliseconds);

        try
        {
            var body = new { version = pkg.Version, package = new { name = pkg.Name, ecosystem = eco } };
            using var resp = await _http.PostAsJsonAsync("https://api.osv.dev/v1/query", body, ct);
            if (!resp.IsSuccessStatusCode)
                return new SourceResult(Key, SourceStatus.Errored, Array.Empty<Finding>(),
                    $"HTTP {(int)resp.StatusCode} from OSV", sw.ElapsedMilliseconds);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("vulns", out var vulns))
                return new SourceResult(Key, SourceStatus.Empty, Array.Empty<Finding>(),
                    "no vulnerabilities recorded", sw.ElapsedMilliseconds);

            var findings = new List<Finding>();
            foreach (var v in vulns.EnumerateArray())
            {
                var id = v.GetProperty("id").GetString() ?? "UNKNOWN";
                var summary = v.TryGetProperty("summary", out var s) ? s.GetString() : null;
                var (sev, cvss, vector) = ExtractCvss(v);
                var fixedVersion = ExtractFixedVersion(v, eco, pkg.Name, pkg.Version);
                findings.Add(new Finding(id, sev, cvss, null, false, Key, summary, fixedVersion,
                    Aliases: ExtractStringArray(v, "aliases"),
                    CvssVector: vector,
                    Cwes: ExtractCwes(v),
                    PublishedAt: ExtractPublished(v),
                    References: ExtractReferences(v)));
            }
            return new SourceResult(Key, findings.Count == 0 ? SourceStatus.Empty : SourceStatus.Ok,
                findings, null, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        { return new SourceResult(Key, SourceStatus.Timeout, Array.Empty<Finding>(), "request cancelled/timed out", sw.ElapsedMilliseconds); }
        catch (Exception ex)
        { return new SourceResult(Key, SourceStatus.Errored, Array.Empty<Finding>(), ex.Message, sw.ElapsedMilliseconds); }
    }

    /// <summary>
    /// Extracts (severity, numeric CVSS, vector string). OSV's severity[].score is the CVSS VECTOR
    /// (e.g. "CVSS:3.1/AV:N/..."), not a number — so we parse the base score out of the vector. Prefers
    /// the highest CVSS version present. Falls back to database_specific.severity label when no vector.
    /// </summary>
    private static (Severity, double?, string?) ExtractCvss(JsonElement v)
    {
        string? vector = null;
        double? score = null;
        if (v.TryGetProperty("severity", out var sevArr) && sevArr.ValueKind == JsonValueKind.Array)
            foreach (var item in sevArr.EnumerateArray())
                if (item.TryGetProperty("score", out var sc) && sc.GetString() is { } scStr)
                {
                    // score may be a numeric string OR a CVSS vector string.
                    if (double.TryParse(scStr, out var num)) { score = num; }
                    else { vector = scStr; var b = CvssBaseScore(scStr); if (b is not null) score = b; }
                }
        if (score is double n) return (FromCvss(n), n, vector);

        // No numeric score — fall back to the textual severity label OSV often carries.
        if (v.TryGetProperty("database_specific", out var ds) && ds.TryGetProperty("severity", out var lbl))
            return (lbl.GetString()?.ToUpperInvariant() switch
            {
                "CRITICAL" => Severity.Critical, "HIGH" => Severity.High,
                "MODERATE" or "MEDIUM" => Severity.Medium, "LOW" => Severity.Low, _ => Severity.Medium
            }, null, vector);
        return (Severity.Medium, null, vector);
    }

    /// <summary>Public alias — compute the CVSS base score from a vector string (OSV severity[].score
    /// carries the vector, not a number). Returns null for vectors we can't score.</summary>
    public static double? CvssFromVector(string vector) => CvssBaseScore(vector);

    /// <summary>Computes the CVSS v3.0/3.1 base score from a vector string per the official spec.
    /// OSV's severity[].score is the vector (e.g. "CVSS:3.1/AV:N/AC:L/..."), so we do the real math
    /// rather than leaving the score blank. CVSS v2/v4 vectors return null (label-based rating used).</summary>
    private static double? CvssBaseScore(string vector)
    {
        if (string.IsNullOrWhiteSpace(vector)) return null;
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in vector.Split('/'))
        {
            var kv = part.Split(':', 2);
            if (kv.Length == 2) m[kv[0]] = kv[1];
        }
        // Only CVSS:3.0 / 3.1 are scored here.
        if (!m.TryGetValue("CVSS", out var ver) || (ver != "3.0" && ver != "3.1")) return null;
        if (!m.ContainsKey("AV") || !m.ContainsKey("AC") || !m.ContainsKey("PR") ||
            !m.ContainsKey("UI") || !m.ContainsKey("S") || !m.ContainsKey("C") ||
            !m.ContainsKey("I") || !m.ContainsKey("A")) return null;

        double av = m["AV"].ToUpperInvariant() switch { "N" => 0.85, "A" => 0.62, "L" => 0.55, "P" => 0.20, _ => 0.85 };
        double ac = m["AC"].ToUpperInvariant() == "L" ? 0.77 : 0.44;
        bool scopeChanged = m["S"].ToUpperInvariant() == "C";
        double pr = m["PR"].ToUpperInvariant() switch
        {
            "N" => 0.85,
            "L" => scopeChanged ? 0.68 : 0.62,
            "H" => scopeChanged ? 0.50 : 0.27,
            _ => 0.85
        };
        double ui = m["UI"].ToUpperInvariant() == "N" ? 0.85 : 0.62;
        double Imp(string k) => m[k].ToUpperInvariant() switch { "H" => 0.56, "L" => 0.22, _ => 0.0 };
        double iscBase = 1 - ((1 - Imp("C")) * (1 - Imp("I")) * (1 - Imp("A")));

        double impact = scopeChanged
            ? 7.52 * (iscBase - 0.029) - 3.25 * Math.Pow(iscBase - 0.02, 15)
            : 6.42 * iscBase;
        double exploitability = 8.22 * av * ac * pr * ui;

        if (impact <= 0) return 0.0;
        double raw = scopeChanged
            ? Math.Min(1.08 * (impact + exploitability), 10.0)
            : Math.Min(impact + exploitability, 10.0);
        // Round up to one decimal (CVSS "roundup").
        return Math.Ceiling(raw * 10) / 10.0;
    }

    private static IReadOnlyList<string>? ExtractStringArray(JsonElement v, string prop)
    {
        if (!v.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        var list = arr.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null).Cast<string>().ToList();
        return list.Count == 0 ? null : list;
    }

    private static IReadOnlyList<string>? ExtractCwes(JsonElement v)
    {
        if (v.TryGetProperty("database_specific", out var ds) &&
            ds.TryGetProperty("cwe_ids", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var list = arr.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null).Cast<string>().ToList();
            return list.Count == 0 ? null : list;
        }
        return null;
    }

    private static string? ExtractPublished(JsonElement v)
    {
        if (v.TryGetProperty("database_specific", out var ds) && ds.TryGetProperty("nvd_published_at", out var d))
            return d.GetString();
        return v.TryGetProperty("published", out var p) ? p.GetString() : null;
    }

    /// <summary>Categorizes OSV reference links into the buckets JFrog shows (Advisory/Exploit/Patch/…).</summary>
    private static IReadOnlyList<AdvisoryRef>? ExtractReferences(JsonElement v)
    {
        if (!v.TryGetProperty("references", out var refs) || refs.ValueKind != JsonValueKind.Array) return null;
        var list = new List<AdvisoryRef>();
        foreach (var r in refs.EnumerateArray())
        {
            var url = r.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(url)) continue;
            var osvType = r.TryGetProperty("type", out var t) ? t.GetString() : "WEB";
            list.Add(new AdvisoryRef(MapRefType(osvType, url), url));
        }
        return list.Count == 0 ? null : list;
    }

    private static string MapRefType(string? osvType, string url)
    {
        // OSV types: ADVISORY, ARTICLE, REPORT, FIX, PACKAGE, EVIDENCE, WEB. Refine with URL hints.
        var lower = url.ToLowerInvariant();
        if (lower.Contains("exploit-db") || lower.Contains("/poc") || lower.Contains("gist.github")) return "Exploit";
        return osvType?.ToUpperInvariant() switch
        {
            "ADVISORY" => "Advisory",
            "FIX" => "Patch",
            "REPORT" => "Report",
            "PACKAGE" => "Package",
            "ARTICLE" => "Advisory",
            _ => lower.Contains("/commit/") || lower.Contains("/pull/") ? "Patch" : "Web"
        };
    }

    public static Severity FromCvss(double c) => c switch
    {
        >= 9.0 => Severity.Critical, >= 7.0 => Severity.High,
        >= 4.0 => Severity.Medium, > 0.0 => Severity.Low, _ => Severity.None
    };

    /// <summary>
    /// Pulls the remediation target from an OSV advisory: the "fixed" version a consumer should
    /// upgrade to. OSV records this in affected[].ranges[].events[] as {"fixed":"x"}. We match the
    /// affected entry for THIS package/ecosystem, gather all fixed versions, and return the lowest
    /// one that is greater than the installed version (the nearest safe upgrade). Returns null if
    /// the advisory lists no fixed version (e.g. unpatched, or withdrawn-only). Best-effort and
    /// version-scheme tolerant: comparison falls back to ordinal when semver parse fails.
    /// </summary>
    private static string? ExtractFixedVersion(JsonElement v, string eco, string name, string current)
    {
        if (!v.TryGetProperty("affected", out var affected) || affected.ValueKind != JsonValueKind.Array)
            return null;

        var fixes = new List<string>();
        foreach (var aff in affected.EnumerateArray())
        {
            // Scope to the right package when the advisory covers several.
            if (aff.TryGetProperty("package", out var pkgEl))
            {
                var an = pkgEl.TryGetProperty("name", out var n) ? n.GetString() : null;
                var ae = pkgEl.TryGetProperty("ecosystem", out var e) ? e.GetString() : null;
                if (an is not null && !string.Equals(an, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (ae is not null && !string.Equals(ae, eco, StringComparison.OrdinalIgnoreCase)) continue;
            }
            if (!aff.TryGetProperty("ranges", out var ranges) || ranges.ValueKind != JsonValueKind.Array) continue;
            foreach (var rng in ranges.EnumerateArray())
            {
                if (!rng.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array) continue;
                foreach (var ev in events.EnumerateArray())
                    if (ev.TryGetProperty("fixed", out var fx) && fx.GetString() is { Length: > 0 } fv)
                        fixes.Add(fv);
            }
        }
        if (fixes.Count == 0) return null;

        // Prefer the lowest fixed version strictly greater than the installed one; else the lowest fix.
        var greater = fixes.Where(f => CompareVersions(f, current) > 0).ToList();
        var pool = greater.Count > 0 ? greater : fixes;
        return pool.OrderBy(f => f, Comparer<string>.Create(CompareVersions)).First();
    }

    /// <summary>Best-effort version compare: numeric dotted segments, ordinal fallback.</summary>
    private static int CompareVersions(string a, string b)
    {
        static int[] Parts(string v) => v.Split('.', '-', '+')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0).ToArray();
        var pa = Parts(a); var pb = Parts(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return string.CompareOrdinal(a, b);
    }
}
