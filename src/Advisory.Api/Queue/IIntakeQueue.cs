using Advisory.Api.Models;

namespace Advisory.Api.Queue;

/// <summary>A queued evaluation request with its stream id for acknowledgement.</summary>
public record QueuedItem(string MessageId, PackageRef Package, DateTimeOffset EnqueuedAt, int DeliveryCount);

/// <summary>
/// Durable intake queue abstraction. Kafka-like semantics (ordered, consumer-group,
/// at-least-once with explicit ack, replayable, dead-letter) behind one interface so the
/// backend (Redis Streams now; Redpanda/Kafka later) is swappable without touching the gate.
/// Decouples enqueue (instant, dev never waits) from evaluation (async consumer).
/// </summary>
public interface IIntakeQueue
{
    Task<string> EnqueueAsync(PackageRef pkg, CancellationToken ct);
    Task<IReadOnlyList<QueuedItem>> ReadAsync(int max, CancellationToken ct);
    Task AckAsync(string messageId, CancellationToken ct);
    Task DeadLetterAsync(QueuedItem item, string reason, CancellationToken ct);
    Task<QueueDepth> DepthAsync(CancellationToken ct);
    bool IsConfigured { get; }
}

public record QueueDepth(long Pending, long DeadLettered, long Processed);
