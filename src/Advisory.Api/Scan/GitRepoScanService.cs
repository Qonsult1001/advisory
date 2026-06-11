using System.Collections.Concurrent;
using System.Text.Json;
using Advisory.Api.Gate;
using Advisory.Api.Models;

namespace Advisory.Api.Scan;

/// <summary>Per-package gate result inside a git repo scan.</summary>
public record GitRepoPkgResult(
    string Ecosystem,
    string Name,
    string Version,
    string Decision,
    string TopSeverity,
    int Findings);

/// <summary>
/// Stored result of scanning a linked git repository for supply-chain risk (control SEC-SRC-01).
/// The scan fetches known manifest files (package.json, requirements.txt) from the raw GitHub
/// content API, parses declared dependencies, and evaluates each package through the gate engine.
/// </summary>
public class GitRepoScanResult
{
    public string FullName { get; set; } = "";
    public string Status { get; set; } = "Scanning";  // Scanning | Done | Failed
    public int PackagesFound { get; set; }
    public int Critical { get; set; }
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
    public string Verdict { get; set; } = "Scanning"; // Scanning | Clean | Vulnerable | Failed
    public List<GitRepoPkgResult> Packages { get; set; } = new();
    public DateTimeOffset? ScannedAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Scans linked git repositories by fetching manifest files from GitHub raw content and
/// evaluating declared dependencies through the gate engine.
/// Supported manifests: package.json (npm), requirements.txt (PyPI).
/// Results are stored in-memory keyed by FullName (lower-case).
/// </summary>
public class GitRepoScanService
{
    private readonly ConcurrentDictionary<string, GitRepoScanResult> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpClientFactory _http;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<GitRepoScanService> _log;

    public GitRepoScanService(IHttpClientFactory http, IServiceScopeFactory scopes,
        IConfiguration cfg, ILogger<GitRepoScanService> log)
    { _http = http; _scopes = scopes; _cfg = cfg; _log = log; }

    public GitRepoScanResult? Get(string fullName)
        => _results.TryGetValue(fullName, out var r) ? r : null;

    /// <summary>
    /// Start an asynchronous scan of the named repo. Returns immediately; callers poll GET.
    /// Idempotent: calling again overwrites any previous result with a fresh Scanning state.
    /// </summary>
    public GitRepoScanResult Start(string fullName)
    {
        var result = new GitRepoScanResult { FullName = fullName };
        _results[fullName] = result;
        _ = Task.Run(() => RunAsync(fullName, result));
        return result;
    }

    private async Task RunAsync(string fullName, GitRepoScanResult result)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var ct = cts.Token;

            var pkgs = await FetchPackagesAsync(fullName, ct);
            result.PackagesFound = pkgs.Count;

            if (pkgs.Count == 0)
            {
                result.Status = "Done";
                result.Verdict = "Clean";
                result.ScannedAt = DateTimeOffset.UtcNow;
                return;
            }

            using var scope = _scopes.CreateScope();
            var gate = scope.ServiceProvider.GetRequiredService<IGateEngine>();

            foreach (var (pkg, batchCt) in pkgs.Select(p => (p, ct)))
            {
                var r = await gate.EvaluateAsync(pkg, batchCt);
                var findings = r.TreeFindings?.Select(t => t.Finding).ToList() ?? new();
                var top = findings.Count > 0 ? findings.Max(f => f.Severity).ToString() : "None";
                result.Packages.Add(new GitRepoPkgResult(
                    pkg.Ecosystem.ToString(), pkg.Name, pkg.Version,
                    r.Decision.ToString(), top, findings.Count));
                result.Critical += findings.Count(f => f.Severity == Severity.Critical);
                result.High += findings.Count(f => f.Severity == Severity.High);
                result.Medium += findings.Count(f => f.Severity == Severity.Medium);
                result.Low += findings.Count(f => f.Severity == Severity.Low);
            }

            var anyBlock = result.Packages.Any(p => p.Decision == "Block" || p.Decision == "Quarantine");
            result.Verdict = (result.Critical + result.High) > 0 || anyBlock ? "Vulnerable" : "Clean";
            result.Status = "Done";
            result.ScannedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Git repo scan failed for {FullName}", fullName);
            result.Status = "Failed";
            result.Verdict = "Failed";
            result.Error = ex.Message;
            result.ScannedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task<List<PackageRef>> FetchPackagesAsync(string fullName, CancellationToken ct)
    {
        // fullName is "owner/repo"; default branch falls back to "main".
        var parts = fullName.Split('/', 2);
        if (parts.Length != 2) return new();
        var branch = "main";

        var client = _http.CreateClient("github");
        var token = _cfg["GITHUB_TOKEN"];
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var pkgs = new List<PackageRef>();
        pkgs.AddRange(await FetchNpmAsync(client, fullName, branch, ct));
        pkgs.AddRange(await FetchPypiAsync(client, fullName, branch, ct));
        return pkgs;
    }

    private static string RawUrl(string fullName, string branch, string path)
        => $"https://raw.githubusercontent.com/{fullName}/{branch}/{path}";

    private static async Task<string?> TryFetchText(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch { return null; }
    }

    private static async Task<List<PackageRef>> FetchNpmAsync(HttpClient http, string fullName, string branch, CancellationToken ct)
    {
        var text = await TryFetchText(http, RawUrl(fullName, branch, "package.json"), ct);
        if (text is null) return new();
        try
        {
            using var doc = JsonDocument.Parse(text);
            var result = new List<PackageRef>();
            foreach (var section in new[] { "dependencies", "devDependencies" })
            {
                if (!doc.RootElement.TryGetProperty(section, out var deps)) continue;
                foreach (var dep in deps.EnumerateObject())
                {
                    var ver = dep.Value.GetString() ?? "*";
                    // Strip semver range prefixes (^ ~ >= > <= < = v)
                    ver = ver.TrimStart('^', '~', '>', '<', '=', 'v', ' ');
                    if (ver.Length > 0 && ver[0] >= '0' && ver[0] <= '9')
                        result.Add(new PackageRef(Ecosystem.npm, dep.Name, ver));
                }
            }
            return result;
        }
        catch { return new(); }
    }

    private static async Task<List<PackageRef>> FetchPypiAsync(HttpClient http, string fullName, string branch, CancellationToken ct)
    {
        var text = await TryFetchText(http, RawUrl(fullName, branch, "requirements.txt"), ct);
        if (text is null) return new();
        var result = new List<PackageRef>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#') || line.Length == 0) continue;
            // Handle: flask==2.3.0  flask>=2.0  flask~=2.0  flask[async]==2.3.0
            var name = line.Split(new[] { '=', '>', '<', '~', '[', ';', ' ' }, 2)[0].Trim();
            if (name.Length == 0) continue;
            var ver = "*";
            var eqIdx = line.IndexOf("==", StringComparison.Ordinal);
            if (eqIdx >= 0)
            {
                var candidate = line[(eqIdx + 2)..].Split(new[] { ',', ' ', ';' })[0].Trim();
                if (candidate.Length > 0) ver = candidate;
            }
            result.Add(new PackageRef(Ecosystem.PyPI, name, ver));
        }
        return result;
    }
}
