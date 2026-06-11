using Advisory.Api.Gate;
using Advisory.Api.Models;

namespace Advisory.Api.Queue;

/// <summary>
/// Drains the intake queue and runs each item through the gate. Decoupled from enqueue, so
/// developers/proxy never wait on evaluation. At-least-once: ack only after the decision is
/// recorded; transient failures get retried; poison messages dead-letter after the threshold.
/// </summary>
public class IntakeConsumer : BackgroundService
{
    private readonly IIntakeQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<IntakeConsumer> _log;
    private readonly int _batch;
    private readonly TimeSpan _idle = TimeSpan.FromSeconds(2);

    public IntakeConsumer(IIntakeQueue queue, IServiceScopeFactory scopes,
                          IConfiguration cfg, ILogger<IntakeConsumer> log)
    {
        _queue = queue; _scopes = scopes; _log = log;
        _batch = cfg.GetValue("INTAKE_BATCH", 10);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("IntakeConsumer started (batch {Batch}).", _batch);
        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<QueuedItem> items;
            try { items = await _queue.ReadAsync(_batch, ct); }
            catch (Exception ex) { _log.LogError(ex, "queue read failed"); await Task.Delay(_idle, ct); continue; }

            if (items.Count == 0) { await Task.Delay(_idle, ct); continue; }

            foreach (var item in items)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var gate = scope.ServiceProvider.GetRequiredService<IGateEngine>();
                    var result = await gate.EvaluateAsync(item.Package, ct);
                    await _queue.AckAsync(item.MessageId, ct);
                    _log.LogInformation("Evaluated {Pkg}@{Ver} -> {Decision}",
                        item.Package.Name, item.Package.Version, result.Decision);
                }
                catch (Exception ex)
                {
                    // Poison-message guard: SQL queue tracks DeliveryCount and decides retry vs dead-letter.
                    if (_queue is SqlServerQueue sql)
                        await sql.RetryOrDeadAsync(item, ex.Message, ct);
                    else
                        await _queue.DeadLetterAsync(item, ex.Message, ct);
                    _log.LogWarning(ex, "evaluation failed for {Pkg} (delivery {N})", item.Package.Name, item.DeliveryCount);
                }
            }
        }
    }
}
