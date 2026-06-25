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
                // Pull the revocation denylist once per cycle so we can re-evaluate revoked packages
                // even if we already processed them (revoke must take effect without a restart).
                using (var revScope = _scopes.CreateScope())
                {
                    var revStore = revScope.ServiceProvider.GetRequiredService<Advisory.Api.Scan.ScanStore>();
                    foreach (var c in components)
                        if (revStore.IsRevoked(c.Ecosystem, c.Name, c.Version)) _processed.Remove(c.ComponentId);
                }

                foreach (var c in components)
                {
                    if (!_processed.Add(c.ComponentId)) continue; // already handled

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
                        _log.LogInformation("PROMOTED {Pkg}@{Ver}", c.Name, c.Version);
                    }
                    else
                    {
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
