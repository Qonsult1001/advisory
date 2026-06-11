using System.Diagnostics;
using System.Text.Json;
using Advisory.Api.Models;

namespace Advisory.Api.VulnSources;

/// <summary>One CISA KEV catalogue entry (the browsable detail behind "known-exploited").</summary>
public record KevEntry(string CveId, string VendorProject, string Product, string Name,
    string DateAdded, string DueDate, bool KnownRansomware, string ShortDescription);

/// <summary>CISA KEV — free known-exploited catalogue. Health-aware load. Keeps full entries so the
/// catalogue is browsable, not just a membership set.</summary>
public class KevSource : IVulnSource
{
    private readonly HttpClient _http;
    private HashSet<string> _kev = new();
    private List<KevEntry> _entries = new();
    private DateTimeOffset _loaded = DateTimeOffset.MinValue;
    private bool _everLoaded = false;
    public string Key => "kev";
    public bool IsAvailable => true;
    public KevSource(IHttpClientFactory f) => _http = f.CreateClient("kev");

    public async Task<SourceStatus> EnsureLoaded(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _loaded < TimeSpan.FromHours(24) && _everLoaded)
            return SourceStatus.Ok;
        try
        {
            var url = "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<KevEntry>();
            foreach (var v in doc.RootElement.GetProperty("vulnerabilities").EnumerateArray())
            {
                string S(string p) => v.TryGetProperty(p, out var e) ? (e.GetString() ?? "") : "";
                var id = S("cveID");
                set.Add(id);
                list.Add(new KevEntry(id, S("vendorProject"), S("product"), S("vulnerabilityName"),
                    S("dateAdded"), S("dueDate"),
                    S("knownRansomwareCampaignUse").Equals("Known", StringComparison.OrdinalIgnoreCase),
                    S("shortDescription")));
            }
            _kev = set; _entries = list; _loaded = DateTimeOffset.UtcNow; _everLoaded = true;
            return SourceStatus.Ok;
        }
        catch { return _everLoaded ? SourceStatus.Ok : SourceStatus.Errored; } // stale-but-usable vs never-loaded
    }

    public Task<SourceResult> QueryAsync(PackageRef pkg, CancellationToken ct)
        => Task.FromResult(new SourceResult(Key, SourceStatus.Ok, Array.Empty<Finding>()));

    public bool IsKnownExploited(string cveId) => _kev.Contains(cveId);

    /// <summary>Total entries in the loaded catalogue (0 if never loaded).</summary>
    public int Count => _entries.Count;
    public DateTimeOffset LoadedAt => _loaded;

    /// <summary>Browse the catalogue with an optional text query over CVE/vendor/product/name.</summary>
    public IReadOnlyList<KevEntry> Browse(string? query, int limit)
    {
        IEnumerable<KevEntry> q = _entries;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var t = query.Trim();
            q = q.Where(e =>
                e.CveId.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                e.VendorProject.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                e.Product.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        return q.OrderByDescending(e => e.DateAdded).Take(limit <= 0 ? 100 : limit).ToList();
    }
}

/// <summary>EPSS — free exploit-probability score. Health-aware.</summary>
public class EpssSource : IVulnSource
{
    private readonly HttpClient _http;
    public string Key => "epss";
    public bool IsAvailable => true;
    public EpssSource(IHttpClientFactory f) => _http = f.CreateClient("epss");

    public Task<SourceResult> QueryAsync(PackageRef pkg, CancellationToken ct)
        => Task.FromResult(new SourceResult(Key, SourceStatus.Ok, Array.Empty<Finding>()));

    public async Task<(double? score, SourceStatus status, string? detail)> ScoreAsync(string cveId, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(
                await _http.GetStringAsync($"https://api.first.org/data/v1/epss?cve={cveId}", ct));
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0) return (null, SourceStatus.Empty, "no EPSS record");
            return (double.Parse(data[0].GetProperty("epss").GetString()!), SourceStatus.Ok, null);
        }
        catch (OperationCanceledException) { return (null, SourceStatus.Timeout, "EPSS timed out"); }
        catch (Exception ex) { return (null, SourceStatus.Errored, ex.Message); }
    }
}
