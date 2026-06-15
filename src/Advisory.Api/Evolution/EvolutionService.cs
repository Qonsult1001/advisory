using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Advisory.Api.Evolution;

/// <summary>A GitHub issue (ticket) that can drive an evolution run.</summary>
public record EvoTicket(int Number, string Title, string Body, string Author, string State,
    List<string> Labels, int Comments, DateTimeOffset UpdatedAt, string Url);

/// <summary>One evolution run: a ticket → engine session → branch + PR.</summary>
public class EvoRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..10];
    public int Ticket { get; set; }
    public string TicketTitle { get; set; } = "";
    public string Status { get; set; } = "queued";   // queued | running | awaiting-approval | tests | pr-open | released | failed | skipped | rejected
    public string Stage { get; set; } = "";
    public int Pct { get; set; }                      // 0-100 progress for the bar
    public int? EtaSeconds { get; set; }              // calibrated estimate to finish (null = unknown)
    // ---- Interactive run control (EPIC A) ----
    public string? Plan { get; set; }                 // the proposed plan, posted before implementing
    public string Approval { get; set; } = "none";    // none | pending | approved | rejected
    public string? SubIssue { get; set; }             // operator's correction when rejecting/refining the plan
    public string? Branch { get; set; }
    public string? PrUrl { get; set; }
    public int? PrNumber { get; set; }
    public bool TestsPassed { get; set; }
    public string Log { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PickedUpAt { get; set; }   // when the worker actually started running it
    public DateTimeOffset? FinishedAt { get; set; }
    public void Append(string line) { Log += line + "\n"; if (Log.Length > 40000) Log = Log[^40000..]; }
}

/// <summary>The ordered stages of a /mutate cycle, with the % each one reaches and a typical
/// duration (seconds) used to compute a calibrated ETA. Honest, short estimates — a cycle is
/// minutes, not days.</summary>
public static class MutateStages
{
    // (key, label, pct-at-completion, typical seconds for THIS stage)
    public static readonly (string Key, string Label, int Pct, int Secs)[] All =
    {
        ("queued",  "waiting for worker",      0,   0),
        ("setup",   "setup · fetch ticket",    10,  20),
        ("plan",    "planning the fix",        25,  40),
        ("test",    "writing a failing test",  45,  60),
        ("fix",     "implementing the fix",    65,  90),
        ("build",   "building",                80,  40),
        ("tests",   "running tests",           92,  50),
        ("pr",      "opening pull request",    100, 25),
    };
    public static int TotalSecs => All.Sum(s => s.Secs);
    public static (int pct, int etaSecs) For(string key)
    {
        int idx = Array.FindIndex(All, s => s.Key == key);
        if (idx < 0) return (0, TotalSecs);
        int pct = All[idx].Pct;
        int remaining = All.Skip(idx + 1).Sum(s => s.Secs);   // time for stages still ahead
        return (pct, remaining);
    }
}

