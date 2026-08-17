using Advisory.Api.Models;

namespace Advisory.Api.Proxy.Adapters;

/// <summary>
/// Go module-proxy adapter. The `go` client speaks the module proxy protocol (GOPROXY): for a module it
/// fetches /&lt;module&gt;/@v/list, /&lt;module&gt;/@v/&lt;version&gt;.info, /&lt;module&gt;/@v/&lt;version&gt;.mod and downloads the
/// source at /&lt;module&gt;/@v/&lt;version&gt;.zip (all confirmed live against the Nexus go proxy). The protocol
/// uses fixed, predictable paths — there are no embedded URLs to rewrite, so RewriteIndex is a no-op. The
/// list/.info/.mod responses are ungated metadata (no executable bytes); the .zip is the gated artifact.
/// Client config: GOPROXY=&lt;proxy&gt;/go .
/// </summary>
public sealed class GoProxyAdapter : IEcosystemProxyAdapter
{
    public Ecosystem Ecosystem => Ecosystem.Go;
    public string RoutePrefix => "go";

    public (string upstreamUrl, string contentType)? MapIndexRequest(string rest, string nexusBase)
    {
        // The .zip is the artifact; everything else on the @v path (list/.info/.mod) is metadata served here.
        if (rest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrWhiteSpace(rest)) return null;
        var ct = rest.EndsWith(".info", StringComparison.OrdinalIgnoreCase) ? "application/json"
               : rest.EndsWith(".mod", StringComparison.OrdinalIgnoreCase) ? "text/plain"
               : "text/plain"; // @v/list
        return ($"{nexusBase}/repository/go-quarantine/{rest}", ct);
    }

    // Go module-proxy responses carry no artifact URLs to rewrite — paths are fixed by the protocol.
    public string RewriteIndex(string body, string nexusBase) => body;

    public (string approvedUrl, string quarantineUrl)? MapArtifactRequest(string rest, string nexusBase)
        => ($"{nexusBase}/repository/go-approved/{rest}",
            $"{nexusBase}/repository/go-quarantine/{rest}");

    // list/.info/.mod are metadata the client needs to resolve before it ever asks for the .zip.
    public bool IsUngatedMetadata(string rest)
        => !rest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public (string? name, string? version, string? fileName) ParseArtifactPath(string rest)
    {
        // Module zip path: "<module>/@v/<version>.zip" — module can contain slashes (rsc.io/quote).
        var noQuery = rest.Split('?')[0];
        var at = noQuery.IndexOf("/@v/", StringComparison.Ordinal);
        if (at < 0) return (null, null, null);
        var module = noQuery[..at];                               // "rsc.io/quote"
        var file = noQuery[(at + 4)..];                           // "v1.5.2.zip"
        if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return (null, null, null);
        var version = file[..^4];                                 // "v1.5.2"
        return (Uri.UnescapeDataString(module), version, file);
    }
}
