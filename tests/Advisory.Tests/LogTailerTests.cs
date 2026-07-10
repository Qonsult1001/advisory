using Advisory.Api.Models;
using Advisory.Api.Nexus;
using Advisory.Api.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the auto-gate-on-pull tailer: appended 404 lines for not-yet-approved packages are enqueued,
/// exactly once per {ecosystem,name} within the dedup window, and non-miss lines are ignored.
/// </summary>
public class LogTailerTests
{
    // Minimal in-test queue that records what was enqueued.
    private sealed class CaptureQueue : IIntakeQueue
    {
        public readonly List<PackageRef> Enqueued = new();
        public Task<string> EnqueueAsync(PackageRef pkg, CancellationToken ct)
        { lock (Enqueued) Enqueued.Add(pkg); return Task.FromResult(Guid.NewGuid().ToString()); }
        public Task<IReadOnlyList<QueuedItem>> ReadAsync(int max, CancellationToken ct) => Task.FromResult((IReadOnlyList<QueuedItem>)Array.Empty<QueuedItem>());
        public Task AckAsync(string messageId, CancellationToken ct) => Task.CompletedTask;
        public Task DeadLetterAsync(QueuedItem item, string reason, CancellationToken ct) => Task.CompletedTask;
        public Task<QueueDepth> DepthAsync(CancellationToken ct) => Task.FromResult(new QueueDepth(0, 0, 0));
        public Task PurgeAsync(CancellationToken ct) => Task.CompletedTask;
        public bool IsConfigured => true;
    }

    private static LogTailer Make(string path, IIntakeQueue q)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NEXUS_REQUEST_LOG"] = path,
            ["NEXUS_TAIL_DEDUP_SECONDS"] = "300",
            ["ScanIndexPath"] = Path.Combine(Path.GetTempPath(), $"scans-{Guid.NewGuid():N}.json"),
        }).Build();
        var scans = new Advisory.Api.Scan.ScanStore(cfg, null!, Array.Empty<Advisory.Api.Resolve.IDependencyResolver>());
        return new LogTailer(q, scans, cfg, NullLogger<LogTailer>.Instance);
    }

    private static string Miss(string name) =>
        $"10.0.0.1 - - [10/Jul/2026:07:14:40 +0000] \"GET /repository/pypi-approved/simple/{name}/ HTTP/1.1\" 404 - 0 1 \"pip/24.0\" [q]";

    [Fact]
    public async Task Enqueues_new_misses_and_dedups_repeats()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "");  // start empty; tailer begins at EOF.
            var q = new CaptureQueue();
            var tailer = Make(path, q);
            using var cts = new CancellationTokenSource();
            var run = tailer.StartAsync(cts.Token);

            await Task.Delay(300);  // let it seek to EOF.
            // Append: two distinct packages, plus a duplicate of the first, plus a 200 (ignored).
            File.AppendAllText(path, Miss("requests") + "\n");
            File.AppendAllText(path, Miss("flask") + "\n");
            File.AppendAllText(path, Miss("requests") + "\n");  // dup — must NOT enqueue again.
            File.AppendAllText(path, "10.0.0.1 - - [x] \"GET /repository/pypi-approved/simple/six/ HTTP/1.1\" 200 - 0 1 \"pip\" [q]\n");
            await Task.Delay(1200);  // let the tailer poll + process.

            await cts.CancelAsync();
            try { await run; } catch { }

            var names = q.Enqueued.Select(p => p.Name).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "flask", "requests" }, names);  // each once; dup + 200 excluded.
            Assert.All(q.Enqueued, p => Assert.Equal(Ecosystem.PyPI, p.Ecosystem));
        }
        finally { File.Delete(path); }
    }
}