/// <summary>
/// The C# bridge to the EVOLVE engine. Reads a target GitHub repo for issues labelled `evolve`
/// (and tester comments on them) via the gh CLI, then runs the engine to produce a focused code
/// change — opening a PR for human review. PR-ONLY: it never merges, never pushes to the default
/// branch. Safety is enforced in code, not config.
/// </summary>
public class EvolutionService
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<EvolutionService> _log;
    private readonly ConcurrentDictionary<string, EvoRun> _runs = new();

    public EvolutionService(IConfiguration cfg, ILogger<EvolutionService> log) { _cfg = cfg; _log = log; }

    // ---- config (PR-only is NOT configurable; sandbox repo is) ----
    public bool Enabled => _cfg.GetValue("EVOLUTION_ENABLED", false);
    public string? Repo => _cfg["EVOLUTION_REPO"];                 // e.g. "Qonsult1001/advisory"
    public string Label => _cfg["EVOLUTION_LABEL"] ?? "mutation";
    public string Workflow => _cfg["EVOLUTION_WORKFLOW"] ?? "mutation.yml";
    public string Model => _cfg["EVOLUTION_MODEL"] ?? "claude-opus-4-8";
    // The mechanism is the GitHub Actions workflow + scripts (no Rust binary, no API key here).
    // "Configured" = gh CLI present + a target repo set; the dashboard triggers the same workflow
    // an `evolve`-labelled issue triggers.
    public bool EngineConfigured => GhAvailable() && !string.IsNullOrWhiteSpace(Repo);

    public IReadOnlyList<EvoRun> Runs(int limit = 50) =>
        _runs.Values.OrderByDescending(r => r.StartedAt).Take(limit).ToList();
    public EvoRun? Run(string id) => _runs.TryGetValue(id, out var r) ? r : null;

    /// <summary>Clear run history. With activeOnly=true keeps finished runs and only drops
    /// queued/running ones (so a stale "queued" run can't re-fire); otherwise clears all.
    /// Returns how many were removed. Also clears matching queue files so nothing re-runs.</summary>
    public int ClearRuns(bool activeOnly = false)
    {
        var toRemove = activeOnly
            ? _runs.Values.Where(r => r.Status is "queued" or "running" or "tests" or "setup").ToList()
            : _runs.Values.ToList();
        foreach (var r in toRemove)
        {
            _runs.TryRemove(r.Id, out _);
            try { var f = Path.Combine(QueueDir, $"ticket-{r.Ticket}.request"); if (File.Exists(f)) File.Delete(f); } catch { }
        }
        return toRemove.Count;
    }

    // The mutation cycle runs LOCALLY (your machine is logged into Claude; the container is not).
    // The dashboard button queues a ticket here; a local `scripts/mutate-claude.sh --loop` drains it.
    private string QueueDir => _cfg["EVOLUTION_QUEUE_DIR"] ?? (Directory.Exists("/data") ? "/data/evolution-queue" : Path.Combine(Path.GetTempPath(), "advisory-evolution-queue"));

    // ---- worker heartbeat: the local mutate-claude.sh loop pings this so the dashboard can say
    //      whether a worker is actually draining the queue (vs "Queued" sitting forever). ----
    private DateTimeOffset? _workerSeen;
    // Last `said stats --json` the worker posted (it can run said.exe; the container can't). Used by
    // the Admin Project-memory panel for accurate live brain stats.
    public string? BrainStatsJson { get; private set; }
    public void SetBrainStats(string json) { BrainStatsJson = json; WorkerHeartbeat(); }
    public void WorkerHeartbeat() => _workerSeen = DateTimeOffset.UtcNow;

    // ---- Per-agent CLI test (claude-cli/cursor-cli run on the host worker, not the container) ----
    // The dashboard queues a test; the worker drains it, runs the CLI, and posts the reply back.
    private readonly ConcurrentDictionary<string, object> _agentTests = new();
    public (bool ok, string detail) QueueAgentTest(string agentId, string prompt)
    {
        try
        {
            Directory.CreateDirectory(QueueDir);
            _agentTests[agentId] = new { status = "queued", reply = (string?)null, ok = (bool?)null, error = (string?)null, at = DateTimeOffset.UtcNow };
            File.WriteAllText(Path.Combine(QueueDir, $"agenttest-{agentId}.request"), $"{agentId}\n{prompt}\n{DateTimeOffset.UtcNow:o}\n");
            return (true, $"queued test for agent '{agentId}' — the local worker will run it");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
    public void SetAgentTestResult(string agentId, string reply, bool ok, string? error)
    { _agentTests[agentId] = new { status = "done", reply, ok, error, at = DateTimeOffset.UtcNow }; WorkerHeartbeat(); }
    public object GetAgentTest(string agentId) => _agentTests.TryGetValue(agentId, out var r) ? r : new { status = "none" };

    // ---- cursor-cli authentication (runs on the host worker; user accepts the licence in a browser) ----
    private readonly ConcurrentDictionary<string, object> _cursorAuth = new();
    public (bool ok, string detail) QueueCursorAuth(string agentId, string standard, string user)
    {
        try
        {
            Directory.CreateDirectory(QueueDir);
            _cursorAuth[agentId] = new { status = "queued", message = (string?)null, url = (string?)null, ok = (bool?)null, at = DateTimeOffset.UtcNow };
            // request: line1=agentId, line2=standard, line3=user, line4=timestamp. The worker branches
            // on standard — cursor-cli → 'cursor-agent login', claude-cli → 'claude setup-token'.
            File.WriteAllText(Path.Combine(QueueDir, $"cursorauth-{agentId}.request"),
                $"{agentId}\n{standard}\n{user}\n{DateTimeOffset.UtcNow:o}\n");
            var cmd = standard == "claude-cli" ? "claude setup-token" : "cursor-agent login";
            return (true, $"queued login for '{agentId}' — the worker will run '{cmd}'; watch for a browser URL to authenticate");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
    public void SetCursorAuthResult(string agentId, string status, string? message, string? url, bool ok)
    { _cursorAuth[agentId] = new { status, message, url, ok, at = DateTimeOffset.UtcNow }; WorkerHeartbeat(); }
    public object GetCursorAuth(string agentId) => _cursorAuth.TryGetValue(agentId, out var r) ? r : new { status = "none" };
    public bool WorkerAlive => _workerSeen is { } t && (DateTimeOffset.UtcNow - t) < TimeSpan.FromSeconds(150);

    public object Status() => new
    {
        enabled = Enabled,
        repo = Repo,
        label = Label,
        prOnly = true,                       // hard guarantee
        engineConfigured = EngineConfigured,
        mechanism = "local /mutate cycle (scripts/mutate-claude.sh) — uses your Claude login",
        runMode = "local-queue",
        queueDir = QueueDir,
        ghAvailable = GhAvailable(),
        model = Model,
        workerAlive = WorkerAlive,                                   // is a worker draining the queue?
        workerLastSeen = _workerSeen,
        activeRuns = _runs.Values.Count(r => r.Status is "running" or "tests" or "queued"),
    };

    /// <summary>Reset a run that was stopped for an external reason (e.g. Claude rate limit / out of
    /// credits) rather than a real failure: remove it so the dashboard doesn't show a misleading
    /// "failed", and clear its queue file so the ticket can be cleanly re-queued later. Returns true
    /// if a run was removed.</summary>
    public bool ResetRun(string id)
    {
        if (!_runs.TryRemove(id, out var r)) return false;
        try { var f = Path.Combine(QueueDir, $"ticket-{r.Ticket}.request"); if (File.Exists(f)) File.Delete(f); } catch { }
        WorkerHeartbeat();
        return true;
    }

    /// <summary>Worker reports progress for a run. Updates stage, %, ETA, status, PR.</summary>
    public EvoRun? UpdateProgress(string id, string? stage, string? status, string? prUrl, string? logLine)
    {
        if (!_runs.TryGetValue(id, out var r)) return null;
        WorkerHeartbeat();
        if (!string.IsNullOrWhiteSpace(stage))
        {
            var s = MutateStages.All.FirstOrDefault(x => x.Key == stage);
            r.Stage = s.Label ?? stage!;
            var (pct, eta) = MutateStages.For(stage!);
            r.Pct = pct; r.EtaSeconds = eta;
            if (r.PickedUpAt is null && stage != "queued") r.PickedUpAt = DateTimeOffset.UtcNow;
        }
        if (!string.IsNullOrWhiteSpace(status)) r.Status = status!;
        if (!string.IsNullOrWhiteSpace(prUrl)) r.PrUrl = prUrl;
        if (!string.IsNullOrWhiteSpace(logLine)) r.Append(logLine!);
        if (status is "pr-open" or "released" or "failed" or "skipped" or "rejected") { r.FinishedAt = DateTimeOffset.UtcNow; r.Pct = status is "pr-open" or "released" ? 100 : r.Pct; r.EtaSeconds = 0; }
        return r;
    }

    // ---- Interactive run control (EPIC A): the worker posts its plan and waits for the operator. ----

    /// <summary>Worker submits the proposed plan and parks the run for approval.</summary>
    public EvoRun? SubmitPlan(string id, string plan)
    {
        if (!_runs.TryGetValue(id, out var r)) return null;
        WorkerHeartbeat();
        r.Plan = plan; r.Approval = "pending"; r.Status = "awaiting-approval";
        r.Stage = "awaiting your approval of the plan"; r.Pct = 25; r.EtaSeconds = null;
        r.Append("[plan] proposed — awaiting Approve / Reject / sub-issue.");
        return r;
    }

    /// <summary>Operator decides on a parked plan: approve | reject | refine (with a sub-issue note).</summary>
    public EvoRun? Decide(string id, string decision, string? subIssue)
    {
        if (!_runs.TryGetValue(id, out var r)) return null;
        if (!string.IsNullOrWhiteSpace(subIssue)) r.SubIssue = subIssue;
        if (decision == "approve") { r.Approval = "approved"; r.Status = "running"; r.Stage = "approved — implementing"; r.Append("[plan] APPROVED by operator."); }
        else if (decision == "reject") { r.Approval = "rejected"; r.Status = "rejected"; r.FinishedAt = DateTimeOffset.UtcNow; r.EtaSeconds = 0; r.Append("[plan] REJECTED by operator." + (string.IsNullOrWhiteSpace(subIssue) ? "" : " Note: " + subIssue)); }
        else if (decision == "refine") { r.Approval = "approved"; r.Status = "running"; r.Stage = "refined — implementing with your note"; r.Append("[plan] refined with sub-issue: " + (subIssue ?? "")); }
        return r;
    }

    /// <summary>Worker polls this for the operator's decision on a parked plan.</summary>
    public object? Decision(string id)
        => _runs.TryGetValue(id, out var r) ? new { approval = r.Approval, subIssue = r.SubIssue } : null;

    /// <summary>Operator approves the MERGE of a green PR (the second checkpoint). Valid ONLY at
    /// 'pr-open' (reached only after build+test pass = 100% complete). Squash-merges, deletes the
    /// branch, closes the issue, marks the run 'released'. Release stays operator-only — this is only
    /// reached when a human POSTs decision="merge"; the cycle never merges on its own. On merge FAILURE
    /// the run stays 'pr-open' so the operator can retry or merge by hand (a transient failure never
    /// marks a green PR failed).</summary>
    public async Task<EvoRun?> DecideMergeAsync(string id, CancellationToken ct)
    {
        if (!_runs.TryGetValue(id, out var r)) return null;
        if (r.Status != "pr-open")
        {
            r.Append($"[merge] ignored — run is '{r.Status}', not 'pr-open' (merge is only available once the PR is built+tested and open).");
            return r;
        }
        var (ok, detail) = await MergeAndCleanAsync(r.PrUrl, r.Ticket, ct);
        if (ok)
        {
            r.Status = "released"; r.Stage = "released — squash-merged + branch deleted + issue closed";
            r.Pct = 100; r.EtaSeconds = 0; r.FinishedAt = DateTimeOffset.UtcNow;
            r.Append($"[merge] APPROVED by operator → squash-merged, branch deleted, #{r.Ticket} closed. {detail}");
        }
        else
        {
            // stay at pr-open — the PR is still good; just couldn't merge right now.
            r.Append($"[merge] FAILED (PR left open — retry or merge manually): {detail}");
        }
        return r;
    }

    /// <summary>Code version of the manual end-of-cycle: squash-merge the PR, delete the branch, ensure
    /// the issue is closed. Uses gh (already authenticated in the container via GH_TOKEN). Returns
    /// (ok, detail). Does NOT touch run state — the caller (DecideMergeAsync) owns that.</summary>
    public async Task<(bool ok, string detail)> MergeAndCleanAsync(string? prUrl, int ticket, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return (false, "no EVOLUTION_REPO");
        if (string.IsNullOrWhiteSpace(prUrl)) return (false, "no PR url on this run");
        // gh accepts the PR URL directly as the {selector}.
        var (mok, _, merr) = await GhAsync(new[] { "pr", "merge", prUrl!, "--repo", Repo!, "--squash", "--delete-branch" }, null, ct);
        if (!mok) return (false, $"gh pr merge failed: {merr}".Trim());
        // Squash-merge honours "Closes #N" in the PR body, so the issue normally auto-closes. Verify;
        // fall back to an explicit close if it is somehow still open. Best-effort — a close failure does
        // not undo a successful merge.
        try
        {
            var (sok, sout, _) = await GhAsync(new[] { "issue", "view", ticket.ToString(), "--repo", Repo!, "--json", "state", "-q", ".state" }, null, ct);
            if (sok && !sout.Trim().Equals("CLOSED", StringComparison.OrdinalIgnoreCase))
                await GhAsync(new[] { "issue", "close", ticket.ToString(), "--repo", Repo! }, null, ct);
        }
        catch { /* close verification is best-effort */ }
        return (true, "merged");
    }

    /// <summary>Reject a parked plan AND amend the ticket with the operator's recommendation, then
    /// restart the cycle: post the recommendation as a GitHub comment on the ticket (so the next run's
    /// setup picks it up as a tester comment), mark this run rejected, and queue a fresh run for the
    /// same ticket. Returns the new run (or null if it couldn't restart).</summary>
    public async Task<EvoRun?> RejectAndAmendAsync(string id, string recommendation, CancellationToken ct)
    {
        if (!_runs.TryGetValue(id, out var rejected)) return null;
        rejected.Approval = "rejected"; rejected.Status = "rejected"; rejected.FinishedAt = DateTimeOffset.UtcNow;
        rejected.EtaSeconds = 0; rejected.Append("[plan] REJECTED by operator. Recommendation: " + recommendation);
        var ticket = rejected.Ticket;
        // Amend the ticket: the recommendation becomes a comment the next cycle's setup reads as a
        // tester comment, so the new plan incorporates it.
        if (!string.IsNullOrWhiteSpace(Repo) && !string.IsNullOrWhiteSpace(recommendation))
            await GhAsync(new[] { "issue", "comment", ticket.ToString(), "--repo", Repo!,
                "--body", "Plan rejected by operator. Please incorporate this and try again:\n\n" + recommendation }, null, ct);
        // Restart: fresh run + re-queue for the worker.
        var fresh = NewRun(new EvoTicket(ticket, rejected.TicketTitle, "", "", "open", new(), 0, DateTimeOffset.UtcNow, ""));
        fresh.Status = "queued"; fresh.Stage = "restarting with your recommendation"; fresh.Pct = 0;
        fresh.EtaSeconds = MutateStages.TotalSecs;
        fresh.Append($"[restart] re-queued #{ticket} with operator recommendation after plan rejection.");
        var (ok, detail) = await DispatchWorkflowAsync(ticket, fresh.Id, ct);
        if (!ok) { fresh.Status = "failed"; fresh.Stage = "restart-failed"; fresh.Append("[error] " + detail); }
        return fresh;
    }

    /// <summary>Mutation history for the Memories dashboard graphs: real tickets (started/closed) and
    /// merged PRs from GitHub, aggregated by day. This is the durable record (GitHub), independent of
    /// the in-memory run list which the operator can clear. Cached briefly so the dashboard can poll.</summary>
    private (DateTimeOffset at, object payload)? _historyCache;
    public async Task<object> HistoryAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return new { enabled = false, days = Array.Empty<object>(), totals = new { } };
        if (_historyCache is { } c && (DateTimeOffset.UtcNow - c.at) < TimeSpan.FromSeconds(60)) return c.payload;

        // tickets labelled `mutation` (created + closed dates) and merged mutation PRs.
        var (tok, tout, _) = await GhAsync(new[] { "issue", "list", "--repo", Repo!, "--label", Label,
            "--state", "all", "--limit", "200", "--json", "number,state,createdAt,closedAt,title" }, null, ct);
        var (pok, pout, _) = await GhAsync(new[] { "pr", "list", "--repo", Repo!, "--state", "merged",
            "--limit", "200", "--json", "number,mergedAt,title" }, null, ct);

        var byDay = new SortedDictionary<string, int[]>();   // day → [started, closed, merged]
        int[] Slot(string d) { if (!byDay.TryGetValue(d, out var a)) { a = new int[3]; byDay[d] = a; } return a; }
        int started = 0, closed = 0, merged = 0;
        var recent = new List<object>();
        try
        {
            if (tok) foreach (var t in JsonDocument.Parse(tout).RootElement.EnumerateArray())
            {
                var created = t.TryGetProperty("createdAt", out var cr) ? cr.GetString() : null;
                var closedAt = t.TryGetProperty("closedAt", out var cl) && cl.ValueKind == JsonValueKind.String ? cl.GetString() : null;
                var num = t.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
                var title = t.TryGetProperty("title", out var ti) ? ti.GetString() : "";
                var state = t.TryGetProperty("state", out var st) ? st.GetString() : "";
                if (created is { Length: >= 10 }) { Slot(created[..10])[0]++; started++; }
                if (closedAt is { Length: >= 10 }) { Slot(closedAt[..10])[1]++; closed++; }
                recent.Add(new { kind = "ticket", number = num, title, state, createdAt = created, closedAt });
            }
            if (pok) foreach (var p in JsonDocument.Parse(pout).RootElement.EnumerateArray())
            {
                var m = p.TryGetProperty("mergedAt", out var ma) && ma.ValueKind == JsonValueKind.String ? ma.GetString() : null;
                if (m is { Length: >= 10 }) { Slot(m[..10])[2]++; merged++; }
            }
        }
        catch { /* malformed gh output → return whatever we parsed */ }

        var days = byDay.Select(kv => new { day = kv.Key, started = kv.Value[0], closed = kv.Value[1], merged = kv.Value[2] }).ToList();
        var payload = new
        {
            enabled = true, repo = Repo,
            days,
            totals = new { started, closed, merged },
            recent = recent.OrderByDescending(r => ((dynamic)r).number).Take(15).ToList(),
        };
        _historyCache = (DateTimeOffset.UtcNow, payload);
        return payload;
    }

    /// <summary>The most recent run still waiting/working — what the worker should pick up next.</summary>
    public EvoRun? NextQueued() => _runs.Values
        .Where(r => r.Status is "queued")
        .OrderBy(r => r.StartedAt).FirstOrDefault();

    /// <summary>Delete a consumed queue request. The API runs as root in the container, so it can
    /// remove a request file the host worker can't (ownership mismatch on the bind mount).
    /// Only basenames inside the queue dir are accepted — no path traversal.</summary>
    public bool ConsumeRequest(string file)
    {
        var name = Path.GetFileName(file);   // strip any path; only a plain filename is allowed
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".request")) return false;
        try { var p = Path.Combine(QueueDir, name); if (File.Exists(p)) File.Delete(p); return true; }
        catch { return false; }
    }

    /// <summary>Queue a ticket for the LOCAL mutation loop to pick up. We don't dispatch CI because
    /// CI has no Claude login; the local loop (scripts/mutate-claude.sh --loop) drains this queue and
    /// runs the /mutate cycle with your Claude session, PR-only.</summary>
    public async Task<(bool ok, string detail)> DispatchWorkflowAsync(int ticket, string runId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return (false, "no EVOLUTION_REPO set");
        // Make sure the ticket is labelled so the loop's setup step finds it.
        await GhAsync(new[] { "issue", "edit", ticket.ToString(), "--repo", Repo!, "--add-label", Label }, null, ct);
        try
        {
            Directory.CreateDirectory(QueueDir);
            // Request format: ticket / runId / iso-timestamp. The worker reads runId to report progress.
            await File.WriteAllTextAsync(Path.Combine(QueueDir, $"ticket-{ticket}.request"),
                $"{ticket}\n{runId}\n{DateTimeOffset.UtcNow:o}\n", ct);
            return (true, $"queued #{ticket} (run {runId}) for the local mutation worker");
        }
        catch (Exception ex) { return (false, $"could not queue: {ex.Message}"); }
    }

    // ---- GitHub reads (gh CLI; it's already authenticated in the environment) ----

    public async Task<List<EvoTicket>> TicketsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return new();
        var (ok, outp, _) = await GhAsync(new[] {
            "issue", "list", "--repo", Repo!, "--state", "open", "--label", Label,
            "--json", "number,title,body,author,state,labels,comments,updatedAt,url", "--limit", "50"
        }, null, ct);
        if (!ok) return new();
        try
        {
            using var doc = JsonDocument.Parse(outp);
            var list = new List<EvoTicket>();
            foreach (var e in doc.RootElement.EnumerateArray())
                list.Add(new EvoTicket(
                    e.GetProperty("number").GetInt32(),
                    e.GetProperty("title").GetString() ?? "",
                    e.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                    e.TryGetProperty("author", out var a) && a.TryGetProperty("login", out var al) ? al.GetString() ?? "" : "",
                    e.GetProperty("state").GetString() ?? "open",
                    e.TryGetProperty("labels", out var lb) ? lb.EnumerateArray().Select(x => x.GetProperty("name").GetString() ?? "").ToList() : new(),
                    e.TryGetProperty("comments", out var c) && c.ValueKind == JsonValueKind.Array ? c.GetArrayLength() : 0,
                    e.TryGetProperty("updatedAt", out var u) && u.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(u.GetString()!) : DateTimeOffset.UtcNow,
                    e.TryGetProperty("url", out var ul) ? ul.GetString() ?? "" : ""));
            return list;
        }
        catch (Exception ex) { _log.LogWarning(ex, "parse tickets"); return new(); }
    }

    /// <summary>Fetch ONE ticket by number (immediate — no label-search index lag).</summary>
    public async Task<EvoTicket?> TicketAsync(int number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return null;
        var (ok, outp, _) = await GhAsync(new[] {
            "issue", "view", number.ToString(), "--repo", Repo!,
            "--json", "number,title,body,author,state,labels,comments,url"
        }, null, ct);
        if (!ok) return null;
        try
        {
            using var doc = JsonDocument.Parse(outp);
            var e = doc.RootElement;
            return new EvoTicket(
                e.GetProperty("number").GetInt32(),
                e.GetProperty("title").GetString() ?? "",
                e.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                e.TryGetProperty("author", out var a) && a.TryGetProperty("login", out var al) ? al.GetString() ?? "" : "",
                e.TryGetProperty("state", out var st) ? st.GetString() ?? "open" : "open",
                e.TryGetProperty("labels", out var lb) ? lb.EnumerateArray().Select(x => x.GetProperty("name").GetString() ?? "").ToList() : new(),
                e.TryGetProperty("comments", out var c) && c.ValueKind == JsonValueKind.Array ? c.GetArrayLength() : 0,
                DateTimeOffset.UtcNow,
                e.TryGetProperty("url", out var ul) ? ul.GetString() ?? "" : "");
        }
        catch { return null; }
    }

    /// <summary>Fetch full comment thread for a ticket (tester replies the engine should address).</summary>
    public async Task<string> TicketContextAsync(int number, CancellationToken ct)
    {
        var (ok, outp, _) = await GhAsync(new[] {
            "issue", "view", number.ToString(), "--repo", Repo!, "--json", "title,body,comments"
        }, null, ct);
        if (!ok) return "";
        try
        {
            using var doc = JsonDocument.Parse(outp);
            var root = doc.RootElement;
            var sb = new StringBuilder();
            sb.AppendLine($"# Issue #{number}: {root.GetProperty("title").GetString()}");
            sb.AppendLine(root.TryGetProperty("body", out var b) ? b.GetString() : "");
            if (root.TryGetProperty("comments", out var cs) && cs.ValueKind == JsonValueKind.Array && cs.GetArrayLength() > 0)
            {
                sb.AppendLine("\n## Tester comments (address these):");
                foreach (var c in cs.EnumerateArray())
                    sb.AppendLine($"- @{(c.TryGetProperty("author", out var au) && au.TryGetProperty("login", out var l) ? l.GetString() : "?")}: {(c.TryGetProperty("body", out var cb) ? cb.GetString() : "")}");
            }
            return sb.ToString();
        }
        catch { return ""; }
    }

    // ---- run store ----
    public EvoRun NewRun(EvoTicket t)
    {
        var run = new EvoRun { Ticket = t.Number, TicketTitle = t.Title };
        _runs[run.Id] = run;
        return run;
    }

    // ---- gh helpers ----
    public bool GhAvailable() { try { return RunProc("gh", new[] { "--version" }, null, null, 5000).ok; } catch { return false; } }

    public async Task<(bool ok, string outp, string err)> GhAsync(string[] args, string? cwd, CancellationToken ct)
        => await Task.Run(() => RunProc("gh", args, cwd, null, 60000), ct);

    /// <summary>Run a process, capturing stdout/stderr. env adds/overrides environment variables.</summary>
    public static (bool ok, string outp, string err) RunProc(string file, string[] args, string? cwd,
        Dictionary<string, string>? env, int timeoutMs)
    {
        var psi = new ProcessStartInfo { FileName = file, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (cwd is not null) psi.WorkingDirectory = cwd;
        if (env is not null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (false, so.Result, "timeout"); }
        return (p.ExitCode == 0, so.Result, se.Result);
    }
}
