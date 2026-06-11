using Advisory.Api.Audit;
using Advisory.Api.Models;

namespace Advisory.Api.Policy;

/// <summary>
/// Background job: periodically purges expired exceptions and records each expiry as an
/// audit event, so the register self-cleans and the trail shows when an override lapsed.
/// </summary>
public class ExceptionSweepJob : BackgroundService
{
    private readonly IPolicyStore _store;
    private readonly IAuditLog _audit;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public ExceptionSweepJob(IPolicyStore store, IAuditLog audit) { _store = store; _audit = audit; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var p = _store.Current;
                var expired = p.Exceptions.Where(e => e.Expires < DateTimeOffset.UtcNow).ToList();
                if (expired.Count > 0)
                {
                    foreach (var e in expired)
                        await _audit.AppendAsync(new AuditEntry(Guid.NewGuid(),
                            new PackageRef(Ecosystem.PyPI, e.Package, "*"), GateDecision.Block,
                            Array.Empty<Finding>(), new[] { $"SEC-EXC-EXPIRED:{e.Ticket}" },
                            e.Ticket, p.Version, 0, DateTimeOffset.UtcNow, null,
                            $"Exception for {e.Package} (ticket {e.Ticket}) expired {e.Expires:o} and was purged."));
                    // JSON round-trip clone — the previous field-by-field copy dropped Watches and
                    // the content-scan/reachability flags every time an exception expired.
                    var kept = System.Text.Json.JsonSerializer.Deserialize<FirewallPolicy>(
                        System.Text.Json.JsonSerializer.Serialize(p))!;
                    kept.Exceptions = kept.Exceptions.Where(e => e.Expires >= DateTimeOffset.UtcNow).ToList();
                    await _store.UpdateAsync(kept, "exception-sweep");
                }
            }
            catch { /* never crash the host on a sweep error */ }
            await Task.Delay(_interval, ct);
        }
    }
}
