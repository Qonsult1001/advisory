using System.Text.RegularExpressions;
using Advisory.Api.Models;

namespace Advisory.Api.Proxy.Adapters;

/// <summary>
/// PyPI adapter — the reference implementation. pip talks PEP 503 (the "simple" HTML index) + PEP 658
/// (".metadata" sidecars). Index lives at /pypi/simple/&lt;name&gt;/ and artifacts at /pypi/packages/… .
/// This preserves exactly the behaviour the proxy shipped with before the multi-ecosystem refactor.
/// </summary>
public sealed class PyPiProxyAdapter : IEcosystemProxyAdapter
{
    public Ecosystem Ecosystem => Ecosystem.PyPI;
    public string RoutePrefix => "pypi";

    public (string upstreamUrl, string contentType)? MapIndexRequest(string rest, string nexusBase)
    {
        // rest = "simple/<name>" (with or without trailing slash). Fetch the PEP 503 page from quarantine.
        var m = Regex.Match(rest, @"^simple/([^/]+)/?$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var name = m.Groups[1].Value.ToLowerInvariant();
        return ($"{nexusBase}/repository/pypi-quarantine/simple/{Uri.EscapeDataString(name)}/", "text/html");
    }

    public string RewriteIndex(string body, string nexusBase)
    {
        // Absolute Nexus URLs -> proxy path.
        body = Regex.Replace(body, @"https?://[^""']*?/repository/pypi-quarantine/(packages/[^""'#]+)",
            m => "/pypi/" + m.Groups[1].Value, RegexOptions.IgnoreCase);
        // Bare relative hrefs ("../../packages/…" or "packages/…"): normalise to the proxy path.
        body = Regex.Replace(body, @"href=""(?:\.\./)*(packages/[^""#]+)",
            m => "href=\"/pypi/" + m.Groups[1].Value, RegexOptions.IgnoreCase);
        return body;
    }

    public (string approvedUrl, string quarantineUrl)? MapArtifactRequest(string rest, string nexusBase)
        => ($"{nexusBase}/repository/pypi-approved/packages/{rest}",
            $"{nexusBase}/repository/pypi-quarantine/packages/{rest}");

    public bool IsUngatedMetadata(string rest)
        => rest.EndsWith(".metadata", StringComparison.OrdinalIgnoreCase);

    public (string? name, string? version, string? fileName) ParseArtifactPath(string rest)
    {
        // "<name>/<version>/<file>" — pip's simple index serves this shape via Nexus.
        var noQuery = rest.Split('?')[0];
        var segs = noQuery.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length >= 3)
        {
            var file = segs[^1];
            var version = segs[^2];
            var name = segs[^3];
            return (Uri.UnescapeDataString(name), Uri.UnescapeDataString(version), Uri.UnescapeDataString(file));
        }
        return (null, null, null);
    }
}
