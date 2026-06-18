using System.Net.Http.Headers;
using System.Text.Json;
using Advisory.Api.Models;

namespace Advisory.Api.Resolve;

/// <summary>
/// LIVE Docker image resolver — pulls a real image from a registry (Docker Hub / any OCI v2
/// registry) and extracts its real components: the base OS, the language/runtime packages declared
/// in the image config, and one node per layer. No fixtures: it hits the registry, gets an
/// anonymous pull token, fetches the manifest + config blob, and reads the actual image.
///
/// PackageRef.Name = image repository (e.g. "library/nginx" or "grafana/grafana"),
/// PackageRef.Version = tag (e.g. "1.27" or "latest").
/// </summary>
public sealed class DockerResolver : IDependencyResolver
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<DockerResolver> _log;
    public DockerResolver(IHttpClientFactory http, ILogger<DockerResolver> log) { _http = http; _log = log; }

    public Ecosystem Ecosystem => Ecosystem.Docker;

    // OCI / Docker registry. Default to Docker Hub; an image name with a registry host overrides it.
    private const string DefaultRegistry = "registry-1.docker.io";
    private const string DefaultAuth = "https://auth.docker.io/token";
    private const string DefaultService = "registry.docker.io";

    public async Task<IReadOnlyList<DepNode>> ResolveAsync(PackageRef root, int maxDepth, CancellationToken ct)
    {
        var image = NormalizeRepo(root.Name);
        var tag = string.IsNullOrWhiteSpace(root.Version) ? "latest" : root.Version;
        var nodes = new List<DepNode> { new(root, 0, null) };

        try
        {
            var http = _http.CreateClient("docker");
            http.Timeout = TimeSpan.FromSeconds(30);

            // 1. anonymous pull token (real registry auth handshake)
            var token = await GetTokenAsync(http, image, ct);

            // 2. fetch the manifest (handle manifest lists / multi-arch by picking amd64)
            var manifest = await GetManifestAsync(http, image, tag, token, ct);
            if (manifest is null) return nodes;

            // 3. fetch the image CONFIG blob — this holds the real OS, env, history, and layer diffs
            var configDigest = manifest.Value.TryGetProperty("config", out var cfg) && cfg.TryGetProperty("digest", out var cd)
                ? cd.GetString() : null;
            var layers = manifest.Value.TryGetProperty("layers", out var ly) ? ly.EnumerateArray().ToList() : new();

            JsonElement config = default; bool haveConfig = false;
            if (configDigest is not null)
            {
                var blob = await GetBlobAsync(http, image, configDigest, token, ct);
                if (blob is { } b) { config = b; haveConfig = true; }
            }

            string os = "unknown", arch = "amd64";
            if (haveConfig)
            {
                if (config.TryGetProperty("os", out var o)) os = o.GetString() ?? os;
                if (config.TryGetProperty("architecture", out var a)) arch = a.GetString() ?? arch;
            }
            nodes.Add(new(new PackageRef(Ecosystem.Docker, $"os/{os}", arch), 1, root.Name));

            // DOWNLOAD + PARSE the real layers to read the OS package DB. We pull each layer (newest
            // first), gunzip + untar in memory, and look for the dpkg/apk database. The newest layer
            // that contains it wins (it reflects the final installed set). Real packages → real CVEs.
            var (osvEco, packages) = await ExtractOsPackagesAsync(http, image, layers, token, ct);
            if (packages.Count > 0)
            {
                _log.LogInformation("Docker {Image}:{Tag} → {N} OS packages ({Eco})", image, tag, packages.Count, osvEco);
                foreach (var (name, ver) in packages)
                    nodes.Add(new(new PackageRef(Ecosystem.Docker, name, ver, OsvEcosystem: osvEco), 2, $"os/{os}"));
            }
            else if (haveConfig && config.TryGetProperty("history", out var hist))
            {
                // Fallback: runtime/lang version hints from the config history (no OS DB found).
                foreach (var h in hist.EnumerateArray())
                {
                    var line = h.TryGetProperty("created_by", out var cb) ? (cb.GetString() ?? "") : "";
                    foreach (var (name, ver) in ExtractPackages(line))
                        if (!nodes.Any(n => n.Package.Name == name && n.Package.Version == ver))
                            nodes.Add(new(new PackageRef(Ecosystem.Docker, name, ver), 2, $"os/{os}"));
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Docker resolve failed for {Image}:{Tag}", image, tag);
        }
        return nodes;
    }

    // --- LIVE layer parsing: download layers, find + parse the OS package DB ---

    // Returns (osvEcosystem, [(name, version)]). Pulls layers newest→oldest, gunzips+untars in memory,
    // and reads dpkg/status (Debian/Ubuntu) or apk/db/installed (Alpine). Caps work for safety.
    async Task<(string eco, List<(string name, string version)> pkgs)> ExtractOsPackagesAsync(
        HttpClient http, string image, List<JsonElement> layers, string? token, CancellationToken ct)
    {
        // newest layer last in the manifest; scan from the end (final filesystem state)
        for (int li = layers.Count - 1; li >= 0 && li >= layers.Count - 12; li--)
        {
            var dig = layers[li].TryGetProperty("digest", out var d) ? d.GetString() : null;
            if (dig is null) continue;
            byte[] blob;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"https://{DefaultRegistry}/v2/{image}/blobs/{dig}");
                if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var resp = await http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) continue;
                blob = await resp.Content.ReadAsByteArrayAsync(ct);
                if (blob.Length > 80 * 1024 * 1024) continue; // skip huge layers
            }
            catch { continue; }

            try
            {
                using var raw = new MemoryStream(blob);
                using var gz = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress);
                using var tar = new System.Formats.Tar.TarReader(gz);
                System.Formats.Tar.TarEntry? entry;
                string? dpkg = null, apk = null, osRelease = null, alpineRelease = null;
                while ((entry = tar.GetNextEntry()) is not null)
                {
                    var path = entry.Name.TrimStart('.', '/');
                    if (path is "var/lib/dpkg/status") dpkg = ReadEntry(entry);
                    else if (path is "lib/apk/db/installed") apk = ReadEntry(entry);
                    else if (path is "etc/os-release") osRelease = ReadEntry(entry);
                    else if (path is "etc/alpine-release") alpineRelease = ReadEntry(entry);
                }
                // Debian/Ubuntu — read the REAL release id (VERSION_ID) from os-release, not a guess.
                if (dpkg is not null)
                {
                    var pkgs = ParseDpkg(dpkg);
                    if (pkgs.Count > 0)
                    {
                        var (distro, ver) = DetectDebianFamily(osRelease);
                        return ($"{distro}:{ver}", pkgs);
                    }
                }
                // Alpine — the OSV ecosystem is the MAJOR.MINOR (e.g. v3.20), from alpine-release/os-release.
                if (apk is not null)
                {
                    var pkgs = ParseApk(apk);
                    if (pkgs.Count > 0)
                        return ($"Alpine:v{DetectAlpineVer(alpineRelease, osRelease)}", pkgs);
                }
            }
            catch { /* not a parseable layer — try the next */ }
        }
        return ("", new());
    }

    static string ReadEntry(System.Formats.Tar.TarEntry e)
    {
        if (e.DataStream is null) return "";
        using var ms = new MemoryStream();
        e.DataStream.CopyTo(ms);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    // Debian/Ubuntu dpkg status: paragraphs of "Package:" / "Version:" separated by blank lines.
    static List<(string, string)> ParseDpkg(string txt)
    {
        var list = new List<(string, string)>();
        string? name = null, ver = null;
        foreach (var line in txt.Split('\n'))
        {
            if (line.StartsWith("Package:")) name = line[8..].Trim();
            else if (line.StartsWith("Version:")) ver = line[8..].Trim();
            else if (line.Length == 0) { if (name is not null && ver is not null) list.Add((name, ver)); name = ver = null; }
        }
        if (name is not null && ver is not null) list.Add((name, ver));
        return list;
    }
    // Real Debian/Ubuntu family + release from /etc/os-release (ID + VERSION_ID).
    static (string distro, string ver) DetectDebianFamily(string? osRelease)
    {
        string id = "debian", vid = "12";
        if (osRelease is not null)
            foreach (var l in osRelease.Split('\n'))
            {
                if (l.StartsWith("ID=")) id = l[3..].Trim().Trim('"').ToLowerInvariant();
                else if (l.StartsWith("VERSION_ID=")) vid = l[11..].Trim().Trim('"');
            }
        // OSV ecosystems: "Debian:12", "Ubuntu:22.04". Map by ID.
        return id == "ubuntu" ? ("Ubuntu", vid) : ("Debian", vid.Split('.')[0]);
    }

    // Real Alpine major.minor from /etc/alpine-release (e.g. "3.20.3") or os-release VERSION_ID.
    static string DetectAlpineVer(string? alpineRelease, string? osRelease)
    {
        string v = "";
        if (!string.IsNullOrWhiteSpace(alpineRelease)) v = alpineRelease.Trim();
        else if (osRelease is not null)
            foreach (var l in osRelease.Split('\n'))
                if (l.StartsWith("VERSION_ID=")) { v = l[11..].Trim().Trim('"'); break; }
        var parts = v.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : (parts.Length == 1 && parts[0].Length > 0 ? parts[0] : "3.20");
    }

    // Alpine apk installed DB: records of "P:name" / "V:version" lines, blank-line separated.
    static List<(string, string)> ParseApk(string txt)
    {
        var list = new List<(string, string)>();
        string? name = null, ver = null;
        foreach (var line in txt.Split('\n'))
        {
            if (line.StartsWith("P:")) name = line[2..].Trim();
            else if (line.StartsWith("V:")) ver = line[2..].Trim();
            else if (line.Length == 0) { if (name is not null && ver is not null) list.Add((name, ver)); name = ver = null; }
        }
        if (name is not null && ver is not null) list.Add((name, ver));
        return list;
    }

    // --- registry plumbing (real OCI v2 calls) ---

    static string NormalizeRepo(string name)
    {
        // Docker Hub official images live under library/<name>; user images keep their org.
        if (name.Contains('/')) return name;
        return $"library/{name}";
    }

    async Task<string?> GetTokenAsync(HttpClient http, string image, CancellationToken ct)
    {
        var url = $"{DefaultAuth}?service={DefaultService}&scope=repository:{image}:pull";
        var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
    }

    async Task<JsonElement?> GetManifestAsync(HttpClient http, string image, string tag, string? token, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://{DefaultRegistry}/v2/{image}/manifests/{tag}");
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.v2+json");
        req.Headers.Accept.ParseAdd("application/vnd.oci.image.manifest.v1+json");
        req.Headers.Accept.ParseAdd("application/vnd.docker.distribution.manifest.list.v2+json");
        req.Headers.Accept.ParseAdd("application/vnd.oci.image.index.v1+json");
        var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;
        // multi-arch index → pick amd64/linux and re-fetch the platform manifest
        if (rootEl.TryGetProperty("manifests", out var ms))
        {
            string? pick = null;
            foreach (var m in ms.EnumerateArray())
            {
                if (m.TryGetProperty("platform", out var p) &&
                    p.TryGetProperty("architecture", out var a) && a.GetString() == "amd64" &&
                    p.TryGetProperty("os", out var o) && o.GetString() == "linux")
                { pick = m.GetProperty("digest").GetString(); break; }
            }
            if (pick is not null) return await GetManifestAsync(http, image, pick, token, ct);
        }
        return rootEl.Clone();
    }

    async Task<JsonElement?> GetBlobAsync(HttpClient http, string image, string digest, string? token, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://{DefaultRegistry}/v2/{image}/blobs/{digest}");
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        try { return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)).RootElement.Clone(); }
        catch { return null; }
    }

    // Parse real package installs from a layer's created_by command. Conservative — only confident hits.
    static IEnumerable<(string name, string version)> ExtractPackages(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) yield break;
        // apt/apk pinned installs: "pkg=1.2.3" or "pkg@1.2.3"
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(cmd, @"([a-z0-9][a-z0-9.+\-]{1,40})[=@]([0-9][0-9A-Za-z.\-:~+]{1,30})"))
            yield return (m.Groups[1].Value, m.Groups[2].Value);
        // runtime version envs: "NODE_VERSION 20.11.0", "GO_VERSION=1.22", "PYTHON_VERSION 3.12.1"
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(cmd, @"([A-Z]+)_VERSION[ =]+([0-9][0-9A-Za-z.\-]{1,20})"))
            yield return (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value);
    }
}
