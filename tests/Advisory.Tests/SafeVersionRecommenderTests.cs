using Advisory.Api.Gate;
using Advisory.Api.Models;
using Advisory.Api.Nexus;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the safe-version recommender: when a version is blocked it recommends a nearest-safe and a
/// latest-safe version, each of which ACTUALLY passes the gate (never a version that is itself blocked),
/// using the advisory fix hint only as a starting point.
/// </summary>
public class SafeVersionRecommenderTests
{
    // A stub gate: versions in `blockedSet` return Block, everything else Allow.
    private sealed class StubGate : IGateEngine
    {
        private readonly HashSet<string> _blocked;
        public int Calls;
        public StubGate(params string[] blocked) => _blocked = new(blocked);
        public Task<GateResult> EvaluateAsync(PackageRef pkg, CancellationToken ct)
        {
            Calls++;
            var decision = _blocked.Contains(pkg.Version) ? GateDecision.Block : GateDecision.Allow;
            return Task.FromResult(new GateResult(pkg, decision, Array.Empty<Finding>(),
                Array.Empty<string>(), null, DateTimeOffset.UnixEpoch));
        }
    }

    private static GateResult Blocked(PackageRef p, string? fixHint) =>
        new(p, GateDecision.Block,
            new[] { new Finding("CVE-1", Severity.High, 8.0, null, false, "osv", "vuln", FixedVersion: fixHint) },
            new[] { "SEC-VULN-01" }, null, DateTimeOffset.UnixEpoch);

    private static SafeVersionRecommender Make(IGateEngine gate, params string[] versions) =>
        new((eco, name, ct) => Task.FromResult((IReadOnlyList<string>)versions.ToList()), gate);

    [Fact]
    public async Task Recommends_nearest_and_latest_that_pass_the_gate()
    {
        // blocked 1.0.0; only 1.0.0 is bad; versions ascend to 3.0.0.
        var blocked = new PackageRef(Ecosystem.PyPI, "acme", "1.0.0");
        var gate = new StubGate("1.0.0");
        var rec = Make(gate, "1.0.0", "1.1.0", "2.0.0", "3.0.0");
        var r = await rec.RecommendAsync(blocked, Blocked(blocked, fixHint: "1.1.0"), default);
        Assert.Equal("1.1.0", r.Nearest);   // smallest bump that passes
        Assert.Equal("3.0.0", r.Latest);    // newest that passes
    }

    [Fact]
    public async Task Does_not_trust_a_fix_hint_that_is_itself_blocked()
    {
        // OSV says "fixed in 1.1.0", but 1.1.0 is ALSO blocked (another CVE). Must skip to 1.2.0.
        var blocked = new PackageRef(Ecosystem.PyPI, "acme", "1.0.0");
        var gate = new StubGate("1.0.0", "1.1.0");
        var rec = Make(gate, "1.0.0", "1.1.0", "1.2.0", "2.0.0");
        var r = await rec.RecommendAsync(blocked, Blocked(blocked, fixHint: "1.1.0"), default);
        Assert.Equal("1.2.0", r.Nearest);   // NOT the blocked hint 1.1.0
        Assert.Equal("2.0.0", r.Latest);
    }

    [Fact]
    public async Task Returns_null_when_no_newer_version_passes()
    {
        var blocked = new PackageRef(Ecosystem.PyPI, "acme", "1.0.0");
        var gate = new StubGate("1.0.0", "1.1.0", "2.0.0");   // every newer version blocked too
        var rec = Make(gate, "1.0.0", "1.1.0", "2.0.0");
        var r = await rec.RecommendAsync(blocked, Blocked(blocked, fixHint: null), default);
        Assert.Null(r.Nearest);
        Assert.Null(r.Latest);
    }

    [Fact]
    public async Task Ignores_older_versions_only_recommends_upgrades()
    {
        var blocked = new PackageRef(Ecosystem.PyPI, "acme", "2.0.0");
        var gate = new StubGate("2.0.0");
        var rec = Make(gate, "1.0.0", "1.5.0", "2.0.0", "2.1.0");
        var r = await rec.RecommendAsync(blocked, Blocked(blocked, fixHint: null), default);
        Assert.Equal("2.1.0", r.Nearest);   // never recommends the older 1.x
        Assert.Equal("2.1.0", r.Latest);
    }
}
