using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Advisory.Api.Models;

namespace Advisory.Api.VulnSources;

/// <summary>One VulnCheck-KEV record's exploited-in-the-wild intel for a CVE.</summary>
public record VcKevHit(
    string Cve,
    bool Exploited,
    bool Ransomware,
    int ReportedExploitationCount,
    int ExploitRefCount,
    string? VulnerabilityName,
    string? DateAdded,
    IReadOnlyList<string> Cwes);

/// <summary>
/// VulnCheck exploited-in-the-wild intelligence via the FREE community-tier `vulncheck-kev` index.
///
/// The paid `/v3/purl` endpoint returns HTTP 402 for community keys, so this source does NOT do a
/// per-package CVE lookup (there is no CVE to query from a bare package coordinate anyway). Instead it
/// exposes <see cref="LookupCveAsync"/> — a per-CVE query against
/// `GET /v3/index/vulncheck-kev?cve={id}` — which the gate and Catalog call during ENRICHMENT to mark
/// findings exploited and surface VulnCheck's richer intel (ransomware use, reported-exploitation count,
/// exploit references). VulnCheck-KEV is a superset of CISA-KEV. Inactive until VULNCHECK_API_KEY is set.
/// </summary>
public class VulnCheckSource : IVulnSource
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    // 24 h per-CVE cache so we stay well under the 1000 req/min community limit. null value = looked up,
    // not in KEV (cache the negative too).
    private readonly ConcurrentDictionary<string, (VcKevHit? Hit, DateTimeOffset At)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public string Key => "vulncheck";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    public VulnCheckSource(IHttpClientFactory f, IConfiguration cfg)
    {
        _http = f.CreateClient("vulncheck");
        _apiKey = cfg["VULNCHECK_API_KEY"];
    }

    /// <summary>
    /// The per-package contract. VulnCheck-KEV is keyed by CVE, not by package coordinate, and the
    /// community tier cannot call the paid package endpoint — so per-package this source is a no-op
    /// (Skipped, never a silent clean). Its value is delivered through <see cref="LookupCveAsync"/>
    /// during the gate's enrichment pass, after a CVE has been discovered.
    /// </summary>
    public Task<SourceResult> QueryAsync(PackageRef pkg, CancellationToken ct)
    {
        if (!IsAvailable)
            return Task.FromResult(new SourceResult(Key, SourceStatus.NotConfigured, Array.Empty<Finding>(),
                "VULNCHECK_API_KEY not set — exploited-intel enrichment inactive", 0));
        return Task.FromResult(new SourceResult(Key, SourceStatus.Skipped, Array.Empty<Finding>(),
            "VulnCheck-KEV is queried per-CVE during enrichment, not per-package", 0));
    }

    /// <summary>
    /// Live per-CVE lookup against the free `vulncheck-kev` index. Returns null when the CVE is not in
    /// VulnCheck-KEV (i.e. not known-exploited), or when the key is missing/insufficient. Cached 24 h.
    /// </summary>
    public async Task<VcKevHit?> LookupCveAsync(string cve, CancellationToken ct)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(cve) || !cve.StartsWith("CVE", StringComparison.OrdinalIgnoreCase))
            return null;
        if (_cache.TryGetValue(cve, out var cached) && DateTimeOffset.UtcNow - cached.At < Ttl)
            return cached.Hit;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.vulncheck.com/v3/index/vulncheck-kev?cve={Uri.EscapeDataString(cve)}");
            req.Headers.Add("Authorization", $"Bearer {_apiKey}");
            using var resp = await _http.SendAsync(req, ct);
            // 402/403 = tier lacks the index, 401 = bad key, 429 = rate-limited. None should poison the
            // cache as a permanent negative — return null and let the next call retry.
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            VcKevHit? hit = null;
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                hit = ParseRecord(data[0], cve);
            _cache[cve] = (hit, DateTimeOffset.UtcNow);
            return hit;
        }
        catch { return null; }
    }

    private static VcKevHit ParseRecord(JsonElement r, string queriedCve)
    {
        string? S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int ArrLen(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : 0;
        var ransom = (S("knownRansomwareCampaignUse") ?? "").Equals("Known", StringComparison.OrdinalIgnoreCase);
        var cwes = new List<string>();
        if (r.TryGetProperty("cwes", out var cw) && cw.ValueKind == JsonValueKind.Array)
            foreach (var c in cw.EnumerateArray())
                if (c.ValueKind == JsonValueKind.String && c.GetString() is { } s) cwes.Add(s);
        // `cve` is an array of ids in vulncheck-kev; prefer the queried id.
        return new VcKevHit(
            queriedCve, true, ransom,
            ArrLen("vulncheck_reported_exploitation"),
            ArrLen("vulncheck_xdb"),
            S("vulnerabilityName"),
            S("date_added"),
            cwes);
    }
}
