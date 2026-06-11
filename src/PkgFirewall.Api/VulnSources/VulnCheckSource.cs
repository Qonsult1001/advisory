using System.Diagnostics;
using System.Text.Json;
using PkgFirewall.Api.Models;

namespace PkgFirewall.Api.VulnSources;

/// <summary>
/// VulnCheck — PAID pre-NVD / zero-day intelligence. Now wired to VulnCheck's index API.
/// Queries the NVD2 index filtered by package; maps results to Findings with KnownExploited
/// set from VulnCheck's exploit intelligence. Inactive until VULNCHECK_API_KEY is set.
/// </summary>
public class VulnCheckSource : IVulnSource
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    public string Key => "vulncheck";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    public VulnCheckSource(IHttpClientFactory f, IConfiguration cfg)
    {
        _http = f.CreateClient("vulncheck");
        _apiKey = cfg["VULNCHECK_API_KEY"];
    }

    public async Task<SourceResult> QueryAsync(PackageRef pkg, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!IsAvailable)
            return new SourceResult(Key, SourceStatus.NotConfigured, Array.Empty<Finding>(),
                "VULNCHECK_API_KEY not set — licensed feed inactive", sw.ElapsedMilliseconds);
        try
        {
            // VulnCheck purl-based lookup (vulncheck-nvd2 index, purl filter).
            var type = pkg.Ecosystem switch
            {
                Ecosystem.PyPI => "pypi", Ecosystem.npm => "npm", Ecosystem.NuGet => "nuget",
                Ecosystem.Cargo => "cargo", Ecosystem.Go => "golang", _ => ""
            };
            if (type == "")
                return new SourceResult(Key, SourceStatus.Skipped, Array.Empty<Finding>(), "ecosystem not mapped", sw.ElapsedMilliseconds);

            var purl = Uri.EscapeDataString($"pkg:{type}/{pkg.Name}@{pkg.Version}");
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.vulncheck.com/v3/purl?purl={purl}");
            req.Headers.Add("Authorization", $"Bearer {_apiKey}");

            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new SourceResult(Key, SourceStatus.Errored, Array.Empty<Finding>(), "401 — check VULNCHECK_API_KEY", sw.ElapsedMilliseconds);
            if (!resp.IsSuccessStatusCode)
                return new SourceResult(Key, SourceStatus.Errored, Array.Empty<Finding>(), $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);

            var findings = Parse(await resp.Content.ReadAsStringAsync(ct));
            return new SourceResult(Key, findings.Count == 0 ? SourceStatus.Empty : SourceStatus.Ok,
                findings, "VulnCheck purl index", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        { return new SourceResult(Key, SourceStatus.Timeout, Array.Empty<Finding>(), "timed out", sw.ElapsedMilliseconds); }
        catch (Exception ex)
        { return new SourceResult(Key, SourceStatus.Errored, Array.Empty<Finding>(), ex.Message, sw.ElapsedMilliseconds); }
    }

    private List<Finding> Parse(string json)
    {
        var findings = new List<Finding>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return findings;
            foreach (var v in data.EnumerateArray())
            {
                var id = (v.TryGetProperty("cve", out var c) ? c.GetString() : null)
                       ?? (v.TryGetProperty("id", out var i) ? i.GetString() : null) ?? "VULNCHECK-UNKNOWN";
                bool exploited = v.TryGetProperty("exploitation", out var ex) &&
                                 ex.ValueKind != JsonValueKind.Null;
                double? cvss = v.TryGetProperty("cvss_base_score", out var cs) && cs.ValueKind == JsonValueKind.Number
                    ? cs.GetDouble() : null;
                // VulnCheck's edge is early/exploited intel — mark KnownExploited so KEV-style policy fires.
                findings.Add(new Finding(id, cvss is double d ? OsvSource.FromCvss(d) : Severity.High,
                    cvss, null, exploited, Key, "VulnCheck early-warning entry"));
            }
        }
        catch { }
        return findings;
    }
}
