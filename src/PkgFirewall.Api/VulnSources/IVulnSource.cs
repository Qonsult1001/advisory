using PkgFirewall.Api.Models;

namespace PkgFirewall.Api.VulnSources;

/// <summary>Outcome of querying one source — distinguishes "clean" from "couldn't tell".</summary>
public enum SourceStatus { Ok, Empty, Errored, Timeout, NotConfigured, Skipped }

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
