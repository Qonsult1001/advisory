using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Advisory.Api.Models;

namespace Advisory.Api.Nexus;

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

    /// <summary>Idempotently create the quarantine proxy + approved hosted pair for an ecosystem
    /// (ADR 0001). "Already exists" is success. Returns whether the repos now exist.</summary>
    Task<ProvisionResult> ProvisionAsync(Ecosystem eco, CancellationToken ct);

    /// <summary>Delete both repos for an ecosystem (the guarded remove). Returns how many were removed.</summary>
    Task<int> DeprovisionAsync(Ecosystem eco, CancellationToken ct);

    /// <summary>The repos that currently exist, as a set of names, for live-state reporting.</summary>
    Task<IReadOnlySet<string>> ExistingRepoNamesAsync(CancellationToken ct);

    /// <summary>Revoke an already-approved package: delete it from its approved repo so developers can
    /// no longer pull it. The operator's manual override on a previously-allowed package.</summary>
    Task<bool> RevokeApprovedAsync(Ecosystem eco, string name, string version, CancellationToken ct);

    /// <summary>Delete every component from all firewall (*-quarantine / *-approved) repos. The repos
    /// themselves stay; only their contents are emptied (the "reset demo data" action).</summary>
    Task<int> EmptyFirewallReposAsync(CancellationToken ct);

    /// <summary>Pull a package through its quarantine proxy so Nexus fetches+caches it — i.e. make it
    /// physically land in <eco>-quarantine, exactly as a real pip/npm install would. The bridge then
    /// gates it. Returns true if the fetch reached the upstream.</summary>
    Task<bool> FetchIntoQuarantineAsync(Ecosystem eco, string name, string version, CancellationToken ct);

    /// <summary>Manually promote a held package from quarantine to approved by name+version (the
    /// operator "approve this" override). Finds it in the quarantine repo, downloads + uploads to
    /// approved. Returns true on success.</summary>
    Task<bool> PromoteByNameAsync(Ecosystem eco, string name, string version, CancellationToken ct);

    /// <summary>True only when Nexus's REST API actually answers (status 200). Unlike the list calls,
    /// this does NOT swallow connection failures — the seed uses it to wait for Nexus to finish booting.</summary>
    Task<bool> IsReachableAsync(CancellationToken ct);
}

/// <summary>Outcome of provisioning an ecosystem.</summary>
public record ProvisionResult(bool Ok, bool AlreadyExisted, string? Error);

public class NexusClient : INexusClient
{
    private readonly HttpClient _http;
    private readonly ILogger<NexusClient> _log;
    private readonly string? _baseUrl;
    private readonly string _quarantineSuffix;
    private readonly string _approvedSuffix;
    private readonly bool _deleteOnHold;

    // Per-ecosystem repo names follow the convention "<eco>-<suffix>". The prefix comes from the
    // single source of truth (NexusEcosystems, ADR 0001) — no per-ecosystem switch, no PyPI fallback.
    private string QuarantineRepo(Ecosystem e) => $"{NexusEcosystems.Prefix(e)}-{_quarantineSuffix}";
    private string ApprovedRepo(Ecosystem e) => $"{NexusEcosystems.Prefix(e)}-{_approvedSuffix}";

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
        // Dynamic discovery (ADR 0001): poll every "*-quarantine" repo that exists in Nexus and maps
        // to a known ecosystem by prefix — no hardcoded ecosystem list.
        if (!IsConfigured) return Array.Empty<NexusComponent>();
        return await NexusDiscovery.DiscoverQuarantineAsync(this, ct);
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
                var format = comp.TryGetProperty("format", out var fmt) ? fmt.GetString() : null;
                var assets = comp.GetProperty("assets");
                var first = assets.GetArrayLength() > 0 ? assets[0] : default;
                var dl = first.ValueKind != JsonValueKind.Undefined && first.TryGetProperty("downloadUrl", out var d) ? d.GetString() : null;
                var sha = first.ValueKind != JsonValueKind.Undefined && first.TryGetProperty("checksum", out var cs) && cs.TryGetProperty("sha256", out var sh) ? sh.GetString() : null;
                var fileName = first.ValueKind != JsonValueKind.Undefined && first.TryGetProperty("path", out var p) ? Path.GetFileName(p.GetString()) : null;
                // Map the component to its ecosystem by the REPO PREFIX (ADR 0001) — never the format,
                // which collides for apt (Debian/Ubuntu). Skip + warn if the repo isn't a known ecosystem.
                if (!NexusEcosystems.TryFromRepoName(repo, out var eco))
                {
                    if (!NexusEcosystems.TryFromFormat(format, out eco))
                    {
                        _log.LogWarning("Nexus repo '{Repo}' (format '{Format}') maps to no known ecosystem — skipping {Pkg}.", repo, format, name);
                        continue;
                    }
                }
                items.Add(new NexusComponent(comp.GetProperty("id").GetString() ?? "",
                    eco, name, version, fileName, sha, dl ?? ""));
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

