using System.Net.Http.Headers;
using System.Text.Json;

namespace Advisory.Api.Integrations;

/// <summary>A GitHub repository returned by the Scans List git-repositories endpoint.</summary>
public record GitRepo(
    string Name,
    string FullName,
    string Url,
    string DefaultBranch,
    string Visibility,
    string? Language,
    DateTimeOffset? LastPushed);

/// <summary>
/// Lists GitHub repositories for a configured owner (org or user) so the Xray-style
/// Scans List "Git Repositories" tab can show which source-code repos are under observation.
/// Configured via GITHUB_OWNER (required) and optional GITHUB_TOKEN for private repos.
/// Unconfigured = IsConfigured false; callers must check before use.
/// </summary>
public interface IGitRepoClient
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<GitRepo>> ListRepositoriesAsync(CancellationToken ct);
}

public class GitHubRepoClient : IGitRepoClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GitHubRepoClient> _log;
    private readonly string? _owner;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_owner);

    public GitHubRepoClient(IHttpClientFactory f, IConfiguration cfg, ILogger<GitHubRepoClient> log)
    {
        _log = log;
        _http = f.CreateClient("github");

        // Prefer explicit GITHUB_OWNER; fall back to deriving the owner from EVOLUTION_REPO.
        var owner = cfg["GITHUB_OWNER"];
        if (string.IsNullOrWhiteSpace(owner))
        {
            var repo = cfg["EVOLUTION_REPO"];
            if (!string.IsNullOrWhiteSpace(repo) && repo.Contains('/'))
                owner = repo.Split('/')[0];
        }
        _owner = string.IsNullOrWhiteSpace(owner) ? null : owner;

        var token = cfg["GITHUB_TOKEN"];
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<IReadOnlyList<GitRepo>> ListRepositoriesAsync(CancellationToken ct)
    {
        if (!IsConfigured) return Array.Empty<GitRepo>();
        try
        {
            // Try org endpoint first; if 404 fall back to user endpoint.
            var url = $"https://api.github.com/orgs/{_owner}/repos?per_page=100&sort=pushed";
            using var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound || resp.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                url = $"https://api.github.com/users/{_owner}/repos?per_page=100&sort=pushed";
            else
            {
                resp.EnsureSuccessStatusCode();
                return ParseRepos(await resp.Content.ReadAsStringAsync(ct));
            }

            using var resp2 = await _http.GetAsync(url, ct);
            resp2.EnsureSuccessStatusCode();
            return ParseRepos(await resp2.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            _log.LogWarning("GitHub repo list failed for owner '{Owner}': {Msg}", _owner, ex.Message);
            return Array.Empty<GitRepo>();
        }
    }

    private static IReadOnlyList<GitRepo> ParseRepos(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<GitRepo>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var name         = el.GetProperty("name").GetString() ?? "";
            var fullName     = el.GetProperty("full_name").GetString() ?? "";
            var htmlUrl      = el.GetProperty("html_url").GetString() ?? "";
            var branch       = el.TryGetProperty("default_branch", out var b) ? b.GetString() ?? "main" : "main";
            var visibility   = el.TryGetProperty("visibility", out var v) ? v.GetString() ?? "public" : "public";
            var language     = el.TryGetProperty("language", out var l) && l.ValueKind != JsonValueKind.Null ? l.GetString() : null;
            DateTimeOffset? pushed = null;
            if (el.TryGetProperty("pushed_at", out var p) && p.ValueKind != JsonValueKind.Null
                && DateTimeOffset.TryParse(p.GetString(), out var dt))
                pushed = dt;
            list.Add(new GitRepo(name, fullName, htmlUrl, branch, visibility, language, pushed));
        }
        return list;
    }
}
