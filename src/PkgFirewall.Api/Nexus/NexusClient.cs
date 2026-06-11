using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PkgFirewall.Api.Models;

namespace PkgFirewall.Api.Nexus;

/// <summary>An artifact discovered in the quarantine repo awaiting a decision.</summary>
public record NexusComponent(string ComponentId, Ecosystem Ecosystem, string Name, string Version,
    string? FileName, string? Sha256, string DownloadUrl);

/// <summary>A Nexus repository, indexed for the Xray-style Scans List.</summary>
public record NexusRepo(string Name, string Format, string Type, string Url,
    int IndexedArtifacts, string? LatestArtifact, string? IndexedOn);

/// <summary>
/// Talks to Nexus's REST API. Nexus stays the proxy/store; this client implements the
/// TWO-REPO PROMOTION model that gives quarantine a physical location:
///   - a "quarantine" proxy repo devs cannot read (packages land here first)
///   - an "approved" hosted repo devs pull from
/// The bridge lists quarantine, asks the gate, and on Allow uploads the bytes to approved
/// (promote); on Block/Quarantine it leaves them in quarantine (held) and optionally deletes.
/// </summary>
public interface INexusClient
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<NexusRepo>> ListRepositoriesAsync(CancellationToken ct);
    Task<IReadOnlyList<NexusComponent>> ListComponentsAsync(string repo, CancellationToken ct);
    Task<IReadOnlyList<NexusComponent>> ListQuarantineAsync(CancellationToken ct);
    Task<byte[]> DownloadAsync(string url, CancellationToken ct);
    Task PromoteAsync(NexusComponent c, byte[] bytes, CancellationToken ct);
    Task HoldAsync(NexusComponent c, string reason, CancellationToken ct);
}

public class NexusClient : INexusClient
{
    private readonly HttpClient _http;
    private readonly ILogger<NexusClient> _log;
    private readonly string? _baseUrl;
    private readonly string _quarantineSuffix;
    private readonly string _approvedSuffix;
    private readonly bool _deleteOnHold;

