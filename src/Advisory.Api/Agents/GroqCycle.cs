using System.Text;
using System.Text.Json;
using Advisory.Api.Evolution;
using Advisory.Api.Policy;

namespace Advisory.Api.Agents;

/// <summary>
/// API-NATIVE mutation cycle for API-standard agents (Groq/OpenAI/Anthropic). Runs entirely inside the
/// container — NO local worker, NO WSL — so an all-Groq cycle is fast and self-contained:
///   1. fetch the ticket (gh, by number)
///   2. Groq PLANS → park for operator approval (same checkpoint as the worker cycle)
///   3. on approve: Groq EMITS the change as structured JSON (files + tests)
///   4. the container clones the repo to a WRITABLE temp dir (the /workspace mount is read-only by
///      design), applies the edits, runs build + tests, commits, pushes the branch, opens a PR
///   5. report the PR
/// The read-only /workspace mount is never modified — all writes happen in the throwaway clone, so the
/// security boundary holds. gh is already authenticated in the container (GH_TOKEN).
/// </summary>
public sealed class GroqCycle(IAgentRunner runner, IPolicyStore policy, EvolutionService evo,
    IConfiguration cfg, ILogger<GroqCycle> log)
{
    string? Repo => cfg["EVOLUTION_REPO"];
    string DefaultBranch => cfg["EVOLUTION_DEFAULT_BRANCH"] ?? "main";
    string SaidBin => cfg["SAID_BIN"] ?? "/app/said";
    string SaidFile => cfg["SAID_FILE"] ?? "/app/Advisory.said";

    /// <summary>RECALL from the .said brain — token-efficient, only what the change needs (not whole
    /// files). Runs the Linux said binary baked into the image. Returns "" if said isn't available.</summary>
    public string SaidRecall(string subcmd, string query)
    {
        if (!File.Exists(SaidBin) || !File.Exists(SaidFile)) return "";
        try
        {
            var (ok, outp, _) = EvolutionService.RunProc(SaidBin,
                new[] { subcmd, query, "--path", SaidFile, "--json" }, null, null, 15000);
            return ok ? outp : "";
        }
        catch { return ""; }
    }

    /// <summary>Push a memory back to .said after a change — "JSON output pushed to .said" so future
    /// cycles recall it. Best-effort; never blocks the cycle.</summary>
    public void SaidRemember(string note)
    {
        if (!File.Exists(SaidBin) || !File.Exists(SaidFile)) return;
        try { EvolutionService.RunProc(SaidBin, new[] { "remember", note, "--path", SaidFile }, null, null, 15000); }
        catch { }
    }

    /// <summary>One file the model wants to create/replace, with full new content.</summary>
    public record FileEdit(string path, string content);
    public record ChangeSet(string summary, List<FileEdit> files);

    /// <summary>Resolve the agent assigned to the execution phase (must be an API agent, not CLI).</summary>
    public AiAgent? ExecutionAgent()
    {
        var admin = policy.Current.Admin;
        var id = admin.MutationRouting.Execution;
        var a = admin.Agents.FirstOrDefault(x => x.Id == id && x.Enabled)
                ?? admin.Agents.FirstOrDefault(x => x.Standard is "openai" or "anthropic" && x.Enabled);
        return a is { Standard: "openai" or "anthropic" } ? a : null;
    }

    /// <summary>Phase 1: ask Groq for a concise plan for the ticket. Returns the plan text.</summary>
    public async Task<string> PlanAsync(AiAgent agent, int ticket, string title, string body, CancellationToken ct)
    {
        var rr = await runner.RunAsync(agent, new AgentRunRequest("planning", agent.Persona ?? "",
            "You are planning ONE small, correct code change for a .NET 10 + React repo (Advisory). " +
            "Output a short plan: the single endpoint/behaviour to add, the file(s) to touch, and the test(s). " +
            "Keep it minimal and PR-only. Do NOT write code yet — just the plan.",
            $"Ticket #{ticket}: {title}\n\n{body}"), ct);
        return rr.Ok ? rr.Text : $"(planning failed: {rr.Error})";
    }

    /// <summary>Phase 2: ask Groq for the actual change as STRICT JSON we can apply. Context comes from
    /// RECALL against the .said brain (only the relevant route-registration + an example test), NOT from
    /// force-feeding whole files — token-efficient and avoids truncation.</summary>
    public async Task<ChangeSet?> ProduceChangeAsync(AiAgent agent, int ticket, string title, string body,
        string plan, CancellationToken ct)
    {
        // RECALL just what's needed from .said: where routes are registered + an example endpoint + a test.
        var routeCtx = SaidRecall("grep", "MapGet");                  // existing endpoint registrations
        var testCtx  = SaidRecall("grep", "[Fact]");                  // existing test pattern
        if (string.IsNullOrWhiteSpace(routeCtx))                       // fall back to a symbol lookup
            routeCtx = SaidRecall("ask", "where are minimal-api GET endpoints registered in Program.cs");

        var sys =
            "You implement ONE minimal change for the Advisory .NET 10 API, test-first. Return ONLY strict JSON " +
            "matching: {\"summary\":\"...\",\"files\":[{\"path\":\"relative/path\",\"content\":\"FULL new file content\"}]}. " +
            "Rules: provide the COMPLETE new content for each file you change (not a diff). Extend " +
            "src/Advisory.Api/Program.cs (add a MapGet next to the existing health/version/uptime routes) and " +
            "tests/Advisory.Tests/HealthTests.cs (add xUnit [Fact] tests). Keep the change tiny and correct. " +
            "Use the recalled .said context below to match the EXISTING style and paths. No prose, JSON only.";
        var user =
            $"Ticket #{ticket}: {title}\n\n{body}\n\nApproved plan:\n{plan}\n\n" +
            $"=== .said recall: route registrations (MapGet) ===\n{Trim(routeCtx, 4000)}\n\n" +
            $"=== .said recall: test pattern ([Fact]) ===\n{Trim(testCtx, 3000)}\n";
        var rr = await runner.RunAsync(agent, new AgentRunRequest("execution", agent.Persona ?? "", sys, user), ct);
        if (!rr.Ok) { log.LogWarning("Groq execution failed: {err}", rr.Error); return null; }
        var cs = ParseChangeSet(rr.Text);
        if (cs is not null) SaidRemember($"Groq cycle #{ticket}: {cs.summary}");   // push the result back to .said
        return cs;
    }

    static string Trim(string s, int max) => string.IsNullOrEmpty(s) ? "(nothing recalled)" : (s.Length <= max ? s : s[..max] + "…");

    static ChangeSet? ParseChangeSet(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        // Strip a ```json fence if present.
        var t = text.Trim();
        if (t.StartsWith("```"))
        {
            int nl = t.IndexOf('\n'); if (nl > 0) t = t[(nl + 1)..];
            int fence = t.LastIndexOf("```"); if (fence > 0) t = t[..fence];
        }
        // Try the whole thing, then the outermost {...}. Models may prepend reasoning text, so scan for
        // the first '{' and walk braces to find a balanced object containing "files".
        foreach (var cand in CandidateJsons(t))
        {
            try
            {
                var cs = JsonSerializer.Deserialize<ChangeSet>(cand, opts);
                if (cs is { files: not null } && cs.files.Count > 0 && cs.files.All(f => !string.IsNullOrWhiteSpace(f.path) && f.content is not null))
                    return cs;
            }
            catch { /* try next candidate */ }
        }
        return null;
    }

    static IEnumerable<string> CandidateJsons(string t)
    {
        yield return t;
        int first = t.IndexOf('{'), last = t.LastIndexOf('}');
        if (first >= 0 && last > first) yield return t.Substring(first, last - first + 1);
        // Balanced-brace scan from each '{' that is followed (soon) by "files".
        for (int i = t.IndexOf('{'); i >= 0; i = t.IndexOf('{', i + 1))
        {
            int depth = 0; bool inStr = false, esc = false;
            for (int j = i; j < t.Length; j++)
            {
                char ch = t[j];
                if (inStr) { if (esc) esc = false; else if (ch == '\\') esc = true; else if (ch == '"') inStr = false; }
                else if (ch == '"') inStr = true;
                else if (ch == '{') depth++;
                else if (ch == '}') { depth--; if (depth == 0) { yield return t.Substring(i, j - i + 1); break; } }
            }
        }
    }

    /// <summary>Apply the change in a throwaway clone, build+test, push a branch, open a PR. Returns
    /// (ok, prUrl-or-error). The /workspace mount is never touched.</summary>
    public async Task<(bool ok, string detail)> ImplementAndPrAsync(int ticket, string title, ChangeSet change, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return (false, "no EVOLUTION_REPO");
        var work = Path.Combine(Path.GetTempPath(), $"advisory-groq-{ticket}-{Guid.NewGuid():N}".Substring(0, 28));
        try
        {
            // 0) make git use gh's auth (GH_TOKEN) for https push — otherwise `git push` prompts for a
            //    username and fails ("could not read Username for https://github.com").
            EvolutionService.RunProc("gh", new[] { "auth", "setup-git" }, null, null, 30000);

            // 1) clone (shallow) into a writable dir
            var (cok, _, cerr) = await Task.Run(() => EvolutionService.RunProc("gh",
                new[] { "repo", "clone", Repo!, work, "--", "--depth", "1" }, null, null, 120000), ct);
            if (!cok) return (false, $"clone failed: {cerr}");

            // 2) branch
            var branch = $"mutation/groq-{ticket}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            EvolutionService.RunProc("git", new[] { "checkout", "-b", branch }, work, null, 30000);

            // 3) apply edits (full-content writes)
            foreach (var f in change.files)
            {
                var safe = f.path.Replace('\\', '/').TrimStart('/');
                if (safe.Contains("..")) return (false, $"refusing unsafe path {f.path}");
                var full = Path.Combine(work, safe);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllTextAsync(full, f.content, ct);
            }

            // 4) build + test (in the clone)
            var (bok, _, berr) = await Task.Run(() => EvolutionService.RunProc("dotnet",
                new[] { "build", "src/Advisory.Api/Advisory.Api.csproj", "-c", "Release", "--nologo" }, work, null, 300000), ct);
            var (tok, _, terr) = bok
                ? await Task.Run(() => EvolutionService.RunProc("dotnet",
                    new[] { "test", "tests/Advisory.Tests/Advisory.Tests.csproj", "--nologo" }, work, null, 300000), ct)
                : (false, "", "skipped (build failed)");
            var draft = (bok && tok) ? null : "--draft";

            // 5) commit + push
            EvolutionService.RunProc("git", new[] { "add", "-A" }, work, null, 30000);
            EvolutionService.RunProc("git", new[] { "-c", "user.name=Advisory Groq", "-c", "user.email=groq@advisory.local",
                "commit", "-m", $"mutate: #{ticket} {change.summary}" }, work, null, 30000);
            var (pok, _, perr) = await Task.Run(() => EvolutionService.RunProc("git",
                new[] { "push", "-u", "origin", branch }, work, null, 120000), ct);
            if (!pok) return (false, $"push failed: {perr}");

            // 6) open PR
            var bodyTxt = $"Automated Groq mutation (API-native, no worker).\n\nCloses #{ticket}\n\n" +
                          $"{change.summary}\n\nBuild: {(bok ? "ok" : "FAILED")} · Tests: {(tok ? "pass" : "not passing — draft")}";
            var prArgs = new List<string> { "pr", "create", "--repo", Repo!, "--base", DefaultBranch, "--head", branch,
                "--title", $"mutate: #{ticket} {title}", "--body", bodyTxt };
            if (draft is not null) prArgs.Add(draft);
            var (prok, prout, prerr) = await Task.Run(() => EvolutionService.RunProc("gh", prArgs.ToArray(), work, null, 60000), ct);
            if (!prok) return (false, $"pr create failed: {prerr}");
            var url = prout.Split('\n').FirstOrDefault(l => l.StartsWith("https://"))?.Trim() ?? prout.Trim();
            return (true, url);
        }
        catch (Exception ex) { return (false, ex.Message); }
        finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { } }
    }
}
