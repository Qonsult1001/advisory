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
    // Off by default: verified that Nexus 3.93 CE hosted (approved) repos serve dynamically and do NOT
    // negatively-cache a miss, so a promoted package is served on the very next pull with no invalidation
    // (the 24h negative-cache lives only on the quarantine PROXY, which developers never hit). Kept as a
    // defensive switch for a future Nexus version or a proxy-fronted deployment where 404s could stick.
    private readonly bool _invalidateOnPromote;

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
        _invalidateOnPromote = cfg.GetValue("NEXUS_INVALIDATE_ON_PROMOTE", false);

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
                // Cargo on this Nexus version stores raw-HTTP fetches with the request PATH as the name
                // (the maintained cargo plugin is archived, so proper crate indexing isn't available).
                // Normalise "/api/v1/crates/<crate>/<ver>/download" → name=<crate>, version=<ver>; drop the
                // bare metadata component ("/api/v1/crates/<crate>" with no version) so it isn't gated twice.
                if (eco == Ecosystem.Cargo && name.StartsWith("/api/v1/crates/", StringComparison.OrdinalIgnoreCase))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(name, @"^/api/v1/crates/(?<n>[^/]+)/(?<v>[^/]+)/download$");
                    if (!m.Success) continue; // bare metadata blob — not a real artifact, skip
                    name = m.Groups["n"].Value;
                    version = m.Groups["v"].Value;
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

        // Defensive (off by default — hosted 404s are not sticky on Nexus 3.93 CE): if enabled, bust any
        // negative cache on the approved repo so a developer's retry sees the freshly-uploaded package.
        if (_invalidateOnPromote && resp.IsSuccessStatusCode)
        {
            try
            {
                using var inv = await _http.PostAsync(
                    $"{_baseUrl}/service/rest/v1/repositories/{ApprovedRepo(c.Ecosystem)}/invalidate-cache", null, ct);
            }
            catch { /* best-effort */ }
        }
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
        var nameLower = name.ToLowerInvariant();
        string url = eco switch
        {
            Ecosystem.PyPI    => $"{repoBase}/simple/{Uri.EscapeDataString(nameLower)}/",
            Ecosystem.npm     => $"{repoBase}/{Uri.EscapeDataString(name)}",
            // NuGet flat-container ("PackageBaseAddress") on a Nexus V3 proxy lives at v3/content/0/.
            // The package's index.json lists its available versions; the artifact is the .nupkg below it.
            Ecosystem.NuGet   => $"{repoBase}/v3/content/0/{Uri.EscapeDataString(nameLower)}/index.json",
            // Cargo: when the version is known, hit the .crate download directly (the index/metadata GET
            // would otherwise be cached as a junk component). Only fall back to metadata to find the latest.
            Ecosystem.Cargo   => string.IsNullOrEmpty(version)
                ? $"{repoBase}/api/v1/crates/{Uri.EscapeDataString(name)}"
                : $"{repoBase}/api/v1/crates/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/download",
            // RubyGems: the .gem download is served directly at gems/<name>-<ver>.gem — no metadata step.
            Ecosystem.RubyGems=> $"{repoBase}/gems/{Uri.EscapeDataString(name)}-{Uri.EscapeDataString(version)}.gem",
            Ecosystem.Composer=> $"{repoBase}/p2/{Uri.EscapeDataString(name)}.json",
            Ecosystem.Maven   => $"{repoBase}/{name.Replace('.', '/').Replace(':', '/')}/",
            // Go module proxy: the module path keeps its slashes (NOT url-encoded). With a version, hit the
            // .zip directly; without one, ask @latest to discover the version (resolved in ResolveArtifactUrl).
            Ecosystem.Go      => string.IsNullOrEmpty(version)
                ? $"{repoBase}/{GoEscape(name)}/@latest"
                : $"{repoBase}/{GoEscape(name)}/@v/{Uri.EscapeDataString(version)}.zip",
            _                 => $"{repoBase}/{Uri.EscapeDataString(name)}",
        };
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            _log.LogInformation("Fetch {Pkg} into {Repo}: {Status}", name, $"{def.Prefix}-{_quarantineSuffix}", (int)resp.StatusCode);
            // Requesting the index/metadata is NOT enough to cache a component — Nexus only stores a
            // component when its actual ARTIFACT is requested through the proxy. Resolve each ecosystem's
            // artifact URL from the index response and pull it so the component materialises in quarantine.
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                var fileUrl = ResolveArtifactUrl(eco, repoBase, url, name, version, body);
                if (fileUrl != null)
                {
                    using var f = await _http.GetAsync(fileUrl, ct);
                    _log.LogInformation("Cached {Pkg} artifact: {Status} ({Url})", name, (int)f.StatusCode, fileUrl);
                }
                else
                    _log.LogWarning("No artifact URL resolved for {Eco} {Pkg}@{Ver} — component may not cache.", eco, name, version);
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Fetch {Pkg} into quarantine failed.", name); return false; }
    }

    /// <summary>True if a PyPI artifact filename is a pre-release (alpha/beta/rc/dev/preview), which
    /// `pip install <name>` skips by default (PEP 440). We look for a pre-release marker attached to the
    /// numeric version, e.g. "wrapt-2.3.0rc1.tar.gz", "foo-1.0b2-py3...", "bar-2.0.dev3-...". A plain
    /// stable version like "wrapt-1.16.0-cp312...whl" returns false.</summary>
    /// <summary>Extract the version token from a PyPI artifact filename given the package name — the
    /// segment right after "&lt;name&gt;-". "idna-3.18-py3-none-any.whl"→"3.18"; "idna-3.18.tar.gz"→"3.18".
    /// PyPI normalises the name (─/. → -) in filenames, so we match case-insensitively on the leading
    /// "&lt;name&gt;-" and read up to the next "-" (wheel) or ".tar.gz"/".zip" (sdist).</summary>
    public static string? PyVersionOf(string fileName, string name)
    {
        // Take just the file part (strip any leading path).
        var f = fileName.Substring(fileName.LastIndexOf('/') + 1);
        var m = System.Text.RegularExpressions.Regex.Match(f,
            @"^.+?-(?<ver>[^-]+?)(?:-.*)?\.(?:whl|tar\.gz|zip)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["ver"].Value : null;
    }

    public static bool IsPyPreRelease(string fileName) =>
        // A pre-release marker (a/b/c/rc/alpha/beta/dev/pre/preview) sits on the numeric version, either
        // attached ("2.3.0rc1", "1.0b2") or dot-separated (".dev3", ".pre2"), followed by a number. The
        // preceding "\d[.]?" anchors it to the version so build tags like "cp312" don't false-positive.
        System.Text.RegularExpressions.Regex.IsMatch(fileName,
            @"\d\.?(?:a|b|c|rc|alpha|beta|dev|pre|preview)\d",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Escape a Go module path for the module proxy: keep '/' separators, but the Go proxy
    /// lowercases the path by encoding each uppercase letter X as "!x". Most module paths are already
    /// lowercase; this handles the occasional uppercase (e.g. github.com/BurntSushi/toml).</summary>
    private static string GoEscape(string module)
    {
        var sb = new System.Text.StringBuilder(module.Length);
        foreach (var c in module)
            if (char.IsUpper(c)) { sb.Append('!'); sb.Append(char.ToLowerInvariant(c)); }
            else sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// Given a proxy index/metadata response, work out the URL of the actual artifact to pull so Nexus
    /// caches a component. Each registry exposes the artifact differently; unknown ecosystems fall back
    /// to the index URL (best-effort). Returns null if no artifact URL could be derived.
    /// </summary>
    private string? ResolveArtifactUrl(Ecosystem eco, string repoBase, string indexUrl, string name, string version, string body)
    {
        try
        {
            switch (eco)
            {
                case Ecosystem.PyPI:
                {
                    // simple-index HTML lists all files (wheels + sdists) in ASCENDING version order. Choose
                    // the file `pip install <name>` would install: newest STABLE version (pip skips
                    // pre-releases by default), and for that version PREFER the wheel (.whl) over the sdist
                    // (.tar.gz) — a wheel installs with no build step, so we don't drag in build backends
                    // (setuptools/wheel) that would also need gating. If a version is pinned, match it.
                    var hrefs = System.Text.RegularExpressions.Regex.Matches(body, "href=\"([^\"]+\\.(?:whl|tar\\.gz))")
                        .Select(m => m.Groups[1].Value.Split('#')[0]).ToList();
                    string? Pick(IEnumerable<string> files)
                    {
                        // Among files for the chosen version, prefer a .whl; else the sdist.
                        string? whl = null, sdist = null;
                        foreach (var f in files)
                            if (f.EndsWith(".whl")) whl ??= f; else sdist ??= f;
                        return whl ?? sdist;
                    }
                    string? rel;
                    if (!string.IsNullOrEmpty(version))
                        rel = Pick(hrefs.Where(h => h.Contains(version)));
                    else
                    {
                        // Newest stable = the version of the LAST non-pre-release file; group by that token.
                        var stable = hrefs.Where(h => !IsPyPreRelease(h)).ToList();
                        var pool = stable.Count > 0 ? stable : hrefs;   // fall back to pre-releases only if nothing stable
                        var newest = pool.LastOrDefault();
                        var ver = newest is null ? null : PyVersionOf(newest, name);
                        rel = ver is null ? newest : Pick(pool.Where(h => PyVersionOf(h, name) == ver));
                    }
                    return rel is null ? null : new Uri(new Uri(indexUrl.EndsWith('/') ? indexUrl : indexUrl + "/"), rel).ToString();
                }
                case Ecosystem.npm:
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("versions", out var versions) || versions.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
                    var verEl = PickVersion(root, versions, version);
                    if (verEl is { } v && v.TryGetProperty("dist", out var dist) && dist.TryGetProperty("tarball", out var tb))
                        return ReRoot(repoBase, tb.GetString());
                    return null;
                }
                case Ecosystem.NuGet:
                {
                    // Flat-container index lists "versions"; the artifact is v3/content/0/<id>/<ver>/<id>.<ver>.nupkg.
                    var nameLower = name.ToLowerInvariant();
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    string? ver = null;
                    if (doc.RootElement.TryGetProperty("versions", out var vers) && vers.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var list = vers.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
                        ver = (!string.IsNullOrEmpty(version) && list.Contains(version)) ? version : list.LastOrDefault();
                    }
                    if (string.IsNullOrEmpty(ver)) return null;
                    var verLower = ver.ToLowerInvariant();
                    return $"{repoBase}/v3/content/0/{nameLower}/{verLower}/{nameLower}.{verLower}.nupkg";
                }
                case Ecosystem.Go:
                    // With a known version we already requested the .zip (index URL was the artifact).
                    // For @latest, the response is {"Version":"vX.Y.Z",...}; resolve its .zip.
                    if (!string.IsNullOrEmpty(version)) return null;
                    using (var doc = System.Text.Json.JsonDocument.Parse(body))
                    {
                        if (doc.RootElement.TryGetProperty("Version", out var gv) && gv.GetString() is { Length: > 0 } gver)
                            return $"{repoBase}/{GoEscape(name)}/@v/{Uri.EscapeDataString(gver)}.zip";
                    }
                    return null;
                case Ecosystem.RubyGems:
                    // The index URL IS the .gem download — the first fetch already cached it; nothing more to pull.
                    return null;
                case Ecosystem.Cargo:
                    // With a known version we already fetched the .crate directly (index URL was the artifact).
                    // Only when version was empty did we fetch metadata — resolve max_version's download here.
                    if (!string.IsNullOrEmpty(version)) return null;
                    using (var doc = System.Text.Json.JsonDocument.Parse(body))
                    {
                        if (doc.RootElement.TryGetProperty("crate", out var cr) && cr.TryGetProperty("max_version", out var mv))
                        {
                            var ver = mv.GetString();
                            if (!string.IsNullOrEmpty(ver)) return $"{repoBase}/api/v1/crates/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(ver)}/download";
                        }
                    }
                    return null;
                case Ecosystem.Composer:
                {
                    // p2 metadata: packages[name][].dist.url is the zip.
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("packages", out var pkgs) && pkgs.ValueKind == System.Text.Json.JsonValueKind.Object)
                        foreach (var pkg in pkgs.EnumerateObject())
                            if (pkg.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                                foreach (var rel in pkg.Value.EnumerateArray())
                                {
                                    var rv = rel.TryGetProperty("version", out var vv) ? vv.GetString() : null;
                                    if (!string.IsNullOrEmpty(version) && rv != version && rv != $"v{version}") continue;
                                    if (rel.TryGetProperty("dist", out var d) && d.TryGetProperty("url", out var du))
                                        return ReRoot(repoBase, du.GetString());
                                }
                    return null;
                }
                default:
                    // Maven and any unmapped ecosystem: the index request itself already touches the artifact
                    // path tree; return null so we don't double-fetch. (Maven caches on the POM/jar GET.)
                    return null;
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Artifact-URL resolution failed for {Eco} {Pkg}.", eco, name); return null; }
    }

    // Pick the requested version element from an npm "versions" map; fall back to dist-tags.latest, then first.
    private static System.Text.Json.JsonElement? PickVersion(System.Text.Json.JsonElement root, System.Text.Json.JsonElement versions, string version)
    {
        if (!string.IsNullOrEmpty(version) && versions.TryGetProperty(version, out var exact)) return exact;
        string? latest = root.TryGetProperty("dist-tags", out var dt) && dt.TryGetProperty("latest", out var lt) ? lt.GetString() : null;
        if (latest != null && versions.TryGetProperty(latest, out var le)) return le;
        foreach (var p in versions.EnumerateObject()) return p.Value;
        return null;
    }

    // Recursively yield every property named <key> anywhere in the JSON tree (for nested registration docs).
    private static IEnumerable<System.Text.Json.JsonElement> EnumDeep(System.Text.Json.JsonElement el, string key)
    {
        if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
            foreach (var p in el.EnumerateObject())
            {
                if (p.NameEquals(key)) yield return p.Value;
                foreach (var d in EnumDeep(p.Value, key)) yield return d;
            }
        else if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var item in el.EnumerateArray())
                foreach (var d in EnumDeep(item, key)) yield return d;
    }

    // A registry's artifact URL points at the upstream host (which Nexus may have rewritten to itself,
    // possibly including "/repository/<repo>/"). Re-root the package path on our quarantine proxy exactly once.
    private string ReRoot(string repoBase, string? artifactUrl)
    {
        if (string.IsNullOrEmpty(artifactUrl)) return repoBase;
        var path = Uri.TryCreate(artifactUrl, UriKind.Absolute, out var abs) ? abs.AbsolutePath : artifactUrl;
        var marker = "/repository/";
        var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            // strip "/repository/<repo>/" → keep the package path
            var after = path[(idx + marker.Length)..];
            var slash = after.IndexOf('/');
            path = slash >= 0 ? after[(slash + 1)..] : after;
        }
        return $"{repoBase}/{path.TrimStart('/')}";
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
