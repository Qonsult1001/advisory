using Advisory.Api.Models;

namespace Advisory.Api.Proxy;

/// <summary>
/// The per-ecosystem knowledge the reverse proxy needs to gate a package manager it doesn't otherwise
/// understand. Everything ELSE — the coordinate gate, the approved-cache re-gate, streaming, the recall/
/// exposure ledger, remediation, single-flight, the deep scan — is shared and ecosystem-agnostic (it works
/// off {ecosystem, name, version}). An adapter supplies ONLY the four things that genuinely differ between
/// pip, npm, NuGet and Go: the index route shape, how to rewrite the index so the client's downloads come
/// back through us, the artifact route shape, and how to parse coordinates out of an artifact path.
///
/// This is what makes "cater for all ecosystems, not just Python" a bounded problem: adding an ecosystem
/// is one adapter, not a fork of the whole proxy.
/// </summary>
public interface IEcosystemProxyAdapter
{
    /// <summary>The ecosystem this adapter serves.</summary>
    Ecosystem Ecosystem { get; }

    /// <summary>The URL prefix the client points at, e.g. "pypi", "npm", "nuget", "go". The proxy exposes
    /// routes under /{Prefix}/… . Matches the Nexus repo-name prefix (NexusEcosystems) so config is uniform.</summary>
    string RoutePrefix { get; }

    /// <summary>Build the upstream (Nexus quarantine) URL for an INDEX/metadata request captured by the
    /// generic index route. <paramref name="rest"/> is the full path the client asked for after the prefix
    /// (e.g. "simple/requests" for pip, "requests" for npm, a v3 index path for NuGet). Returns the Nexus
    /// URL to fetch, and the content-type to serve back. Return null to 404 (unhandled shape).</summary>
    (string upstreamUrl, string contentType)? MapIndexRequest(string rest, string nexusBase);

    /// <summary>Rewrite an index/metadata document so every artifact link points back at THIS proxy
    /// (/{Prefix}/…) instead of at Nexus or the public registry — so the client downloads through the gate.
    /// Only called for index responses that are text (HTML/JSON); binary artifacts are never rewritten.</summary>
    string RewriteIndex(string body, string nexusBase);

    /// <summary>Build the Nexus quarantine + approved URLs for an ARTIFACT request. <paramref name="rest"/>
    /// is the artifact path after the prefix. Returns the two candidate URLs (approved tried first, then
    /// gate-then-serve from quarantine). Return null if this path isn't a gateable artifact (passthrough).</summary>
    (string approvedUrl, string quarantineUrl)? MapArtifactRequest(string rest, string nexusBase);

    /// <summary>Parse {name, version, fileName} from an artifact path so the gate/recall/exposure layer can
    /// key on real coordinates. Return (null, …) when the path carries no gateable coordinates (e.g. a
    /// metadata sidecar) — the proxy then does a best-effort passthrough instead of gating.</summary>
    (string? name, string? version, string? fileName) ParseArtifactPath(string rest);

    /// <summary>True when this request is a harmless metadata sidecar that should be served WITHOUT gating
    /// (e.g. PyPI's PEP 658 ".metadata", npm's package document, Go's ".info"/".mod"). These carry no
    /// executable bytes and the client needs them to resolve before it ever asks for an artifact.</summary>
    bool IsUngatedMetadata(string rest);
}
