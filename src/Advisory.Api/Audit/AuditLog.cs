using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Advisory.Api.Models;

namespace Advisory.Api.Audit;

/// <summary>
/// Write-Once-Read-Many sink. Every sealed audit line is mirrored here in addition to the
/// in-process ledger. The default implementation appends to a file; swap for S3 Object Lock /
/// Splunk / Sentinel by registering a different <see cref="IWormSink"/> (see README PCI 10.5).
/// </summary>
public interface IWormSink
{
    /// <summary>Append one already-sealed (hash-chained) audit line. Must be durable + append-only.</summary>
    Task WriteAsync(string sealedLine);
}

/// <summary>Default WORM sink: append-only file at <c>WormPath</c> (default <c>worm.log</c>).</summary>
public sealed class FileWormSink : IWormSink
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileWormSink(IConfiguration config)
        => _path = config["WormPath"] ?? "worm.log";

    public async Task WriteAsync(string sealedLine)
    {
        await _gate.WaitAsync();
        try
        {
            AppendShared(_path, sealedLine);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Append a line opening the file with <see cref="FileShare.ReadWrite"/> so concurrent writers
    /// (e.g. parallel hosts, multiple workers) don't fail with a sharing violation. Seeks to end
    /// before each write, so interleaved appends stay well-formed.
    /// </summary>
    internal static void AppendShared(string path, string line)
    {
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
        fs.Write(bytes, 0, bytes.Length);
    }
}

public interface IAuditLog
{
    /// <summary>Seal an entry into the hash-chained ledger and mirror it to the WORM sink.</summary>
    Task AppendAsync(AuditEntry entry);

    /// <summary>Most-recent-first view of the ledger, optionally filtered by decision.</summary>
    IReadOnlyList<AuditEntry> Query(GateDecision? decision, int limit);
}

/// <summary>
/// Append-only, hash-chained decision ledger. Each persisted line carries the SHA-256 of the
/// previous sealed line, so any edit or deletion breaks the chain and is detectable. Every entry
/// is also mirrored to a pluggable <see cref="IWormSink"/> for tamper-resistant retention.
/// Persists to <c>AuditPath</c> (default <c>audit.log</c>); reloads the chain on startup.
/// </summary>
public sealed class AuditLog : IAuditLog
{
    private const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly string _path;
    private readonly IWormSink _worm;
    private readonly object _lock = new();
    private readonly List<AuditEntry> _entries = new();
    private string _prevHash = Genesis;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public AuditLog(IConfiguration config, IWormSink worm)
    {
        _path = config["AuditPath"] ?? "audit.log";
        _worm = worm;
        Reload();
    }

    public async Task AppendAsync(AuditEntry entry)
    {
        string line;
        lock (_lock)
        {
            line = Seal(entry, _prevHash);
            _prevHash = HashOf(line);
            _entries.Add(entry);
            // Local persistence is part of the chain; WORM mirror is the immutable copy.
            FileWormSink.AppendShared(_path, line);
        }
        await _worm.WriteAsync(line);
    }

    public IReadOnlyList<AuditEntry> Query(GateDecision? decision, int limit)
    {
        if (limit <= 0) limit = 200;
        lock (_lock)
        {
            IEnumerable<AuditEntry> q = ((IEnumerable<AuditEntry>)_entries).Reverse();
            if (decision is { } d) q = q.Where(e => e.Decision == d);
            return q.Take(limit).ToList();
        }
    }

    // --- chain mechanics ---

    /// <summary>One sealed ledger line: {"prev":<hash>,"entry":<json>}. The prev field binds it to its predecessor.</summary>
    private static string Seal(AuditEntry entry, string prevHash)
    {
        var payload = JsonSerializer.Serialize(entry, Json);
        var sealedLine = new SealedLine(prevHash, JsonSerializer.Deserialize<JsonElement>(payload, Json));
        return JsonSerializer.Serialize(sealedLine, Json);
    }

    private static string HashOf(string line)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(line))).ToLowerInvariant();

    private void Reload()
    {
        if (!File.Exists(_path)) return;
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var sealedLine = JsonSerializer.Deserialize<SealedLine>(line, Json);
                if (sealedLine is null) continue;
                var entry = sealedLine.Entry.Deserialize<AuditEntry>(Json);
                if (entry is null) continue;
                _entries.Add(entry);
                _prevHash = HashOf(line);
            }
            catch (JsonException)
            {
                // A corrupt line means the chain is broken from here on; stop replaying.
                break;
            }
        }
    }

    private sealed record SealedLine(
        [property: JsonPropertyName("prev")] string Prev,
        [property: JsonPropertyName("entry")] JsonElement Entry);
}
