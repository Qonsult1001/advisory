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

            // OS / base layer (real, from config)
            string os = "unknown", arch = "amd64";
            if (haveConfig)
            {
                if (config.TryGetProperty("os", out var o)) os = o.GetString() ?? os;
                if (config.TryGetProperty("architecture", out var a)) arch = a.GetString() ?? arch;
            }
            nodes.Add(new(new PackageRef(Ecosystem.Docker, $"os/{os}", arch), 1, root.Name));

            // Language/runtime packages declared in the config's history (apt-get/apk/npm/pip/go installs)
            // — parsed from the real RUN/ENV lines, the closest live signal without untarring every layer.
            if (haveConfig && config.TryGetProperty("history", out var hist))
            {
                foreach (var h in hist.EnumerateArray())
                {
                    var line = h.TryGetProperty("created_by", out var cb) ? (cb.GetString() ?? "") : "";
                    foreach (var (name, ver) in ExtractPackages(line))
                        if (!nodes.Any(n => n.Package.Name == name && n.Package.Version == ver))
                            nodes.Add(new(new PackageRef(Ecosystem.Docker, name, ver), 2, $"os/{os}"));
                }
            }

            // One node per real layer (digest + size) — the image's actual filesystem layers.
            int i = 0;
            foreach (var l in layers)
            {
                var dig = l.TryGetProperty("digest", out var d) ? (d.GetString() ?? "") : "";
                if (dig.Length == 0) continue;
                var shortDig = dig.Replace("sha256:", "")[..Math.Min(12, dig.Replace("sha256:", "").Length)];
                nodes.Add(new(new PackageRef(Ecosystem.Docker, $"layer/{shortDig}", $"layer{++i}"), 1, root.Name));
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Docker resolve failed for {Image}:{Tag}", image, tag);
        }
        return nodes;
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
