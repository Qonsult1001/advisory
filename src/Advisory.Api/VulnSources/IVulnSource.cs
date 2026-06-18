using Advisory.Api.Models;

namespace Advisory.Api.VulnSources;

/// <summary>Outcome of querying one source — distinguishes "clean" from "couldn't tell".</summary>
public enum SourceStatus { Ok, Empty, Errored, Timeout, NotConfigured, Skipped }

/// <summary>
/// THE single authoritative mapping from our <see cref="Ecosystem"/> enum to OSV.dev's exact
/// ecosystem identifiers. Every source that talks to OSV (vulns AND malware) uses this, so the
/// CVE and malicious-package coverage can never silently drift apart per-ecosystem again.
/// Returns null when OSV has no bare-name ecosystem for the value (Docker/OS distros carry their
/// real OSV ecosystem in <see cref="PackageRef.OsvEcosystem"/> instead, e.g. "Debian:12").
/// </summary>
public static class OsvEcosystems
{
    public static string? Name(Ecosystem e) => e switch
    {
        Ecosystem.PyPI => "PyPI",
        Ecosystem.npm => "npm",
        Ecosystem.NuGet => "NuGet",
        Ecosystem.Cargo => "crates.io",
        Ecosystem.Go => "Go",
        Ecosystem.Maven => "Maven",
        Ecosystem.RubyGems => "RubyGems",
        Ecosystem.Composer => "Packagist",
        Ecosystem.Conan => "ConanCenter",
        Ecosystem.CRAN => "CRAN",
        Ecosystem.DartPub => "Pub",
        // Alpine/Debian/Ubuntu/AIEditorExtensions/HuggingFace/Docker: no bare OSV ecosystem.
        // OS distro packages carry their release-qualified ecosystem in PackageRef.OsvEcosystem.
        _ => null,
    };

    /// <summary>The effective OSV ecosystem for a ref — its explicit OsvEcosystem (Docker OS/lang
    /// packages) if set, else the enum mapping. Null when OSV does not cover it.</summary>
    public static string? For(PackageRef pkg)
        => !string.IsNullOrEmpty(pkg.OsvEcosystem) ? pkg.OsvEcosystem : Name(pkg.Ecosystem);
}

/// <summary>What a source returned, with health so the gate never treats an error as safe.</summary>
public record SourceResult(
    string SourceKey,
    SourceStatus Status,
    IReadOnlyList<Finding> Findings,
    string? Detail = null,         // error message / context for audit
    long ElapsedMs = 0)
{
    public bool IsConclusive => Status is SourceStatus.Ok or SourceStatus.Empty;
}

/// <summary>
/// THE plugin contract. Every feed (free or paid) hides behind this.
/// Returns a SourceResult — findings AND health — so a feed failure registers as
/// uncertainty, not a silent pass.
/// </summary>
public interface IVulnSource
{
    string Key { get; }
    bool IsAvailable { get; }
    Task<SourceResult> QueryAsync(PackageRef pkg, CancellationToken ct);
}
