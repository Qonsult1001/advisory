using System.Text.RegularExpressions;
using Advisory.Api.Models;

namespace Advisory.Api.Proxy.Adapters;

/// <summary>
/// NuGet v3 adapter. The NuGet client starts from the service index (index.json), which advertises
/// resource endpoints; downloads come from the flat-container ("PackageBaseAddress"), where a package's
/// versions live at /v3/content/0/&lt;id-lower&gt;/index.json and the artifact at
/// /v3/content/0/&lt;id-lower&gt;/&lt;version&gt;/&lt;id-lower&gt;.&lt;version&gt;.nupkg (confirmed live against the Nexus
/// nuget proxy). We rewrite every advertised resource "@id" in the service index to route back through the
/// proxy, so registrations + flat-container downloads all pass the gate. Client config: a NuGet source
/// pointing at &lt;proxy&gt;/nuget/index/index.json .
/// </summary>
public sealed class NuGetProxyAdapter : IEcosystemProxyAdapter
{
    public Ecosystem Ecosystem => Ecosystem.NuGet;
    public string RoutePrefix => "nuget";

    public (string upstreamUrl, string contentType)? MapIndexRequest(string rest, string nexusBase)
    {
        // The .nupkg artifact is not an index — the artifact route handles it.
        if (rest.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrWhiteSpace(rest)) rest = "index.json";
        return ($"{nexusBase}/repository/nuget-quarantine/{rest}", "application/json");
    }

    public string RewriteIndex(string body, string nexusBase)
    {
        // Rewrite every Nexus quarantine URL advertised in the service index / registration / flat-container
        // JSON so the client comes back through the proxy. A .nupkg goes to the artifact route; everything
        // else (index.json, registrations, flat-container listings) goes to the index route.
        body = Regex.Replace(body,
            @"https?://[^""']*?/repository/nuget-quarantine/([^""']+?\.nupkg)",
            m => "/nuget/artifact/" + m.Groups[1].Value, RegexOptions.IgnoreCase);
        body = Regex.Replace(body,
            @"https?://[^""']*?/repository/nuget-quarantine/([^""']+)",
            m => "/nuget/index/" + m.Groups[1].Value, RegexOptions.IgnoreCase);
        return body;
    }

    public (string approvedUrl, string quarantineUrl)? MapArtifactRequest(string rest, string nexusBase)
        => ($"{nexusBase}/repository/nuget-approved/{rest}",
            $"{nexusBase}/repository/nuget-quarantine/{rest}");

    public bool IsUngatedMetadata(string rest) => false;   // artifact route only serves .nupkg

    public (string? name, string? version, string? fileName) ParseArtifactPath(string rest)
    {
        // Flat-container nupkg path: "v3/content/0/<id-lower>/<version>/<id-lower>.<version>.nupkg".
        var noQuery = rest.Split('?')[0];
        var segs = noQuery.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < 3) return (null, null, null);
        var file = segs[^1];
        if (!file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)) return (null, null, null);
        var version = segs[^2];
        var id = segs[^3];
        return (Uri.UnescapeDataString(id), Uri.UnescapeDataString(version), file);
    }
}
