using Advisory.Api.Models;

namespace Advisory.Api.Nexus;

/// <summary>How a provisioned ecosystem proxy is created in Nexus + how a repo maps back to it.</summary>
/// <param name="Ecosystem">The Advisory ecosystem.</param>
/// <param name="Prefix">The repo-name prefix — the SINGLE key (ADR 0001). Never the Nexus format,
/// because formats collide (Debian and Ubuntu are both <c>apt</c>); prefixes never do.</param>
/// <param name="Format">The Nexus proxy recipe (format) name.</param>
/// <param name="Upstream">The public registry the quarantine proxy points at.</param>
/// <param name="ProxyReady">True when a simple <c>remoteUrl</c> proxy works today. False for formats
/// that need extra config before provisioning (apt distribution/signing, alpine signing keypair,
/// docker connectors) — these are still mapped (so the bridge gates them once their repos exist) but
/// provisioning is deferred.</param>
/// <param name="ApiPath">The Nexus REST URL segment for create calls. Usually equals <see cref="Format"/>,
/// but maven's format is <c>maven2</c> while its REST path is <c>maven</c>. Defaults to Format.</param>
/// <param name="ProxyOnly">True when Nexus offers no hosted recipe (Composer) — provision the proxy only.</param>
public record NexusEcosystem(Ecosystem Ecosystem, string Prefix, string Format, string Upstream,
    bool ProxyReady, string? ApiPath = null, bool ProxyOnly = false)
{
    /// <summary>The REST URL segment (ApiPath if set, else Format).</summary>
    public string Recipe => ApiPath ?? Format;
}

/// <summary>
/// THE single source of truth tying an <see cref="Ecosystem"/> to its Nexus repo prefix, format,
/// and upstream (ADR 0001). The provision API, the discovery bridge, and the dashboard all derive
/// from this map. There is no silent fallback: an unknown prefix or format resolves to "unknown"
/// (the caller skips + warns) — a Maven package is never mis-scanned as PyPI.
/// </summary>
public static class NexusEcosystems
{
    // Only the OSV-gateable package ecosystems belong here. HuggingFace/Docker have their own
    // scanners (gated outside Nexus); AIEditorExtensions has no package registry; Conda is deferred
    // (no OSV CVE source). Debian/Ubuntu are mapped but ProxyReady=false (apt needs distro config).
    public static readonly IReadOnlyList<NexusEcosystem> All = new[]
    {
        new NexusEcosystem(Ecosystem.PyPI,     "pypi",     "pypi",     "https://pypi.org",                       true),
        new NexusEcosystem(Ecosystem.npm,      "npm",      "npm",      "https://registry.npmjs.org",            true),
        new NexusEcosystem(Ecosystem.NuGet,    "nuget",    "nuget",    "https://api.nuget.org/v3/index.json",   true),
        new NexusEcosystem(Ecosystem.Cargo,    "cargo",    "cargo",    "https://crates.io",                      true),
        new NexusEcosystem(Ecosystem.Go,       "go",       "go",       "https://proxy.golang.org",              true),
        // Maven: format is "maven2" but the REST create path is "maven".
        new NexusEcosystem(Ecosystem.Maven,    "maven",    "maven2",   "https://repo1.maven.org/maven2/",       true, ApiPath: "maven"),
        new NexusEcosystem(Ecosystem.RubyGems, "rubygems", "rubygems", "https://rubygems.org",                   true),
        // Composer has no hosted recipe in Nexus — proxy only.
        new NexusEcosystem(Ecosystem.Composer, "composer", "composer", "https://repo.packagist.org",            true, ProxyOnly: true),
        new NexusEcosystem(Ecosystem.Conan,    "conan",    "conan",    "https://center.conan.io",                true),
        new NexusEcosystem(Ecosystem.CRAN,     "cran",     "r",        "https://cran.r-project.org",            true),
        new NexusEcosystem(Ecosystem.DartPub,  "dartpub",  "pub",      "https://pub.dev",                        true),
        // Mapped for discovery, but provisioning is deferred — these need extra format-specific config:
        // alpine needs a signing keypair; apt (Debian/Ubuntu) needs distribution + signing.
        new NexusEcosystem(Ecosystem.Alpine,   "alpine",   "alpine",   "https://dl-cdn.alpinelinux.org/alpine", false),
        new NexusEcosystem(Ecosystem.Debian,   "debian",   "apt",      "http://deb.debian.org/debian",          false),
        new NexusEcosystem(Ecosystem.Ubuntu,   "ubuntu",   "apt",      "http://archive.ubuntu.com/ubuntu",      false),
    };