    public async Task<IReadOnlySet<string>> ExistingRepoNamesAsync(CancellationToken ct)
    {
        var repos = await ListRepositoriesAsync(ct);
        return repos.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> RevokeApprovedAsync(Ecosystem eco, string name, string version, CancellationToken ct)
    {
        if (!IsConfigured || !NexusEcosystems.TryGet(eco, out var def)) return false;
        var repo = $"{def.Prefix}-{_approvedSuffix}";
        // Find the component in the approved repo by name (+ version when given), then delete it by id.
        var items = await ListComponentsAsync(repo, ct);
        var match = items.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrEmpty(version) || string.Equals(c.Version, version, StringComparison.OrdinalIgnoreCase)));
        if (match is null) return false;
        using var resp = await _http.DeleteAsync(
            $"{_baseUrl}/service/rest/v1/components/{Uri.EscapeDataString(match.ComponentId)}", ct);
        _log.LogWarning("REVOKED {Pkg}@{Ver} from {Repo}: {Status}", name, version, repo, (int)resp.StatusCode);
        return resp.IsSuccessStatusCode;
    }

    public async Task<int> EmptyFirewallReposAsync(CancellationToken ct)
    {
        if (!IsConfigured) return 0;
        var repos = await ListRepositoriesAsync(ct);
        var deleted = 0;
        foreach (var r in repos)
        {
            var isQuarantine = r.Name.EndsWith("-quarantine", StringComparison.OrdinalIgnoreCase);
            if (!isQuarantine && !r.Name.EndsWith("-approved", StringComparison.OrdinalIgnoreCase)) continue;

            // 1) Delete components (the packages).
            foreach (var c in await ListComponentsAsync(r.Name, ct))
            {
                using var resp = await _http.DeleteAsync(
                    $"{_baseUrl}/service/rest/v1/components/{Uri.EscapeDataString(c.ComponentId)}", ct);
                if (resp.IsSuccessStatusCode) deleted++;
            }
            // 2) Delete any remaining assets (proxies cache index/metadata blobs as standalone assets
            //    that the component delete leaves behind — these are what keep showing in Nexus).
            await DeleteAllAssetsAsync(r.Name, ct);
            // 3) Invalidate the proxy cache so nothing stale is served or counted.
            if (isQuarantine)
                try { using var inv = await _http.PostAsync($"{_baseUrl}/service/rest/v1/repositories/{r.Name}/invalidate-cache", null, ct); }
                catch { /* best-effort */ }
        }
        _log.LogWarning("Reset: emptied firewall repos — {Count} components deleted, assets purged, proxy caches invalidated.", deleted);
        return deleted;
    }

    private async Task DeleteAllAssetsAsync(string repo, CancellationToken ct)
    {
        string? token = null;
        do
        {
            var url = $"{_baseUrl}/service/rest/v1/assets?repository={repo}" + (token is null ? "" : $"&continuationToken={token}");
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) break;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var a in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var id = a.GetProperty("id").GetString();
                if (id is null) continue;
                using var del = await _http.DeleteAsync($"{_baseUrl}/service/rest/v1/assets/{Uri.EscapeDataString(id)}", ct);
            }
            token = doc.RootElement.TryGetProperty("continuationToken", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        } while (token is not null && !ct.IsCancellationRequested);
    }

    public async Task<bool> FetchIntoQuarantineAsync(Ecosystem eco, string name, string version, CancellationToken ct)
    {
        if (!IsConfigured || !NexusEcosystems.TryGet(eco, out var def)) return false;
        var repoBase = $"{_baseUrl}/repository/{def.Prefix}-{_quarantineSuffix}";

        // Each registry caches a component when its artifact path is requested through the proxy. We
        // request the package metadata/index, which is enough for Nexus to index it into quarantine.
        // (For wheels/tarballs, fetching the index is what makes the component appear in the repo.)
        string url = eco switch
        {
            Ecosystem.PyPI    => $"{repoBase}/simple/{Uri.EscapeDataString(name.ToLowerInvariant())}/",
            Ecosystem.npm     => $"{repoBase}/{Uri.EscapeDataString(name)}",
            Ecosystem.NuGet   => $"{repoBase}/v3/registration/{Uri.EscapeDataString(name.ToLowerInvariant())}/index.json",
            Ecosystem.Cargo   => $"{repoBase}/api/v1/crates/{Uri.EscapeDataString(name)}",
            Ecosystem.RubyGems=> $"{repoBase}/api/v1/gems/{Uri.EscapeDataString(name)}.json",
            Ecosystem.Composer=> $"{repoBase}/p2/{Uri.EscapeDataString(name)}.json",
            Ecosystem.Maven   => $"{repoBase}/{name.Replace('.', '/').Replace(':', '/')}/",
            _                 => $"{repoBase}/{Uri.EscapeDataString(name)}",
        };
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            _log.LogInformation("Fetch {Pkg} into {Repo}: {Status}", name, $"{def.Prefix}-{_quarantineSuffix}", (int)resp.StatusCode);
            // For ecosystems where the index alone doesn't cache the artifact, also pull the first asset.
            if (resp.IsSuccessStatusCode && eco == Ecosystem.PyPI)
            {
                var html = await resp.Content.ReadAsStringAsync(ct);
                var m = System.Text.RegularExpressions.Regex.Match(html, "href=\"([^\"]+\\.(?:whl|tar\\.gz))");
                if (m.Success)
                {
                    var rel = m.Groups[1].Value.Split('#')[0];
                    var fileUrl = new Uri(new Uri(url.EndsWith('/') ? url : url + "/"), rel).ToString();
                    using var f = await _http.GetAsync(fileUrl, ct);
                    _log.LogInformation("Cached {Pkg} artifact: {Status}", name, (int)f.StatusCode);
                }
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Fetch {Pkg} into quarantine failed.", name); return false; }
    }

    public async Task<bool> PromoteByNameAsync(Ecosystem eco, string name, string version, CancellationToken ct)
    {
        if (!IsConfigured || !NexusEcosystems.TryGet(eco, out var def)) return false;
        var quarantine = $"{def.Prefix}-{_quarantineSuffix}";
        var match = (await ListComponentsAsync(quarantine, ct)).FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrEmpty(version) || string.Equals(c.Version, version, StringComparison.OrdinalIgnoreCase)));
        if (match is null) return false;
        var bytes = await DownloadAsync(match.DownloadUrl, ct);
        await PromoteAsync(match, bytes, ct);
        _log.LogWarning("MANUALLY PROMOTED {Pkg}@{Ver} to approved (operator override).", name, version);
        return true;
    }

    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        if (!IsConfigured) return false;
        try
        {
            using var resp = await _http.GetAsync($"{_baseUrl}/service/rest/v1/status", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }   // connection refused while Nexus boots — not yet reachable
    }

    public async Task<ProvisionResult> ProvisionAsync(Ecosystem eco, CancellationToken ct)
    {
        if (!IsConfigured) return new ProvisionResult(false, false, "Nexus is not configured (NEXUS_URL unset).");
        if (!NexusEcosystems.TryGet(eco, out var def))
            return new ProvisionResult(false, false, $"{eco} is not a Nexus-gateable ecosystem.");
        if (!def.ProxyReady)
            return new ProvisionResult(false, false, $"{eco} proxy provisioning is deferred (needs format-specific config).");

        var existing = await ExistingRepoNamesAsync(ct);
        var qName = $"{def.Prefix}-{_quarantineSuffix}";
        var aName = $"{def.Prefix}-{_approvedSuffix}";
        var already = existing.Contains(qName) && existing.Contains(aName);

        // Quarantine = proxy at the upstream; Approved = hosted repo devs pull from.
        var qOk = existing.Contains(qName) || await CreateProxyAsync(def, qName, ct);
        // Composer has no hosted recipe — the proxy IS the gate; no separate approved repo.
        var aOk = def.ProxyOnly || existing.Contains(aName) || await CreateHostedAsync(def, aName, ct);

        if (qOk && aOk) return new ProvisionResult(true, already, null);
        return new ProvisionResult(false, already, $"Failed to create {(qOk ? aName : qName)}.");
    }

    public async Task<int> DeprovisionAsync(Ecosystem eco, CancellationToken ct)
    {
        if (!IsConfigured || !NexusEcosystems.TryGet(eco, out var def)) return 0;
        var removed = 0;
        foreach (var name in new[] { $"{def.Prefix}-{_quarantineSuffix}", $"{def.Prefix}-{_approvedSuffix}" })
        {
            using var resp = await _http.DeleteAsync($"{_baseUrl}/service/rest/v1/repositories/{name}", ct);
            if (resp.IsSuccessStatusCode) removed++;
            else _log.LogWarning("Deprovision {Repo}: {Status}", name, (int)resp.StatusCode);
        }
        return removed;
    }

    private async Task<bool> CreateProxyAsync(NexusEcosystem def, string name, CancellationToken ct)
    {
        // Base proxy body + format-specific blocks Nexus requires (maven layout, nuget protocol).
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["online"] = true,
            ["storage"] = new { blobStoreName = "default", strictContentTypeValidation = true },
            ["proxy"] = new { remoteUrl = def.Upstream, contentMaxAge = 1440, metadataMaxAge = 1440 },
            ["negativeCache"] = new { enabled = true, timeToLive = 1440 },
            ["httpClient"] = new { blocked = false, autoBlock = true },
        };
        AddFormatBlocks(body, def.Format, hosted: false);
        return await PutRepoAsync($"{def.Recipe}/proxy", name, body, ct);
    }

    private async Task<bool> CreateHostedAsync(NexusEcosystem def, string name, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["online"] = true,
            ["storage"] = new { blobStoreName = "default", strictContentTypeValidation = true, writePolicy = "ALLOW" },
        };
        AddFormatBlocks(body, def.Format, hosted: true);
        return await PutRepoAsync($"{def.Recipe}/hosted", name, body, ct);
    }

    /// <summary>Some Nexus recipes reject a bare proxy/hosted body — they need a format block.</summary>
    private static void AddFormatBlocks(Dictionary<string, object?> body, string format, bool hosted)
    {
        switch (format)
        {
            case "maven2":
                body["maven"] = new { versionPolicy = hosted ? "MIXED" : "RELEASE", layoutPolicy = "STRICT" };
                break;
            case "nuget" when !hosted:
                body["nugetProxy"] = new { queryCacheItemMaxAge = 3600, nugetVersion = "V3" };
                break;
        }
    }

    private async Task<bool> PutRepoAsync(string recipe, string name, object body, CancellationToken ct)
    {
        var url = $"{_baseUrl}/service/rest/v1/repositories/{recipe}";
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(url, content, ct);
        // 201 created; 400 with "already exists" is treated as success by the caller's existence check.
        if (resp.IsSuccessStatusCode) { _log.LogInformation("Created Nexus repo {Repo} ({Recipe}).", name, recipe); return true; }
        var msg = await resp.Content.ReadAsStringAsync(ct);
        if (msg.Contains("already exists", StringComparison.OrdinalIgnoreCase)) return true;
        _log.LogWarning("Create {Repo} ({Recipe}) failed: {Status} {Msg}", name, recipe, (int)resp.StatusCode, msg);
        return false;
    }
}
