using Microsoft.Extensions.DependencyInjection;
using Advisory.Api.Models;
using Advisory.Api.Queue;
using Xunit;

namespace Advisory.Tests;

public class QueueTests
{
    [Fact]
    public async Task Enqueue_then_read_then_ack_roundtrips()
    {
        IIntakeQueue q = new InMemoryQueue();
        var id = await q.EnqueueAsync(new PackageRef(Ecosystem.PyPI, "demo", "1.0.0"), default);
        Assert.False(string.IsNullOrEmpty(id));

        var depth = await q.DepthAsync(default);
        Assert.Equal(1, depth.Pending);

        var items = await q.ReadAsync(10, default);
        Assert.Single(items);
        Assert.Equal("demo", items[0].Package.Name);

        await q.AckAsync(items[0].MessageId, default);
        var after = await q.DepthAsync(default);
        Assert.Equal(0, after.Pending);
        Assert.Equal(1, after.Processed);
    }

    [Fact]
    public async Task Dead_letter_moves_item_off_main_queue()
    {
        IIntakeQueue q = new InMemoryQueue();
        await q.EnqueueAsync(new PackageRef(Ecosystem.npm, "poison", "0.0.1"), default);
        var items = await q.ReadAsync(10, default);
        await q.DeadLetterAsync(items[0], "simulated poison", default);
        var depth = await q.DepthAsync(default);
        Assert.Equal(1, depth.DeadLettered);
    }

    [Fact]
    public async Task Consumer_drains_queue_through_gate()
    {
        // Reuse the test gate DI from PromotionBridgeTests.
        var m = typeof(PromotionBridgeTests).GetMethod("BuildGate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public)!;
        var provider = (Microsoft.Extensions.DependencyInjection.ServiceProvider)m.Invoke(null, null)!;

        IIntakeQueue q = new InMemoryQueue();
        await q.EnqueueAsync(new PackageRef(Ecosystem.HuggingFace, "vendor/model", "main",
            null, "pytorch_model.bin"), default); // pickle => block, but still evaluated+acked

        var consumer = new IntakeConsumer(q,
            provider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IntakeConsumer>.Instance);

        using var cts = new System.Threading.CancellationTokenSource();
        await consumer.StartAsync(cts.Token);
        await Task.Delay(1500);
        cts.Cancel();

        var depth = await q.DepthAsync(default);
        Assert.Equal(0, depth.Pending);                       // drained — nothing left pending
        // The test has no live Nexus, so the quarantine fetch can't succeed. Per #159 the consumer no
        // longer silently acks a failed fetch — it dead-letters it. Either way the message is drained
        // and accounted for (processed OR dead-lettered), never silently lost.
        Assert.True(depth.Processed + depth.DeadLettered >= 1);
    }
}
