using Advisory.Api.Gate;
using Advisory.Api.Models;

namespace Advisory.Api.Nexus;

/// <summary>
/// THE interception piece. Polls the Nexus quarantine repo; for each new component it
/// downloads the bytes, runs the full gate (tree-walk + all sources + weights scan), then:
///   Allow      -> promote to the approved repo devs pull from
///   Block/Quar -> leave held in quarantine (the physical quarantine location) + audit
/// This is what turns the tested decision engine into a system that actually stops packages.
/// Runs only when NEXUS_URL is configured; otherwise idles (decision API still usable directly).
/// </summary>
public class PromotionBridge : BackgroundService
{
    private readonly INexusClient _nexus;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PromotionBridge> _log;
    private readonly TimeSpan _interval;
    private readonly HashSet<string> _processed = new();

    public PromotionBridge(INexusClient nexus, IServiceScopeFactory scopes,
                           IConfiguration cfg, ILogger<PromotionBridge> log)
    {
        _nexus = nexus; _scopes = scopes; _log = log;
        _interval = TimeSpan.FromSeconds(cfg.GetValue("NEXUS_POLL_SECONDS", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_nexus.IsConfigured)
        {
            _log.LogInformation("PromotionBridge idle: NEXUS_URL not set (decision API still available).");
            return;
        }
        _log.LogInformation("PromotionBridge active: polling quarantine every {Sec}s.", _interval.TotalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var components = await _nexus.ListQuarantineAsync(ct);

                foreach (var c in components)
                {
                    // A package still SITTING in quarantine has not been promoted yet, so we re-evaluate
                    // it every cycle. This makes exceptions, policy changes, and revocations take effect
                    // automatically — the moment the gate verdict flips to Allow, it promotes. We only
                    // skip a component once it has been PROMOTED (recorded in _processed after promote).
                    if (_processed.Contains(c.ComponentId)) continue;

                    byte[] bytes = Array.Empty<byte>();
                    if (c.Ecosystem == Ecosystem.HuggingFace || (c.FileName?.EndsWith(".bin") ?? false))
                        bytes = await _nexus.DownloadAsync(c.DownloadUrl, ct); // needed for pickle scan

                    var pkg = new PackageRef(c.Ecosystem, c.Name, c.Version, c.Sha256, c.FileName,
                        LocalPath: null);

                    using var scope = _scopes.CreateScope();
                    var gate = scope.ServiceProvider.GetRequiredService<IGateEngine>();
                    var result = await gate.EvaluateAsync(pkg, ct);

                    // Persist the decision into the scan store so the Quarantine view can show, per
                    // package, what the pipeline did (promoted / blocked / held) and why.
                    var repo = NexusEcosystems.TryGet(c.Ecosystem, out var def) ? $"{def.Prefix}-quarantine" : "quarantine";
                    var scans = scope.ServiceProvider.GetRequiredService<Advisory.Api.Scan.ScanStore>();
                    try { await scans.RecordDecisionAsync(repo, pkg, result); } catch { /* best-effort observability */ }

                    // An operator-revoked package is held regardless of the gate verdict — revoke is an
                    // explicit "no" that must not be silently re-promoted.
                    var revoked = scans.IsRevoked(c.Ecosystem, c.Name, c.Version);

                    if (result.Decision == GateDecision.Allow && !revoked)
                    {
                        if (bytes.Length == 0) bytes = await _nexus.DownloadAsync(c.DownloadUrl, ct);
                        await _nexus.PromoteAsync(c, bytes, ct);
                        _processed.Add(c.ComponentId);   // promoted — don't re-promote next cycle
                        _log.LogInformation("PROMOTED {Pkg}@{Ver}", c.Name, c.Version);
                    }
                    else
                    {
                        // Held/blocked — NOT added to _processed, so it's re-checked next cycle and
                        // promotes automatically once an exception/policy change flips it to Allow.
                        await _nexus.HoldAsync(c, string.Join("; ", result.TriggeredRules), ct);
                        _log.LogWarning("HELD {Pkg}@{Ver}: {Decision}", c.Name, c.Version, result.Decision);
                    }
                }
            }
            catch (Exception ex) { _log.LogError(ex, "PromotionBridge cycle failed"); }
            await Task.Delay(_interval, ct);
        }
    }
}
