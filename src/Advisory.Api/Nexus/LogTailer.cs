using Advisory.Api.Models;
using Advisory.Api.Queue;

namespace Advisory.Api.Nexus;

/// <summary>
/// Auto-gate-on-pull discovery. Tails Nexus's inbound request.log; when a developer's pip/npm install
/// requests a package that isn't approved yet, Nexus 404s it and logs the miss. We parse that line and
/// enqueue the package onto the existing intake queue — the IntakeConsumer + PromotionBridge then fetch,
/// scan, and promote/hold it, exactly as the manual "Send to pipeline" flow does. A retry a short time
/// later installs the now-approved package.
///
/// Nexus OSS cannot hold-and-gate a download, so this is reactive: the FIRST pull of a never-seen
/// package fails; requesting it is what triggers gating; the retry fills in. See the request.log-based
/// design (no reverse proxy, developer workflow unchanged).
///
/// Follows appends, survives daily log rotation (re-opens on truncation/inode change), and de-duplicates
/// on {ecosystem, name} within a short TTL so a 1000-dependency install enqueues each package once — the
/// PromotionBridge's own dedup is the downstream backstop.
/// </summary>
public class LogTailer : BackgroundService
{
    private readonly IIntakeQueue _queue;
    private readonly ILogger<LogTailer> _log;
    private readonly string? _path;
    private readonly TimeSpan _dedupTtl;
    private readonly TimeSpan _poll = TimeSpan.FromMilliseconds(500);

    // {eco}:{name} -> last enqueued (UTC). Bounded by TTL sweep. Not persisted: on restart we re-scan
    // from EOF, so we only ever re-enqueue live misses — and the bridge dedups those anyway.
    private readonly Dictionary<string, DateTimeOffset> _recent = new(StringComparer.OrdinalIgnoreCase);

    public LogTailer(IIntakeQueue queue, IConfiguration cfg, ILogger<LogTailer> log)
    {
        _queue = queue; _log = log;
        _path = cfg["NEXUS_REQUEST_LOG"];
        _dedupTtl = TimeSpan.FromSeconds(cfg.GetValue("NEXUS_TAIL_DEDUP_SECONDS", 300));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_path))
        {
            _log.LogInformation("LogTailer disabled (NEXUS_REQUEST_LOG not set).");
            return;
        }
        _log.LogInformation("LogTailer watching {Path} for not-yet-approved package requests.", _path);

        long offset = 0;
        // Start at end-of-file so we react to NEW misses, not the whole backlog on boot.
        try { if (File.Exists(_path)) offset = new FileInfo(_path).Length; } catch { /* first read handles it */ }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!File.Exists(_path)) { await Task.Delay(_poll, ct); continue; }

                var len = new FileInfo(_path).Length;
                if (len < offset) offset = 0;          // rotated/truncated — restart from the top of the new file.
                if (len > offset)
                {
                    foreach (var line in ReadFrom(_path, ref offset))
                        await HandleLineAsync(line, ct);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "LogTailer read error; retrying.");
            }
            await Task.Delay(_poll, ct);
        }
    }

    private static IEnumerable<string> ReadFrom(string path, ref long offset)
    {
        var lines = new List<string>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(offset, SeekOrigin.Begin);
        using var sr = new StreamReader(fs);
        string? l;
        while ((l = sr.ReadLine()) is not null) lines.Add(l);
        offset = fs.Position;
        return lines;
    }

    private async Task HandleLineAsync(string line, CancellationToken ct)
    {
        if (!RequestLogParser.TryParseMiss(line, out var pkg) || pkg is null) return;

        var key = $"{pkg.Ecosystem}:{pkg.Name}";
        var now = DateTimeOffset.UtcNow;
        if (_recent.TryGetValue(key, out var last) && now - last < _dedupTtl) return;   // seen recently — skip.
        Sweep(now);
        _recent[key] = now;

        try
        {
            await _queue.EnqueueAsync(pkg, ct);
            _log.LogInformation("Auto-gate: developer requested {Eco}:{Name} (not approved) — enqueued for gating.",
                pkg.Ecosystem, pkg.Name);
        }
        catch (Exception ex)
        {
            _recent.Remove(key);   // let a later line retry.
            _log.LogWarning(ex, "Auto-gate: enqueue failed for {Eco}:{Name}", pkg.Ecosystem, pkg.Name);
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        if (_recent.Count < 4096) return;   // cheap bound; only sweep when it grows.
        var stale = _recent.Where(kv => now - kv.Value >= _dedupTtl).Select(kv => kv.Key).ToList();
        foreach (var k in stale) _recent.Remove(k);
    }
}
