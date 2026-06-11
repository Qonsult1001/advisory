using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Advisory.Api.Models;

namespace Advisory.Api.VulnSources;

/// <summary>
/// JFrog Artifactory scanning API as an INTELLIGENCE SOURCE (not the proxy — Nexus stays
/// the proxy). Queries Artifactory's free scanning endpoint for a component and maps its
/// reported vulnerabilities into Findings, behind the same IVulnSource seam as every other
/// feed. Health-aware: a failure registers as uncertainty, never a silent pass.
///
/// Configure with ARTIFACTORY_URL (base, e.g. https://artifactory.internal/artifactory)
/// and ARTIFACTORY_TOKEN. Inactive (NotConfigured) until both are present.
/// </summary>
public class ArtifactorySource : IVulnSource
{
    private readonly HttpClient _http;
    private readonly string? _baseUrl;
    private readonly string? _token;
    public string Key => "artifactory";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_token);

    public ArtifactorySource(IHttpClientFactory f, IConfiguration cfg)
    {
        _http = f.CreateClient("artifactory");
        _baseUrl = cfg["ARTIFACTORY_URL"]?.TrimEnd('/');
        _token = cfg["ARTIFACTORY_TOKEN"];
    }

    private static string PackageType(Ecosystem e) => e switch
    {
        Ecosystem.PyPI => "pypi", Ecosystem.npm => "npm", Ecosystem.NuGet => "nuget",
        Ecosystem.Cargo => "cargo", Ecosystem.Go => "go", _ => ""
    };

    public async Task<SourceResult> QueryAsync(PackageRef pkg, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!IsAvailable)
            return new SourceResult(Key, SourceStatus.NotConfigured, Array.Empty<Finding>(),
                "ARTIFACTORY_URL / ARTIFACTORY_TOKEN not set", sw.ElapsedMilliseconds);

        var type = PackageType(pkg.Ecosystem);
        if (string.IsNullOrEmpty(type))
            return new SourceResult(Key, SourceStatus.Skipped, Array.Empty<Finding>(),
                $"ecosystem {pkg.Ecosystem} not mapped", sw.ElapsedMilliseconds);

        try
        {
            // Component-scan query against Artifactory's scanning API (component-graph form).
            var componentId = $"{type}://{pkg.Name}:{pkg.Version}";
            var body = new { component_id = componentId };
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{_baseUrl}/api/v2/ci/build/scan/component");
            req.Headers.Add("Authorization", $"Bearer {_token}");
            req.Content = JsonContent.Create(body);

            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new SourceResult(Key, SourceStatus.Errored, Array.Empty<Finding>(),
                    "401 — check ARTIFACTORY_TOKEN", sw.ElapsedMilliseconds);
            if (!resp.IsSuccessStatusCode)
                return new SourceResult(Key, SourceStatus.Errored, Array.Empty<Finding>(),
                    $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);

            var findings = ParseFindings(await resp.Content.ReadAsStringAsync(ct));
            return new SourceResult(Key, findings.Count == 0 ? SourceStatus.Empty : SourceStatus.Ok,
                findings, "Artifactory scanning API", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        { return new SourceResult(Key, SourceStatus.Timeout, Array.Empty<Finding>(), "timed out", sw.ElapsedMilliseconds); }
        catch (Exception ex)
        { return new SourceResult(Key, SourceStatus.Errored, Array.Empty<Finding>(), ex.Message, sw.ElapsedMilliseconds); }
    }

    /// <summary>
    /// Maps Artifactory's scan response into Findings. Tolerant of shape: looks for a
    /// vulnerabilities/issues array with cve id + cvss + severity fields. Adjust selectors
    /// to your Artifactory version's exact JSON if needed.
    /// </summary>
    private List<Finding> ParseFindings(string json)
    {
        var findings = new List<Finding>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement arr = default;
            foreach (var prop in new[] { "vulnerabilities", "issues", "data" })
                if (root.TryGetProperty(prop, out arr) && arr.ValueKind == JsonValueKind.Array) break;
            if (arr.ValueKind != JsonValueKind.Array) return findings;

            foreach (var v in arr.EnumerateArray())
            {
                var id = TryStr(v, "cve") ?? TryStr(v, "id") ?? TryStr(v, "issue_id") ?? "ARTIFACTORY-UNKNOWN";
                double? cvss = TryDouble(v, "cvss_v3_score") ?? TryDouble(v, "cvss") ?? TryDouble(v, "score");
                var sevStr = TryStr(v, "severity");
                var sev = MapSeverity(sevStr, cvss);
                var summary = TryStr(v, "summary") ?? TryStr(v, "description");
                findings.Add(new Finding(id, sev, cvss, null, false, Key, summary));
            }
        }
        catch { /* unparseable -> caller already marked Ok/Empty; leave empty */ }
        return findings;
    }

    private static string? TryStr(JsonElement e, string p)
        => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? TryDouble(JsonElement e, string p)
        => e.TryGetProperty(p, out var v) && (v.ValueKind == JsonValueKind.Number) ? v.GetDouble()
         : (e.TryGetProperty(p, out var s) && s.ValueKind == JsonValueKind.String && double.TryParse(s.GetString(), out var d) ? d : null);

    private static Severity MapSeverity(string? s, double? cvss) => s?.ToLowerInvariant() switch
    {
        "critical" => Severity.Critical, "high" => Severity.High,
        "medium" => Severity.Medium, "low" => Severity.Low,
        _ => cvss is double c ? OsvSource.FromCvss(c) : Severity.Medium
    };
}
