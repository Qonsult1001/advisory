using System.Collections.Concurrent;

namespace Advisory.Api.Catalog;

/// <summary>Live state of one file inside a verification job (drives the UI progress rows).</summary>
public class FileProgress
{
    public string Name { get; set; } = "";
    public string Stage { get; set; } = "pending";   // pending | head | downloading | scanning | done | error
    public int Percent { get; set; }                   // download progress 0..100
    public long Bytes { get; set; }                    // bytes downloaded so far
    public long TotalBytes { get; set; }
    public WeightVerdict? Verdict { get; set; }        // set when stage == done
}

/// <summary>A model-verification job: many files verified concurrently in the background.</summary>
public class VerificationJob
{
    public string ModelId { get; set; } = "";
    public string Status { get; set; } = "running";    // running | done | failed
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public ConcurrentDictionary<string, FileProgress> Files { get; } = new();
    public long CachedBytes { get; set; }              // bytes currently held in the on-disk cache for this model

    public object Snapshot() => new
    {
        modelId = ModelId, status = Status, startedAt = StartedAt, finishedAt = FinishedAt,
        cachedBytes = CachedBytes,
        files = Files.Values.OrderBy(f => f.Name).Select(f => new
        {
            f.Name, f.Stage, f.Percent, f.Bytes, f.TotalBytes, f.Verdict,
        }),
        summary = new
        {
            total = Files.Count,
            done = Files.Values.Count(f => f.Stage == "done"),
            confirmed = Files.Values.Count(f => f.Verdict?.Confirmed == true),
            pickleConfirmed = Files.Values.Count(f => f.Verdict is { Confirmed: true, Format: "pickle" }),
            unconfirmed = Files.Values.Count(f => f.Stage == "done" && f.Verdict?.Confirmed != true),
            malicious = Files.Values.SelectMany(f => f.Verdict?.MaliciousHits ?? new())
                .Where(h => h.Contains("DANGEROUS") || h.Contains("DYNAMIC")).Distinct().ToList(),
        },
    };
}

/// <summary>
/// Runs weight verification as a background job so the UI never blocks on multi-GB downloads.
/// Per-file progress (head → download% → scan → verdict) is observable via Snapshot(); the
/// downloaded bytes live in a persistent cache and can be evicted after a decision is made.
/// </summary>
public class VerificationJobService
{
    private readonly WeightVerifier _verifier;
    private readonly ILogger<VerificationJobService> _log;
    private readonly ConcurrentDictionary<string, VerificationJob> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public VerificationJobService(WeightVerifier verifier, ILogger<VerificationJobService> log)
    { _verifier = verifier; _log = log; }

    public VerificationJob? Get(string modelId) => _jobs.TryGetValue(modelId, out var j) ? j : null;

    /// <summary>All jobs (most recent first) — powers the global "downloads in progress" panel.</summary>
    public IEnumerable<object> All() => _jobs.Values
        .OrderByDescending(j => j.StartedAt)
        .Select(j => new
        {
            modelId = j.ModelId, status = j.Status, startedAt = j.StartedAt,
            cachedBytes = j.CachedBytes,
            total = j.Files.Count,
            done = j.Files.Values.Count(f => f.Stage == "done"),
            downloading = j.Files.Values.Count(f => f.Stage == "downloading"),
            percent = j.Files.Count == 0 ? 0 : (int)(j.Files.Values.Count(f => f.Stage == "done") * 100.0 / j.Files.Count),
        });

    /// <summary>Start (or return the existing) verification job for a model.</summary>
    public VerificationJob Start(string modelId, List<AiModelFile> files)
    {
        if (_jobs.TryGetValue(modelId, out var existing) && existing.Status == "running")
            return existing;

        var job = new VerificationJob { ModelId = modelId };
        var weightFiles = files.Where(x => x.Format is not ("config" or "other")).ToList();
        foreach (var wf in weightFiles)
            job.Files[wf.Name] = new FileProgress { Name = wf.Name };
        _jobs[modelId] = job;

        _ = Task.Run(() => RunAsync(job, modelId, files, weightFiles));
        return job;
    }

    private async Task RunAsync(VerificationJob job, string modelId, List<AiModelFile> allFiles, List<AiModelFile> weightFiles)
    {
        var xmlStems = allFiles.Where(x => x.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Name[..^4]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sem = new SemaphoreSlim(3);
        try
        {
            await Task.WhenAll(weightFiles.Select(async wf =>
            {
                await sem.WaitAsync();
                var fp = job.Files[wf.Name];
                try
                {
                    var verdict = await _verifier.VerifyFileTrackedAsync(modelId, wf, xmlStems, fp,
                        bytes => job.CachedBytes += bytes, CancellationToken.None);
                    fp.Verdict = verdict; fp.Stage = "done";
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "verify failed {Model}/{File}", modelId, wf.Name);
                    fp.Stage = "error";
                    fp.Verdict = new WeightVerdict(wf.Name, wf.Format, "inconclusive", false, ex.Message, new());
                }
                finally { sem.Release(); }
            }));
            job.Status = "done";
        }
        catch (Exception ex) { _log.LogError(ex, "verify job failed {Model}", modelId); job.Status = "failed"; }
        finally { job.FinishedAt = DateTimeOffset.UtcNow; }
    }

    /// <summary>Delete this model's cached downloads from disk (after a decision). Returns bytes freed.</summary>
    public long Evict(string modelId)
    {
        var freed = _verifier.EvictCache(modelId);
        if (_jobs.TryGetValue(modelId, out var j)) j.CachedBytes = 0;
        return freed;
    }
}
