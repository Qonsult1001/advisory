using Advisory.Api.Models;

namespace Advisory.Api.Nexus;

/// <summary>
/// Fresh-install seed (ADR 0001 / CONTEXT.md): on startup, if NEXUS_AUTOPROVISION is on (default),
/// provision the default gateable ecosystems INTO Nexus via the same provision path the UI/API use —
/// then step back. Idempotent: a no-op on every restart, and it never fights manual UI changes
/// (provision treats "already exists" as success). Set NEXUS_AUTOPROVISION=false to start empty and
/// add ecosystems from the dashboard by hand.
/// </summary>
public class NexusAutoProvisioner : BackgroundService
{
    private readonly INexusClient _nexus;
    private readonly IConfiguration _cfg;
    private readonly ILogger<NexusAutoProvisioner> _log;

    public NexusAutoProvisioner(INexusClient nexus, IConfiguration cfg, ILogger<NexusAutoProvisioner> log)
    { _nexus = nexus; _cfg = cfg; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_cfg.GetValue("NEXUS_AUTOPROVISION", true))
        {
            _log.LogInformation("Nexus auto-provision disabled (NEXUS_AUTOPROVISION=false) — ecosystems added via the UI.");
            return;
        }
        if (!_nexus.IsConfigured)
        {
            _log.LogInformation("Nexus auto-provision idle: NEXUS_URL not set.");
            return;
        }

        // Wait for Nexus to actually answer its REST API (it boots much slower than the API). The
        // status probe does NOT swallow connection failures, so we never seed against a half-booted
        // Nexus (which previously refused every create with "Connection refused").
        var ready = false;
        for (var attempt = 0; attempt < 60 && !ct.IsCancellationRequested; attempt++)
        {
            if (await _nexus.IsReachableAsync(ct)) { ready = true; break; }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
        if (!ready)
        {
            _log.LogWarning("Nexus did not become reachable in time — skipping auto-provision this boot.");
            return;
        }

        var seeded = 0;
        foreach (var def in NexusEcosystems.Provisionable)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var r = await _nexus.ProvisionAsync(def.Ecosystem, ct);
                if (r.Ok && !r.AlreadyExisted) seeded++;
            }
            catch (Exception ex) { _log.LogWarning(ex, "Auto-provision of {Eco} failed (continuing).", def.Ecosystem); }
        }
        _log.LogInformation("Nexus auto-provision complete: {Seeded} new ecosystem(s) seeded; rest already present.", seeded);
    }
}
