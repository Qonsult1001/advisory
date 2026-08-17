using Advisory.Api.Gate;
using Advisory.Api.Models;

namespace Advisory.Api.Nexus;

/// <summary>The safe-version advice for a blocked package: the smallest upgrade that passes the gate
/// (nearest) and the newest that passes (latest). Either may be null if none qualifies.</summary>
public record SafeVersions(string? Nearest, string? Latest);

/// <summary>
/// When a package version is BLOCKED, turn "no" into "use this instead". Finds two versions that
/// genuinely pass the full gate: the nearest safe (smallest bump above the blocked one) and the latest
/// safe (newest overall). The advisory's fixed-version hint is used as a starting point but NEVER
/// trusted blindly — every recommended version is re-evaluated through the gate, because a version that
/// fixes one CVE may be blocked for another (or a licence, malware, etc.). The candidate search is
/// bounded so we never gate an entire version history.
/// </summary>
public sealed class SafeVersionRecommender
{
    // How many candidate versions we're willing to gate per direction before giving up.
    private const int MaxProbes = 8;

    private readonly Func<Ecosystem, string, CancellationToken, Task<IReadOnlyList<string>>> _listVersions;
    private readonly IGateEngine _gate;

    public SafeVersionRecommender(
        Func<Ecosystem, string, CancellationToken, Task<IReadOnlyList<string>>> listVersions,
        IGateEngine gate)
    { _listVersions = listVersions; _gate = gate; }

    public async Task<SafeVersions> RecommendAsync(PackageRef blocked, GateResult blockedResult, CancellationToken ct)
    {
        var all = (await Safe(() => _listVersions(blocked.Ecosystem, blocked.Name, ct)))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .OrderBy(v => v, VersionOrder.Comparer)
            .ToList();
        if (all.Count == 0) return new SafeVersions(null, null);

        // Candidates strictly newer than the blocked version.
        var above = all.Where(v => VersionOrder.Compare(v, blocked.Version) > 0).ToList();

        // Seed nearest-search from the advisory fix hint (if it's a real, newer version) so we usually
        // hit a clean version on the first probe instead of walking.
        var hint = blockedResult.Findings.Select(f => f.FixedVersion)
            .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h) && above.Contains(h!));

        // NEAREST: ascending from just above the blocked version, hint first, bounded.
        var nearestOrder = new List<string>();
        if (hint is not null) nearestOrder.Add(hint);
        nearestOrder.AddRange(above.Where(v => v != hint));
        var nearest = await FirstThatPassesAsync(blocked, nearestOrder.Take(MaxProbes), ct);

        // LATEST: descending from newest, bounded.
        var latestOrder = Enumerable.Reverse(all).Where(v => VersionOrder.Compare(v, blocked.Version) > 0);
        var latest = await FirstThatPassesAsync(blocked, latestOrder.Take(MaxProbes), ct);

        return new SafeVersions(nearest, latest);
    }

    private async Task<string?> FirstThatPassesAsync(PackageRef blocked, IEnumerable<string> versions, CancellationToken ct)
    {
        foreach (var v in versions)
        {
            var candidate = blocked with { Version = v, Sha256 = null, FileName = null };
            GateResult r;
            try { r = await _gate.EvaluateAsync(candidate, ct); }
            catch { continue; }   // a probe failure shouldn't sink the whole recommendation.
            if (r.Decision == GateDecision.Allow) return v;
        }
        return null;
    }

    private static async Task<IReadOnlyList<string>> Safe(Func<Task<IReadOnlyList<string>>> f)
    { try { return await f(); } catch { return Array.Empty<string>(); } }
}
