using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Advisory.Api.Evolution;

namespace Advisory.Api.Research;

/// <summary>One research finding parsed from RESEARCH.md — an enhancement candidate for a product section.</summary>
public record ResearchGap(string Id, string Title, string Section, string Goal, string? Source, bool Closed);

/// <summary>
/// The Evolution (research) loop's read model + control plane. Evolution studies the supply-chain
/// security landscape (arXiv, NIST SSDF, SLSA, competitor controls) and records findings into
/// RESEARCH.md — it NEVER edits product code. A human approves a finding here, which files a
/// `mutation` ticket that the bug-fix loop implements (PR-only). This service:
///   • parses RESEARCH.md into section-tagged gaps (AppTrust/Xray/Curation/Catalog/AI/ML/Pipeline),
///   • reports the weekly schedule + next/last run,
///   • queues a "run research now" request for the local /evolve loop,
///   • turns an approved finding into a labelled GitHub issue for /mutate.
/// </summary>
public class ResearchService
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<ResearchService> _log;
    private readonly EvolutionService _evo;   // reuse its gh + queue plumbing

    public ResearchService(IConfiguration cfg, ILogger<ResearchService> log, EvolutionService evo)
    { _cfg = cfg; _log = log; _evo = evo; }

    // Canonical product sections (must match the dashboard nav groups).
    public static readonly string[] Sections =
        { "AppTrust", "Xray", "Curation", "Catalog", "AI/ML", "Pipeline" };

    public bool Enabled => _cfg.GetValue("EVOLUTION_ENABLED", false);
    public string? Repo => _evo.Repo;
    public string Label => _cfg["EVOLUTION_LABEL"] ?? "mutation";

    // Weekly schedule: day-of-week (0=Sun) + hour, UTC. Defaults to Sunday 02:00. "Run now" always available.
    private int ScheduleDow => Math.Clamp(_cfg.GetValue("RESEARCH_SCHEDULE_DOW", 0), 0, 6);
    private int ScheduleHour => Math.Clamp(_cfg.GetValue("RESEARCH_SCHEDULE_HOUR", 2), 0, 23);
    public string ScheduleText => $"weekly · {DayName(ScheduleDow)} {ScheduleHour:00}:00 UTC";

    private string ResearchPath => _cfg["RESEARCH_PATH"]
        ?? (File.Exists("/app/RESEARCH.md") ? "/app/RESEARCH.md"
            : File.Exists("RESEARCH.md") ? "RESEARCH.md" : "/data/RESEARCH.md");
    private string QueueDir => _cfg["EVOLUTION_QUEUE_DIR"]
        ?? (Directory.Exists("/data") ? "/data/evolution-queue" : Path.Combine(Path.GetTempPath(), "advisory-evolution-queue"));

    /// <summary>Compute the next scheduled UTC run from now (the upcoming ScheduleDow/ScheduleHour).</summary>
    public DateTimeOffset NextRun(DateTimeOffset now)
    {
        var nowUtc = now.ToUniversalTime();
        int daysAhead = ((ScheduleDow - (int)nowUtc.DayOfWeek) + 7) % 7;
        var candidate = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, ScheduleHour, 0, 0, TimeSpan.Zero)
            .AddDays(daysAhead);
        if (candidate <= nowUtc) candidate = candidate.AddDays(7);   // already passed this week
        return candidate;
    }

    /// <summary>Last run = last time the local /evolve loop dropped a marker, or RESEARCH.md mtime.</summary>
    public DateTimeOffset? LastRun()
    {
        try
        {
            var marker = Path.Combine(QueueDir, "research.last");
            if (File.Exists(marker) && DateTimeOffset.TryParse(File.ReadAllText(marker).Trim(), out var t)) return t;
            if (File.Exists(ResearchPath)) return new DateTimeOffset(File.GetLastWriteTimeUtc(ResearchPath), TimeSpan.Zero);
        }
        catch { /* best effort */ }
        return null;
    }

    public List<ResearchGap> Gaps()
    {
        var list = new List<ResearchGap>();
        if (!File.Exists(ResearchPath)) return list;
        string text;
        try { text = File.ReadAllText(ResearchPath); } catch { return list; }

        // Split on "### [ ]" / "### [x]" headings, then pull Section/Goal/Source from each block.
        var matches = Regex.Matches(text, @"^###\s+\[( |x|X)\]\s+(.+)$", RegexOptions.Multiline);
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            bool closed = !m.Groups[1].Value.Equals(" ");
            var title = m.Groups[2].Value.Trim();
            int start = m.Index + m.Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var block = text[start..end];

            var section = Field(block, "Section") is { Length: > 0 } s ? Normalize(s) : "Pipeline";
            var goal = Field(block, "Goal") ?? "";
            var source = Field(block, "Source");
            var id = "rg-" + Slug(title);
            list.Add(new ResearchGap(id, title, section, goal, source, closed));
        }
        return list;
    }

    private static string? Field(string block, string name)
    {
        var m = Regex.Match(block, $@"\*\*{name}:\*\*\s*(.+?)(?=\n\*\*|\n###|\z)", RegexOptions.Singleline);
        return m.Success ? Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " ") : null;
    }

    /// <summary>Map free text to one of the canonical sections (case/format tolerant).</summary>
    private static string Normalize(string s)
    {
        var t = s.Trim();
        foreach (var sec in Sections)
            if (string.Equals(sec, t, StringComparison.OrdinalIgnoreCase)) return sec;
        // tolerate "AI", "ML", "AIML"
        if (Regex.IsMatch(t, @"(?i)\bai\b|\bml\b")) return "AI/ML";
        return Sections.FirstOrDefault(sec => t.Contains(sec, StringComparison.OrdinalIgnoreCase)) ?? "Pipeline";
    }

    public object Status() => new
    {
        enabled = Enabled,
        repo = Repo,
        kind = "research",
        purpose = "studies the supply-chain security landscape and files enhancement candidates — never edits product code",
        prOnly = true,
        runMode = "local-queue",
        mechanism = "local /evolve cycle (scripts/evolve-claude.sh) — uses your Claude login",
        schedule = ScheduleText,
        nextRun = NextRun(DateTimeOffset.UtcNow),
        lastRun = LastRun(),
        ghAvailable = _evo.GhAvailable(),
        sections = Sections,
        // backlog burn-down
        gaps = (object)Gaps().Count,
    };

    /// <summary>Queue a "run research now" request for the local /evolve loop (mirrors the mutation queue).</summary>
    public async Task<(bool ok, string detail)> RunNowAsync(string? topic, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(QueueDir);
            var safe = string.IsNullOrWhiteSpace(topic) ? "open-backlog" : Slug(topic);
            await File.WriteAllTextAsync(Path.Combine(QueueDir, $"research-{safe}.request"),
                $"{topic ?? "(pick an open RESEARCH.md gap)"}\n{DateTimeOffset.UtcNow:o}\n", ct);
            return (true, $"queued research run — run scripts/evolve-claude.sh (or --loop) to process it");
        }
        catch (Exception ex) { return (false, $"could not queue: {ex.Message}"); }
    }

    /// <summary>Approve a finding → file a `mutation` GitHub issue so the bug-fix loop can implement it.
    /// This is the bridge from research (no code) to implementation (PR-only via /mutate).</summary>
    public async Task<(bool ok, string detail, string? url)> ApproveAsync(string gapId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return (false, "no EVOLUTION_REPO set", null);
        var gap = Gaps().FirstOrDefault(g => g.Id == gapId);
        if (gap is null) return (false, $"finding {gapId} not found in RESEARCH.md", null);

        var body = new StringBuilder()
            .AppendLine($"**Enhancement approved from the Evolution research backlog.**").AppendLine()
            .AppendLine($"**Section:** {gap.Section}").AppendLine()
            .AppendLine($"**Goal:** {gap.Goal}").AppendLine();
        if (!string.IsNullOrWhiteSpace(gap.Source)) body.AppendLine($"**Source:** {gap.Source}").AppendLine();
        body.AppendLine("Implement via the mutation cycle (PR-only). Keep the change scoped to this section; do not weaken a control.");

        var (ok, outp, err) = await _evo.GhAsync(new[]
        {
            "issue", "create", "--repo", Repo!, "--title", $"[{gap.Section}] {gap.Title}",
            "--label", Label, "--body", body.ToString()
        }, null, ct);
        if (!ok) return (false, err, null);
        var url = Regex.Match(outp, @"https://\S+").Value;
        return (true, $"filed mutation ticket for “{gap.Title}”", string.IsNullOrEmpty(url) ? null : url);
    }

    private static string Slug(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-') is { Length: > 0 } v ? v[..Math.Min(v.Length, 60)] : "x";
    private static string DayName(int dow) => new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[dow];
}