    private static readonly Dictionary<Ecosystem, NexusEcosystem> ByEco = All.ToDictionary(e => e.Ecosystem);

    /// <summary>Every ecosystem gated through a Nexus quarantine→approved proxy.</summary>
    public static IEnumerable<Ecosystem> Gateable => All.Select(e => e.Ecosystem);

    /// <summary>Ecosystems whose proxy can be provisioned today (simple remoteUrl). Excludes deferred apt.</summary>
    public static IEnumerable<NexusEcosystem> Provisionable => All.Where(e => e.ProxyReady);

    public static bool TryGet(Ecosystem e, out NexusEcosystem def) => ByEco.TryGetValue(e, out def!);

    /// <summary>The repo-name prefix for an ecosystem. Throws for a non-gateable ecosystem — callers
    /// should only ask about ecosystems in <see cref="Gateable"/>.</summary>
    public static string Prefix(Ecosystem e) => ByEco[e].Prefix;

    /// <summary>The Nexus format recipe for an ecosystem.</summary>
    public static string Format(Ecosystem e) => ByEco[e].Format;

    /// <summary>Map a repo name to its ecosystem by PREFIX, but only when the name follows our
    /// convention "<prefix>-quarantine" or "<prefix>-approved". Returns false for anything else —
    /// so Nexus's own default repos (e.g. "maven-central") are correctly NOT claimed, and there is
    /// never a silent default.</summary>
    public static bool TryFromRepoName(string? repoName, out Ecosystem eco)
    {
        eco = default;
        if (string.IsNullOrWhiteSpace(repoName)) return false;
        var dash = repoName.LastIndexOf('-');
        if (dash <= 0) return false;
        var prefix = repoName[..dash];
        var suffix = repoName[(dash + 1)..];
        if (suffix is not ("quarantine" or "approved")) return false;
        foreach (var e in All)
            if (string.Equals(e.Prefix, prefix, StringComparison.OrdinalIgnoreCase)) { eco = e.Ecosystem; return true; }
        return false;
    }

    /// <summary>Map a Nexus format back to an ecosystem. Returns false on unknown OR ambiguous formats:
    /// apt maps to BOTH Debian and Ubuntu, so format alone cannot decide (ADR 0001) — callers must use
    /// <see cref="TryFromRepoName"/> for those. Refusing here prevents a silent Debian/Ubuntu mis-map.</summary>
    public static bool TryFromFormat(string? format, out Ecosystem eco)
    {
        eco = default;
        if (string.IsNullOrWhiteSpace(format)) return false;
        var matches = All.Where(e => string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1) return false;   // 0 = unknown, >1 = ambiguous (apt) — never guess
        eco = matches[0].Ecosystem;
        return true;
    }

    /// <summary>The honest gate-mechanism label for ANY ecosystem (ADR 0001 / CONTEXT.md):
    /// "nexus-osv" (gated through a Nexus proxy + OSV CVE scan), "scanner" (gated by a specialised
    /// scanner — HuggingFace/Docker/extensions — not Nexus), or "research-only" (no gate today).</summary>
    public static string GateMechanism(Ecosystem e) => e switch
    {
        _ when ByEco.ContainsKey(e) => "nexus-osv",
        Ecosystem.HuggingFace or Ecosystem.Docker or Ecosystem.AIEditorExtensions => "scanner",
        _ => "research-only",   // Conda (deferred) and anything without a CVE source
    };
}
