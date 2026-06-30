using System.Collections.Concurrent;
using Advisory.Api.Models;

namespace Advisory.Api.Queue;

/// <summary>
/// Fallback intake queue when REDIS_URL is unset (dev/test/air-gapped demo). NOT durable —
/// in-process only. Same contract as the Redis impl so the consumer code is identical.
/// </summary>
public class InMemoryQueue : IIntakeQueue
{
    private readonly ConcurrentQueue<QueuedItem> _q = new();
    private readonly ConcurrentQueue<(QueuedItem, string)> _dead = new();
    private long _processed, _seq;
    public bool IsConfigured => true; // always usable

    public Task<string> EnqueueAsync(PackageRef pkg, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _seq).ToString();
        _q.Enqueue(new QueuedItem(id, pkg, DateTimeOffset.UtcNow, 1));
        return Task.FromResult(id);
    }
    public Task<IReadOnlyList<QueuedItem>> ReadAsync(int max, CancellationToken ct)
    {
        var list = new List<QueuedItem>();
        while (list.Count < max && _q.TryDequeue(out var it)) list.Add(it);
        return Task.FromResult<IReadOnlyList<QueuedItem>>(list);
    }
    public Task AckAsync(string messageId, CancellationToken ct) { Interlocked.Increment(ref _processed); return Task.CompletedTask; }
    public Task DeadLetterAsync(QueuedItem item, string reason, CancellationToken ct) { _dead.Enqueue((item, reason)); return Task.CompletedTask; }
    public Task<QueueDepth> DepthAsync(CancellationToken ct)
        => Task.FromResult(new QueueDepth(_q.Count, _dead.Count, Interlocked.Read(ref _processed)));
    public Task PurgeAsync(CancellationToken ct)
    {
        _q.Clear(); _dead.Clear(); Interlocked.Exchange(ref _processed, 0);
        return Task.CompletedTask;
    }
}