    // Per-ecosystem repo names follow the convention "<eco>-<suffix>" created by nexus-setup.sh.
    private static string EcoPrefix(Ecosystem e) => e switch
    {
        Ecosystem.PyPI => "pypi", Ecosystem.npm => "npm", Ecosystem.NuGet => "nuget",
        Ecosystem.Cargo => "cargo", Ecosystem.Go => "go", _ => "pypi"
    };
    private string QuarantineRepo(Ecosystem e) => $"{EcoPrefix(e)}-{_quarantineSuffix}";
    private string ApprovedRepo(Ecosystem e) => $"{EcoPrefix(e)}-{_approvedSuffix}";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl);

    public NexusClient(IHttpClientFactory f, IConfiguration cfg, ILogger<NexusClient> log)
    {
        _http = f.CreateClient("nexus");
        _log = log;
        _baseUrl = cfg["NEXUS_URL"]?.TrimEnd('/');               // e.g. http://nexus:8081
        _quarantineSuffix = cfg["NEXUS_QUARANTINE_SUFFIX"] ?? "quarantine";
        _approvedSuffix = cfg["NEXUS_APPROVED_SUFFIX"] ?? "approved";
        _deleteOnHold = cfg.GetValue("NEXUS_DELETE_ON_HOLD", false);

        var user = cfg["NEXUS_USER"]; var pass = cfg["NEXUS_PASS"];
        if (!string.IsNullOrWhiteSpace(user))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));
    }

    public async Task<IReadOnlyList<NexusComponent>> ListQuarantineAsync(CancellationToken ct)
    {
        var items = new List<NexusComponent>();
        if (!IsConfigured) return items;
        foreach (var eco in new[] { Ecosystem.PyPI, Ecosystem.npm, Ecosystem.NuGet, Ecosystem.Cargo, Ecosystem.Go })
            await ListRepoAsync(QuarantineRepo(eco), items, ct);
        return items;
    }

    /// <summary>All Nexus repositories with an indexed-artifact count + latest artifact — the Scans List.</summary>
    public async Task<IReadOnlyList<NexusRepo>> ListRepositoriesAsync(CancellationToken ct)
    {
        var repos = new List<NexusRepo>();
        if (!IsConfigured) return repos;
        try
        {
            using var resp = await _http.GetAsync($"{_baseUrl}/service/rest/v1/repositories", ct);
            if (!resp.IsSuccessStatusCode) return repos;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var format = r.TryGetProperty("format", out var f) ? f.GetString() ?? "" : "";
                var type = r.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                var url = r.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                // Index: count components + capture the most recent one.
                var comps = new List<NexusComponent>();
                await ListRepoAsync(name, comps, ct);
                var latest = comps.LastOrDefault();
                repos.Add(new NexusRepo(name, format, type, url, comps.Count,
                    latest is null ? null : $"{latest.Name}{(string.IsNullOrEmpty(latest.Version) ? "" : "/" + latest.Version)}",
                    comps.Count > 0 ? DateTimeOffset.UtcNow.ToString("dd MMM yyyy HH:mm 'UTC'") : null));
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Nexus repo list failed"); }
        return repos;
    }

    public async Task<IReadOnlyList<NexusComponent>> ListComponentsAsync(string repo, CancellationToken ct)
    {
        var items = new List<NexusComponent>();
        if (!IsConfigured) return items;
        await ListRepoAsync(repo, items, ct);
        return items;
    }

    private async Task ListRepoAsync(string repo, List<NexusComponent> items, CancellationToken ct)
    {
        string? token = null;
        do
        {
            var url = $"{_baseUrl}/service/rest/v1/components?repository={repo}" +
                      (token is null ? "" : $"&continuationToken={token}");
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) { _log.LogWarning("Nexus list {Status}", (int)resp.StatusCode); break; }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var comp in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var name = comp.GetProperty("name").GetString() ?? "";
                var version = comp.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                var format = comp.TryGetProperty("format", out var fmt) ? fmt.GetString() : "pypi";
                var assets = comp.GetProperty("assets");
                var first = assets.GetArrayLength() > 0 ? assets[0] : default;
                var dl = first.ValueKind != JsonValueKind.Undefined && first.TryGetProperty("downloadUrl", out var d) ? d.GetString() : null;
                var sha = first.ValueKind != JsonValueKind.Undefined && first.TryGetProperty("checksum", out var cs) && cs.TryGetProperty("sha256", out var sh) ? sh.GetString() : null;
                var fileName = first.ValueKind != JsonValueKind.Undefined && first.TryGetProperty("path", out var p) ? Path.GetFileName(p.GetString()) : null;
                items.Add(new NexusComponent(comp.GetProperty("id").GetString() ?? "",
                    MapEco(format), name, version, fileName, sha, dl ?? ""));
            }
            token = doc.RootElement.TryGetProperty("continuationToken", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
        } while (token is not null && !ct.IsCancellationRequested);
    }

    public async Task<byte[]> DownloadAsync(string url, CancellationToken ct)
        => string.IsNullOrEmpty(url) ? Array.Empty<byte>() : await _http.GetByteArrayAsync(url, ct);

    public async Task PromoteAsync(NexusComponent c, byte[] bytes, CancellationToken ct)
    {
        if (!IsConfigured || bytes.Length == 0) return;
        // Upload to the approved repo (component upload API; PyPI form fields shown).
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "pypi.asset", c.FileName ?? $"{c.Name}-{c.Version}.tar.gz");
        var url = $"{_baseUrl}/service/rest/v1/components?repository={ApprovedRepo(c.Ecosystem)}";
        using var resp = await _http.PostAsync(url, form, ct);
        _log.LogInformation("Promote {Pkg} -> {Repo}: {Status}", c.Name, ApprovedRepo(c.Ecosystem), (int)resp.StatusCode);
    }

    public async Task HoldAsync(NexusComponent c, string reason, CancellationToken ct)
    {
        _log.LogWarning("HELD in quarantine: {Pkg}@{Ver} — {Reason}", c.Name, c.Version, reason);
        if (_deleteOnHold && IsConfigured)
        {
            var url = $"{_baseUrl}/service/rest/v1/components/{Uri.EscapeDataString(c.ComponentId)}";
            using var resp = await _http.DeleteAsync(url, ct);
            _log.LogInformation("Delete held {Pkg}: {Status}", c.Name, (int)resp.StatusCode);
        }
        // Default: leave it in the quarantine repo as the physical holding area.
    }

    private static Ecosystem MapEco(string? f) => f?.ToLowerInvariant() switch
    {
        "npm" => Ecosystem.npm, "nuget" => Ecosystem.NuGet, "cargo" => Ecosystem.Cargo,
        "go" => Ecosystem.Go, _ => Ecosystem.PyPI
    };
}
