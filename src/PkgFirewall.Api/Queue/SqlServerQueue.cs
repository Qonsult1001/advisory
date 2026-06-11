using System.Text.Json;
using Microsoft.Data.SqlClient;
using PkgFirewall.Api.Models;

namespace PkgFirewall.Api.Queue;

/// <summary>
/// Durable SQL Server intake queue. Runs under the bank's existing DB change-control,
/// backups and DR — no new infrastructure. The queue table doubles as an audit artifact.
///
/// Concurrency: workers claim rows atomically with
///   UPDATE TOP(n) ... WITH (READPAST, UPDLOCK, ROWLOCK) ... OUTPUT
/// which is the SQL Server equivalent of SELECT ... FOR UPDATE SKIP LOCKED — multiple
/// workers pull disjoint batches without blocking each other. Poison messages move to
/// status 'dead' after MaxRetries. Table auto-created on startup.
/// </summary>
public class SqlServerQueue : IIntakeQueue
{
    private readonly string? _cs;
    private readonly ILogger<SqlServerQueue> _log;
    private const int MaxRetries = 5;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_cs);

    public SqlServerQueue(IConfiguration cfg, ILogger<SqlServerQueue> log)
    {
        _cs = cfg["SQL_CONNECTION_STRING"];
        _log = log;
        if (IsConfigured) { try { EnsureSchema(); } catch (Exception ex) { _log.LogError(ex, "queue schema init failed"); } }
    }

    private void EnsureSchema()
    {
        using var con = new SqlConnection(_cs); con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
IF OBJECT_ID('dbo.IntakeQueue','U') IS NULL
CREATE TABLE dbo.IntakeQueue(
    MessageId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    PackageJson  NVARCHAR(MAX) NOT NULL,
    Status       VARCHAR(16)   NOT NULL DEFAULT 'pending',   -- pending|processing|done|dead
    DeliveryCount INT          NOT NULL DEFAULT 0,
    LastError    NVARCHAR(1024) NULL,
    EnqueuedAt   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    ClaimedAt    DATETIME2     NULL,
    CompletedAt  DATETIME2     NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_IntakeQueue_Status')
CREATE INDEX IX_IntakeQueue_Status ON dbo.IntakeQueue(Status, MessageId);";
        cmd.ExecuteNonQuery();
    }

    public async Task<string> EnqueueAsync(PackageRef pkg, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("SQL queue not configured");
        await using var con = new SqlConnection(_cs); await con.OpenAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO dbo.IntakeQueue(PackageJson) OUTPUT INSERTED.MessageId VALUES(@p);";
        cmd.Parameters.AddWithValue("@p", JsonSerializer.Serialize(pkg));
        var id = await cmd.ExecuteScalarAsync(ct);
        return id!.ToString()!;
    }

    public async Task<IReadOnlyList<QueuedItem>> ReadAsync(int max, CancellationToken ct)
    {
        if (!IsConfigured) return Array.Empty<QueuedItem>();
        await using var con = new SqlConnection(_cs); await con.OpenAsync(ct);
        await using var cmd = con.CreateCommand();
        // Atomic claim: grab up to @n pending rows, skip rows locked by other workers,
        // flip them to 'processing', and return them in one statement.
        cmd.CommandText = @"
UPDATE TOP(@n) q WITH (READPAST, UPDLOCK, ROWLOCK)
SET Status='processing', ClaimedAt=SYSUTCDATETIME(), DeliveryCount=DeliveryCount+1
OUTPUT INSERTED.MessageId, INSERTED.PackageJson, INSERTED.EnqueuedAt, INSERTED.DeliveryCount
FROM dbo.IntakeQueue q
WHERE q.Status='pending'
   OR (q.Status='processing' AND q.ClaimedAt < DATEADD(SECOND,-60,SYSUTCDATETIME()));"; // reclaim stuck
        cmd.Parameters.AddWithValue("@n", max);

        var items = new List<QueuedItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var pkg = JsonSerializer.Deserialize<PackageRef>(r.GetString(1))!;
            items.Add(new QueuedItem(r.GetInt64(0).ToString(), pkg,
                new DateTimeOffset(r.GetDateTime(2), TimeSpan.Zero), r.GetInt32(3)));
        }
        return items;
    }

    public async Task AckAsync(string messageId, CancellationToken ct)
    {
        if (!IsConfigured) return;
        await Exec("UPDATE dbo.IntakeQueue SET Status='done', CompletedAt=SYSUTCDATETIME() WHERE MessageId=@id;",
            messageId, null, ct);
    }

    public async Task DeadLetterAsync(QueuedItem item, string reason, CancellationToken ct)
    {
        if (!IsConfigured) return;
        await Exec("UPDATE dbo.IntakeQueue SET Status='dead', LastError=@err, CompletedAt=SYSUTCDATETIME() WHERE MessageId=@id;",
            item.MessageId, reason, ct);
        _log.LogWarning("DEAD-LETTER {Pkg}: {Reason}", item.Package.Name, reason);
    }

    /// <summary>Return a claimed-but-failed row to 'pending' for retry, unless it has exhausted retries.</summary>
    public async Task RetryOrDeadAsync(QueuedItem item, string reason, CancellationToken ct)
    {
        if (!IsConfigured) return;
        if (item.DeliveryCount >= MaxRetries) { await DeadLetterAsync(item, reason, ct); return; }
        await Exec("UPDATE dbo.IntakeQueue SET Status='pending', LastError=@err WHERE MessageId=@id;",
            item.MessageId, reason, ct);
    }

    public async Task<QueueDepth> DepthAsync(CancellationToken ct)
    {
        if (!IsConfigured) return new QueueDepth(0, 0, 0);
        await using var con = new SqlConnection(_cs); await con.OpenAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT
            SUM(CASE WHEN Status IN('pending','processing') THEN 1 ELSE 0 END),
            SUM(CASE WHEN Status='dead' THEN 1 ELSE 0 END),
            SUM(CASE WHEN Status='done' THEN 1 ELSE 0 END) FROM dbo.IntakeQueue;";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
            return new QueueDepth(
                r.IsDBNull(0) ? 0 : r.GetInt32(0),
                r.IsDBNull(1) ? 0 : r.GetInt32(1),
                r.IsDBNull(2) ? 0 : r.GetInt32(2));
        return new QueueDepth(0, 0, 0);
    }

    public int MaxDeliveryThreshold => MaxRetries;

    private async Task Exec(string sql, string id, string? err, CancellationToken ct)
    {
        await using var con = new SqlConnection(_cs); await con.OpenAsync(ct);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", long.Parse(id));
        if (err is not null) cmd.Parameters.AddWithValue("@err", err.Length > 1000 ? err[..1000] : err);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
