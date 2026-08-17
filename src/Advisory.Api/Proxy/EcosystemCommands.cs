using Advisory.Api.Models;

namespace Advisory.Api.Proxy;

/// <summary>
/// The developer-facing REMEDIATION commands per gated ecosystem — "how do I remove this bad version,
/// and how do I install the safe one?". Centralised here so the recall/exposure ledger, the 403 block
/// response, and the downloadable report all speak each ecosystem's real package manager instead of the
/// old pip/npm-only branches. Scope = the ecosystems the reverse proxy gates (PyPI, npm, NuGet, Go) plus
/// apt (Debian/Ubuntu, provisioning deferred). Scanner-gated ecosystems (HuggingFace/Docker/extensions)
/// don't flow through the proxy and so never reach here.
/// </summary>
public static class EcosystemCommands
{
    /// <summary>The package-manager CLI name shown to the developer (pip, npm, dotnet, go, apt).</summary>
    public static string Tool(Ecosystem eco) => eco switch
    {
        Ecosystem.PyPI => "pip",
        Ecosystem.npm => "npm",
        Ecosystem.NuGet => "dotnet",
        Ecosystem.Go => "go",
        Ecosystem.Debian or Ecosystem.Ubuntu => "apt",
        _ => "pkg",
    };

    /// <summary>The command to REMOVE the vulnerable version from a developer's machine.</summary>
    public static string Uninstall(Ecosystem eco, string name) => eco switch
    {
        Ecosystem.PyPI => $"pip uninstall -y {name}",
        Ecosystem.npm => $"npm uninstall {name}",
        Ecosystem.NuGet => $"dotnet remove package {name}",
        Ecosystem.Go => $"go get {name}@none",
        Ecosystem.Debian or Ecosystem.Ubuntu => $"apt-get remove -y {name}",
        _ => $"# remove {name} using your package manager",
    };

    /// <summary>The command to INSTALL a gate-verified SAFE version, or null if none was found.</summary>
    public static string? Install(Ecosystem eco, string name, string? safeVersion)
    {
        if (safeVersion is null) return null;
        return eco switch
        {
            Ecosystem.PyPI => $"pip install {name}=={safeVersion}",
            Ecosystem.npm => $"npm install {name}@{safeVersion}",
            Ecosystem.NuGet => $"dotnet add package {name} --version {safeVersion}",
            Ecosystem.Go => $"go get {name}@v{safeVersion.TrimStart('v')}",
            Ecosystem.Debian or Ecosystem.Ubuntu => $"apt-get install -y {name}={safeVersion}",
            _ => null,
        };
    }
}
