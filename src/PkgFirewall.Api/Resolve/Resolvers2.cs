using System.Text.Json;
using System.Text.RegularExpressions;
using PkgFirewall.Api.Models;

namespace PkgFirewall.Api.Resolve;

/// <summary>NuGet resolver — reads dependency groups from the registration index.</summary>
public class NuGetResolver : TreeWalker, IDependencyResolver
{
    private readonly HttpClient _http;
    public Ecosystem Ecosystem => Ecosystem.NuGet;
    public NuGetResolver(IHttpClientFactory f) => _http = f.CreateClient("resolve");

    public Task<IReadOnlyList<DepNode>> ResolveAsync(PackageRef root, int maxDepth, CancellationToken ct)
        => WalkAsync(root, Ecosystem.NuGet, maxDepth, ct);

    protected override async Task<IEnumerable<PackageRef>> DirectDepsAsync(PackageRef pkg, CancellationToken ct)
    {
        var deps = new List<PackageRef>();
        try
        {
            var id = pkg.Name.ToLowerInvariant();
            var url = $"https://api.nuget.org/v3/registration5-gz-semver2/{id}/{pkg.Version}.json";
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
            var entry = doc.RootElement.GetProperty("catalogEntry");
            if (entry.TryGetProperty("dependencyGroups", out var groups))
                foreach (var g in groups.EnumerateArray())
                    if (g.TryGetProperty("dependencies", out var ds))
                        foreach (var d in ds.EnumerateArray())
                        {
                            var name = d.GetProperty("id").GetString()!;
                            var range = d.TryGetProperty("range", out var r) ? r.GetString() : "*";
                            var ver = Regex.Match(range ?? "", @"[0-9][\w.\-]*").Value;
                            deps.Add(new PackageRef(Ecosystem.NuGet, name,
                                string.IsNullOrEmpty(ver) ? "*" : ver));
                        }
        }
        catch { }
        return deps.DistinctBy(d => d.Name).ToList();
    }
}

/// <summary>Cargo resolver — reads dependencies from the crates.io API.</summary>
public class CargoResolver : TreeWalker, IDependencyResolver
{
    private readonly HttpClient _http;
    public Ecosystem Ecosystem => Ecosystem.Cargo;
    public CargoResolver(IHttpClientFactory f) => _http = f.CreateClient("resolve");

    public Task<IReadOnlyList<DepNode>> ResolveAsync(PackageRef root, int maxDepth, CancellationToken ct)
        => WalkAsync(root, Ecosystem.Cargo, maxDepth, ct);

    protected override async Task<IEnumerable<PackageRef>> DirectDepsAsync(PackageRef pkg, CancellationToken ct)
    {
        var deps = new List<PackageRef>();
        try
        {
            var url = $"https://crates.io/api/v1/crates/{pkg.Name}/{pkg.Version}/dependencies";
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
            foreach (var d in doc.RootElement.GetProperty("dependencies").EnumerateArray())
            {
                if (d.TryGetProperty("kind", out var k) && k.GetString() != "normal") continue;
                var name = d.GetProperty("crate_id").GetString()!;
                var ver = Regex.Match(d.GetProperty("req").GetString() ?? "", @"[0-9][\w.\-]*").Value;
                deps.Add(new PackageRef(Ecosystem.Cargo, name, string.IsNullOrEmpty(ver) ? "*" : ver));
            }
        }
        catch { }
        return deps;
    }
}

/// <summary>
/// Go resolver — reads requirements from the module proxy's mod file.
/// Go's flat MVS means most deps surface at depth 1.
/// </summary>
public class GoResolver : TreeWalker, IDependencyResolver
{
    private readonly HttpClient _http;
    public Ecosystem Ecosystem => Ecosystem.Go;
    public GoResolver(IHttpClientFactory f) => _http = f.CreateClient("resolve");

    public Task<IReadOnlyList<DepNode>> ResolveAsync(PackageRef root, int maxDepth, CancellationToken ct)
        => WalkAsync(root, Ecosystem.Go, maxDepth, ct);

    protected override async Task<IEnumerable<PackageRef>> DirectDepsAsync(PackageRef pkg, CancellationToken ct)
    {
        var deps = new List<PackageRef>();
        try
        {
            var mod = pkg.Name.ToLowerInvariant();
            var url = $"https://proxy.golang.org/{mod}/@v/{pkg.Version}.mod";
            var text = await _http.GetStringAsync(url, ct);
            foreach (Match m in Regex.Matches(text, @"^\s*([^\s]+)\s+v([\w.\-]+)", RegexOptions.Multiline))
            {
                var name = m.Groups[1].Value;
                if (name is "module" or "go" or "require" or "(" or ")") continue;
                deps.Add(new PackageRef(Ecosystem.Go, name, "v" + m.Groups[2].Value));
            }
        }
        catch { }
        return deps;
    }
}
