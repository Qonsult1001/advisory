namespace Advisory.Api.Nexus;

/// <summary>
/// A tiny shared flag the maintenance reset uses to tell the PromotionBridge to flush its in-memory
/// tracking (_processed / _lastOutcomeKey / _evaluatedUnderPolicy). Registered as a singleton so the
/// controller and the hosted bridge see the same instance. The bridge consumes the flag at the top of
/// its next cycle — so after a reset, every package is treated as brand-new and the emptied Nexus repos
/// stay empty instead of being re-promoted from stale memory.
/// </summary>
public sealed class BridgeResetSignal
{
    private volatile bool _pending;
    /// <summary>Raise the flag (called by the reset endpoint).</summary>
    public void Request() => _pending = true;
    /// <summary>Atomically read-and-clear the flag (called by the bridge each cycle).</summary>
    public bool Consume()
    {
        if (!_pending) return false;
        _pending = false;
        return true;
    }
}
