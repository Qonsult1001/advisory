using System.Text.RegularExpressions;
using Advisory.Api.Models;

namespace Advisory.Api.Proxy.Adapters;

/// <summary>
/// npm adapter. The npm client fetches a package DOCUMENT (JSON) at /{name} (or /@scope/name), whose
/// "dist.tarball" URLs point at the .tgz artifacts. We rewrite those tarball URLs to come back through
/// this proxy so every install is gated. Tarballs live at /{name}/-/{name}-{version}.tgz (confirmed live
/// against the Nexus npm proxy). Client config: npm points its registry at <proxy>/npm/ .
/// </summary>
public sealed class NpmProxyAdapter : IEcosystemProxyAdapter
{
    public Ecosystem Ecosystem => Ecosystem.npm;
    public string RoutePrefix => "npm";

    // The npm client requests the package document at /npm/<name>. That comes in as an index request.
    public (string upstreamUrl, string contentType)? MapIndexRequest(string rest, string nexusBase)
    {
        // A tarball request (/<name>/-/<file>.tgz) is NOT an index — let the artifact route handle it.
        if (rest.Contains("/-/")) return null;
        if (string.IsNullOrWhiteSpace(rest)) return null;
        return ($"{nexusBase}/repository/npm-quarantine/{rest}", "application/json");
    }

    public string RewriteIndex(string body, string nexusBase)
    {
        // Rewrite every "tarball":"…/repository/npm-quarantine/<name>/-/<file>.tgz" to the proxy artifact
        // route so the client downloads the .tgz through the gate.
        return Regex.Replace(body,
            @"https?://[^""']*?/repository/npm-quarantine/([^""']+?\.tgz)",
            m => "/npm/artifact/" + m.Groups[1].Value, RegexOptions.IgnoreCase);
    }

    public (string approvedUrl, string quarantineUrl)? MapArtifactRequest(string rest, string nexusBase)
        => ($"{nexusBase}/repository/npm-approved/{rest}",
            $"{nexusBase}/repository/npm-quarantine/{rest}");

    // npm has no separate ungated metadata sidecar on the artifact route — the package document (index
    // route) is the metadata; the artifact route only ever serves .tgz tarballs.
    public bool IsUngatedMetadata(string rest) => false;

    public (string? name, string? version, string? fileName) ParseArtifactPath(string rest)
    {
        // Tarball path: "<name>/-/<file>.tgz" or "@scope/<name>/-/<file>.tgz". The file is
        // "<name>-<version>.tgz"; for scoped packages the file drops the scope ("@scope/foo" → "foo-1.2.3.tgz").
        var noQuery = rest.Split('?')[0];
        var dash = noQuery.IndexOf("/-/", StringComparison.Ordinal);
        if (dash < 0) return (null, null, null);
        var name = noQuery[..dash];                              // "lodash" or "@babel/core"
        var file = noQuery[(dash + 3)..];                        // "lodash-4.17.21.tgz"
        if (!file.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) return (null, null, null);
        var stem = file[..^4];                                  // "lodash-4.17.21"
        // version = everything after the LAST '-' that starts the semver; the package basename is the
        // unscoped name, so strip "<basename>-" from the front of the stem.
        var basename = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
        var version = stem.StartsWith(basename + "-", StringComparison.OrdinalIgnoreCase)
            ? stem[(basename.Length + 1)..]
            : stem[(stem.LastIndexOf('-') + 1)..];
        return (Uri.UnescapeDataString(name), version, file);
    }
}
