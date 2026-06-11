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
    public string Status { get; set; } = "queued";   // queued | running | tests | pr-open | failed | skipped
    public string Stage { get; set; } = "";
    public string? Branch { get; set; }
    public string? PrUrl { get; set; }
    public int? PrNumber { get; set; }
    public bool TestsPassed { get; set; }
    public string Log { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public void Append(string line) { Log += line + "\n"; if (Log.Length > 40000) Log = Log[^40000..]; }
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
    public string Label => _cfg["EVOLUTION_LABEL"] ?? "evolve";
    public string Workflow => _cfg["EVOLUTION_WORKFLOW"] ?? "evolve.yml";
    public string Model => _cfg["EVOLUTION_MODEL"] ?? "claude-opus-4-8";
    // The mechanism is the GitHub Actions workflow + scripts (no Rust binary, no API key here).
    // "Configured" = gh CLI present + a target repo set; the dashboard triggers the same workflow
    // an `evolve`-labelled issue triggers.
    public bool EngineConfigured => GhAvailable() && !string.IsNullOrWhiteSpace(Repo);

    public IReadOnlyList<EvoRun> Runs(int limit = 50) =>
        _runs.Values.OrderByDescending(r => r.StartedAt).Take(limit).ToList();
    public EvoRun? Run(string id) => _runs.TryGetValue(id, out var r) ? r : null;

    public object Status() => new
    {
        enabled = Enabled,
        repo = Repo,
        label = Label,
        prOnly = true,                       // hard guarantee
        engineConfigured = EngineConfigured,
        mechanism = "GitHub Actions workflow + scripts/evolve-*.sh (Claude Code)",
        workflow = Workflow,
        ghAvailable = GhAvailable(),
        model = Model,
        activeRuns = _runs.Values.Count(r => r.Status is "running" or "tests" or "queued"),
    };

    /// <summary>Trigger the evolution workflow for a ticket — the SAME workflow an `evolve`-labelled
    /// issue fires. This is how the dashboard's "Evolve" button reaches the GitHub event.</summary>
    public async Task<(bool ok, string detail)> DispatchWorkflowAsync(int ticket, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return (false, "no EVOLUTION_REPO set");
        // Ensure the ticket carries the label, then dispatch the workflow.
        await GhAsync(new[] { "issue", "edit", ticket.ToString(), "--repo", Repo!, "--add-label", Label }, null, ct);
        var (ok, _, err) = await GhAsync(new[] { "workflow", "run", Workflow, "--repo", Repo! }, null, ct);
        return ok ? (true, $"dispatched {Workflow} for #{ticket}") : (false, err);
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
