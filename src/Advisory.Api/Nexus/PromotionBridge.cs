using Advisory.Api.Gate;
using Advisory.Api.Models;
using Advisory.Api.Policy;

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
    private readonly BridgeResetSignal _reset;
    private readonly TimeSpan _interval;
    private readonly HashSet<string> _processed = new();
    // Per held component: the policy signature under which we last evaluated it. We only re-gate a
    // held package when the policy/exceptions have actually CHANGED — otherwise the same Block would
    // be re-audited every cycle, flooding Violations with duplicates.
    private readonly Dictionary<string, string> _evaluatedUnderPolicy = new();
    // Per component: the (revoked-state, policy-signature) under which we last fully handled it. A
    // revoked package is already held, so re-handling it every cycle just re-audits the same Block —
    // only re-handle when its revoke-state or the policy actually changed.
    private readonly Dictionary<string, string> _lastOutcomeKey = new();

    public PromotionBridge(INexusClient nexus, IServiceScopeFactory scopes,
                           IConfiguration cfg, ILogger<PromotionBridge> log, BridgeResetSignal reset)
    {
        _nexus = nexus; _scopes = scopes; _log = log; _reset = reset;
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
                // A maintenance reset emptied the repos + cleared the stores — flush our in-memory
                // tracking too, so nothing is re-promoted from stale state and the repos stay empty.
                if (_reset.Consume())
                {
                    _processed.Clear(); _evaluatedUnderPolicy.Clear(); _lastOutcomeKey.Clear();
                    _log.LogInformation("PromotionBridge tracking flushed (maintenance reset).");
                }
                var components = await _nexus.ListQuarantineAsync(ct);

                // The current policy signature — changes whenever the policy or its exceptions change.
                string policySig;
                using (var ps = _scopes.CreateScope())
                    policySig = ps.ServiceProvider.GetRequiredService<IPolicyStore>().CurrentSignature ?? "";

                foreach (var c in components)
                {
                    // Decide whether to (re)handle this component. We re-handle when:
                    //  - it's new (never gated), OR
                    //  - it's currently revoked (needs to be re-held / cleared by an exception), OR
                    //  - the policy/exceptions changed since we last gated it (a new exception may now
                    //    apply, or a rule change flips the verdict).
                    // Otherwise skip — avoids re-auditing the same unchanged decision every cycle.
                    bool revokedNow;
                    using (var rs = _scopes.CreateScope())
                        revokedNow = rs.ServiceProvider.GetRequiredService<Advisory.Api.Scan.ScanStore>()
                            .IsRevoked(c.Ecosystem, c.Name, c.Version);
                    var seenBefore = _evaluatedUnderPolicy.TryGetValue(c.ComponentId, out var lastSig);
                    var policyChanged = !seenBefore || lastSig != policySig;
                    // Skip when nothing relevant changed since we last fully handled this component. The
                    // outcome key folds in revoke-state + policy signature, so a revoked-and-still-revoked
                    // package under an unchanged policy is skipped (no more re-auditing the same Block).
                    var outcomeKey = $"{(revokedNow ? "R" : "-")}:{policySig}";
                    if (_lastOutcomeKey.TryGetValue(c.ComponentId, out var lastKey) && lastKey == outcomeKey
                        && !policyChanged) continue;

                    byte[] bytes = Array.Empty<byte>();
                    if (c.Ecosystem == Ecosystem.HuggingFace || (c.FileName?.EndsWith(".bin") ?? false))
                        bytes = await _nexus.DownloadAsync(c.DownloadUrl, ct); // needed for pickle scan

                    var pkg = new PackageRef(c.Ecosystem, c.Name, c.Version, c.Sha256, c.FileName,
                        LocalPath: null);

                    using var scope = _scopes.CreateScope();
                    var gate = scope.ServiceProvider.GetRequiredService<IGateEngine>();
                    var result = await gate.EvaluateAsync(pkg, ct);
                    _evaluatedUnderPolicy[c.ComponentId] = policySig;
                    _lastOutcomeKey[c.ComponentId] = outcomeKey;   // remember what we just handled

                    // Persist the decision into the scan store so the Quarantine view can show, per
                    // package, what the pipeline did (promoted / blocked / held) and why.
                    var repo = NexusEcosystems.TryGet(c.Ecosystem, out var def) ? $"{def.Prefix}-quarantine" : "quarantine";
                    var scans = scope.ServiceProvider.GetRequiredService<Advisory.Api.Scan.ScanStore>();
                    try { await scans.RecordDecisionAsync(repo, pkg, result); } catch { /* best-effort observability */ }

                    // An operator-revoked package is normally held regardless of the gate verdict.
                    // BUT a granted EXCEPTION (ExceptionRef set) is a later, explicit operator approval
                    // that OVERRIDES the prior revoke — clear the revocation and let it promote.
                    var revoked = scans.IsRevoked(c.Ecosystem, c.Name, c.Version);
                    if (revoked && result.Decision == GateDecision.Allow && !string.IsNullOrEmpty(result.ExceptionRef))
                    {
                        scans.ClearRevoked(c.Ecosystem, c.Name, c.Version);
                        revoked = false;
                        _log.LogInformation("Revocation cleared by exception {Ref} for {Pkg}@{Ver}", result.ExceptionRef, c.Name, c.Version);
                    }

                    if (result.Decision == GateDecision.Allow && !revoked)
                    {
                        if (bytes.Length == 0) bytes = await _nexus.DownloadAsync(c.DownloadUrl, ct);
                        await _nexus.PromoteAsync(c, bytes, ct);
                        _processed.Add(c.ComponentId);   // promoted — don't re-promote next cycle
                        _log.LogInformation("PROMOTED {Pkg}@{Ver}", c.Name, c.Version);
                    }
                    else
                    {
                        // Held/blocked/revoked — remove from the promoted set (a revoked package was
                        // pulled out of approved) so the next exception/policy change re-promotes it.
                        _processed.Remove(c.ComponentId);
                        await _nexus.HoldAsync(c, string.Join("; ", result.TriggeredRules), ct);
                        _log.LogWarning("HELD {Pkg}@{Ver}: {Decision}{Rev}", c.Name, c.Version, result.Decision, revoked ? " (revoked)" : "");
                    }
                }
            }
            catch (Exception ex) { _log.LogError(ex, "PromotionBridge cycle failed"); }
            await Task.Delay(_interval, ct);
        }
    }
}
