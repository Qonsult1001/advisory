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

    /// <summary>One SURGICAL edit applied via `said edit` — an anchored insert/replace, NEVER a whole-file
    /// rewrite. mode is a said edit mode; anchor is exact text (text modes) or symbol (symbol modes).</summary>
    public record Edit(string file, string mode, string? anchor, string? symbol, string content);
    public record ChangeSet(string summary, List<Edit> edits);

    static readonly HashSet<string> ValidModes = new()
    {
        "insert-after-text", "insert-before-text", "replace-text",
        "insert-after-symbol", "insert-before-symbol", "replace-symbol", "delete-symbol"
    };

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

    /// <summary>Phase 2: ask Groq for SURGICAL edits (anchored insert/replace) — NOT full file content.
    /// Each edit is applied via `said edit`, which cannot rewrite a whole file, so the model can't gut
    /// Program.cs the way the old full-file approach did. Context is RECALL from .said.</summary>
    public async Task<ChangeSet?> ProduceChangeAsync(AiAgent agent, int ticket, string title, string body,
        string plan, CancellationToken ct, string? priorFailure = null, ChangeSet? priorAttempt = null)
    {
        // RECALL the EXACT anchor text from .said: existing endpoint registrations + a test pattern.
        var routeCtx = SaidRecall("grep", "MapGet");
        var testCtx  = SaidRecall("grep", "Fact");
        if (string.IsNullOrWhiteSpace(routeCtx))
            routeCtx = SaidRecall("ask", "where are minimal-api GET endpoints registered in Program.cs");

        var sys =
            "You implement ONE minimal change for the Advisory .NET 10 API, test-first, as SURGICAL EDITS. " +
            "Return ONLY strict JSON: {\"summary\":\"...\",\"edits\":[{\"file\":\"relative/path\",\"mode\":\"<mode>\"," +
            "\"anchor\":\"EXACT existing line text\",\"content\":\"new code\"}]}. " +
            "NEVER return whole-file content. Each edit is applied at an anchor — you can only INSERT or REPLACE " +
            "at a precise location, never rewrite a file. Prefer mode \"insert-after-text\" with an \"anchor\" that " +
            "is an EXACT substring of an existing line (copy it verbatim from the recalled context below). " +
            "CRITICAL: the anchor must be a COMPLETE, self-contained line — never the first line of a multi-line " +
            "statement (e.g. don't anchor on `Results.Ok(new` that spans several lines); pick a line that ends a " +
            "statement, like one with `.AllowAnonymous();`, so the insert lands between statements, not inside one. " +
            "Add the endpoint to src/Advisory.Api/Program.cs (insert-after-text, anchor = a complete existing " +
            "`.AllowAnonymous();` line) and a test to tests/Advisory.Tests/HealthTests.cs. " +
            "Valid modes: insert-after-text, insert-before-text, replace-text. No prose, JSON only.";
        var repair = "";
        if (!string.IsNullOrWhiteSpace(priorFailure))
            repair =
                "\n\n=== YOUR PREVIOUS ATTEMPT FAILED — FIX IT ===\n" +
                "The previous change set did not build/test. Here is what you returned:\n" +
                JsonSerializer.Serialize(priorAttempt) + "\n\nThe build/test error was:\n" + Trim(priorFailure, 2500) +
                "\nReturn a CORRECTED change set. Common causes: the anchor was inside a multi-line statement " +
                "(pick a complete line that ends in `;`), a duplicate definition, or a missing using/namespace.";
        var user =
            $"Ticket #{ticket}: {title}\n\n{body}\n\nApproved plan:\n{plan}\n\n" +
            $"=== .said recall: existing endpoint registrations (copy an anchor verbatim) ===\n{Trim(routeCtx, 4000)}\n\n" +
            $"=== .said recall: existing test pattern (anchor for the test file) ===\n{Trim(testCtx, 3000)}\n" + repair;
        var rr = await runner.RunAsync(agent, new AgentRunRequest("execution", agent.Persona ?? "", sys, user), ct);
        if (!rr.Ok) { log.LogWarning("Groq execution failed: {err}", rr.Error); return null; }
        var cs = ParseChangeSet(rr.Text);
        if (cs is not null && string.IsNullOrWhiteSpace(priorFailure)) SaidRemember($"Groq cycle #{ticket}: {cs.summary}");
        return cs;
    }

    static string Trim(string s, int max) => string.IsNullOrEmpty(s) ? "(nothing recalled)" : (s.Length <= max ? s : s[..max] + "…");

    /// <summary>Last N non-empty lines of build/test output, for a concise failure reason.</summary>
    static string LastLines(string s, int n)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(no output)";
        var lines = s.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - n)));
    }

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
                if (cs is { edits: not null } && cs.edits.Count > 0 &&
                    cs.edits.All(e => !string.IsNullOrWhiteSpace(e.file) && ValidModes.Contains(e.mode ?? "")
                                      && e.content is not null
                                      && (!string.IsNullOrWhiteSpace(e.anchor) || !string.IsNullOrWhiteSpace(e.symbol))))
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

    /// <summary>Result of one implement attempt. buildOrTestFailed=true means it's RETRYABLE — feed
    /// `detail` (the compiler/test error) back to Groq for a corrected change set.</summary>
    public record ImplResult(bool ok, string detail, bool buildOrTestFailed);

    /// <summary>Drive the full implement step with a SELF-REPAIR loop: produce a change set, apply +
    /// build + test in a throwaway clone; if it fails to build/test, feed the error back to Groq for a
    /// corrected change set and retry (up to maxRepairs). Only opens a PR when build AND tests pass —
    /// a non-compiling change can never reach a PR. Reports progress via the supplied callback.</summary>
    public async Task<(bool ok, string detail)> ImplementWithRepairAsync(
        AiAgent agent, int ticket, string title, string body, string plan,
        Action<string, string>? progress, int maxRepairs, CancellationToken ct)
    {
        ChangeSet? change = await ProduceChangeAsync(agent, ticket, title, body, plan, ct);
        if (change is null) return (false, "Groq did not return a valid change set");
        string lastErr = "";
        for (int attempt = 0; attempt <= maxRepairs; attempt++)
        {
            progress?.Invoke("build", attempt == 0
                ? $"building + testing: {change!.summary}"
                : $"repair attempt {attempt}/{maxRepairs}: re-building after a failure");
            var r = await ImplementAndPrAsync(ticket, title, change!, ct);
            if (r.ok) return (true, r.detail);                       // PR opened
            if (!r.buildOrTestFailed) return (false, r.detail);      // hard error (clone/push/etc.) — don't loop
            lastErr = r.detail;
            if (attempt == maxRepairs) break;
            // Build/test failed — ask Groq to FIX it, passing the error + its prior attempt.
            progress?.Invoke("fix", $"change didn't build/test — asking Groq to fix (attempt {attempt + 1}/{maxRepairs})");
            var fixedCs = await ProduceChangeAsync(agent, ticket, title, body, plan, ct, lastErr, change);
            if (fixedCs is null) return (false, $"repair failed — Groq returned no valid change set. Last error:\n{lastErr}");
            change = fixedCs;
        }
        return (false, $"change still failed to build/test after {maxRepairs} repair attempt(s). Last error:\n{lastErr}");
    }

    /// <summary>Apply the change in a throwaway clone, build+test; only on PASS push + open a PR. Returns
    /// ImplResult (buildOrTestFailed=true = retryable). The /workspace mount is never touched.</summary>
    public async Task<ImplResult> ImplementAndPrAsync(int ticket, string title, ChangeSet change, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return new(false, "no EVOLUTION_REPO", false);
        var work = Path.Combine(Path.GetTempPath(), $"advisory-groq-{ticket}-{Guid.NewGuid():N}".Substring(0, 28));
        try
        {
            // 0) make git use gh's auth (GH_TOKEN) for https push — otherwise `git push` prompts for a
            //    username and fails ("could not read Username for https://github.com").
            EvolutionService.RunProc("gh", new[] { "auth", "setup-git" }, null, null, 30000);

            // 1) clone (shallow) into a writable dir
            var (cok, _, cerr) = await Task.Run(() => EvolutionService.RunProc("gh",
                new[] { "repo", "clone", Repo!, work, "--", "--depth", "1" }, null, null, 120000), ct);
            if (!cok) return new(false, $"clone failed: {cerr}", false);

            // 2) branch
            var branch = $"mutation/groq-{ticket}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            EvolutionService.RunProc("git", new[] { "checkout", "-b", branch }, work, null, 30000);

            // make the brain available in the clone so `said edit` symbol modes can resolve ranges
            // (text-anchor modes don't need it, but copy it so both work). Best-effort.
            try { if (File.Exists(SaidFile)) File.Copy(SaidFile, Path.Combine(work, "Advisory.said"), true); } catch { }

            // 3) apply each edit SURGICALLY via `said edit` — anchored insert/replace, NEVER a whole-file
            //    write. If any edit fails to resolve its anchor, ABORT the whole change set (no partial PR).
            foreach (var e in change.edits)
            {
                var safe = e.file.Replace('\\', '/').TrimStart('/');
                if (safe.Contains("..")) return new(false, $"refusing unsafe path {e.file}", false);
                var cfile = Path.Combine(work, $".edit-content-{Guid.NewGuid():N}.txt");
                await File.WriteAllTextAsync(cfile, e.content, ct);
                var args = new List<string> { "edit", "--path", "Advisory.said", "--file", safe, e.mode };
                if (!string.IsNullOrWhiteSpace(e.symbol)) { args.Add("--symbol"); args.Add(e.symbol!); }
                else { args.Add("--anchor"); args.Add(e.anchor!); }
                args.Add("--content-file"); args.Add(cfile);
                args.Add("--json");
                var (eok, eout, eerr) = EvolutionService.RunProc(SaidBin, args.ToArray(), work, null, 30000);
                try { File.Delete(cfile); } catch { }
                if (!eok || eout.Contains("\"ok\":false"))
                    return new(false, $"said edit failed for {e.file} ({e.mode}): {(string.IsNullOrWhiteSpace(eerr) ? eout : eerr)}".Trim(), true);
            }
            try { File.Delete(Path.Combine(work, "Advisory.said")); } catch { }   // don't commit the brain

            // 4) BUILD + TEST GATE (in the clone). A change that does not BUILD and PASS TESTS must NEVER
            //    reach a PR — this is what stops a bad surgical anchor (which once split Program.cs) from
            //    becoming a mergeable PR. The runtime image is now SDK-based so this actually runs.
            var (bok, bout, berr) = await Task.Run(() => EvolutionService.RunProc("dotnet",
                new[] { "build", "src/Advisory.Api/Advisory.Api.csproj", "-c", "Release", "--nologo" }, work, null, 300000), ct);
            if (!bok)
            {
                var tail = LastLines(bout + "\n" + berr, 12);
                return new(false, $"does NOT BUILD:\n{tail}", true);
            }
            var (tok, tout, terr) = await Task.Run(() => EvolutionService.RunProc("dotnet",
                new[] { "test", "tests/Advisory.Tests/Advisory.Tests.csproj", "--nologo" }, work, null, 300000), ct);
            if (!tok)
            {
                var tail = LastLines(tout + "\n" + terr, 12);
                return new(false, $"TESTS FAILED:\n{tail}", true);
            }

            // 5) commit + push (only reached when build AND tests pass)
            EvolutionService.RunProc("git", new[] { "add", "-A" }, work, null, 30000);
            EvolutionService.RunProc("git", new[] { "-c", "user.name=Advisory Groq", "-c", "user.email=groq@advisory.local",
                "commit", "-m", $"mutate: #{ticket} {change.summary}" }, work, null, 30000);
            var (pok, _, perr) = await Task.Run(() => EvolutionService.RunProc("git",
                new[] { "push", "-u", "origin", branch }, work, null, 120000), ct);
            if (!pok) return new(false, $"push failed: {perr}", false);

            // 6) open a READY (non-draft) PR — it built and passed tests in-clone, so it's mergeable.
            var bodyTxt = $"Automated Groq mutation (API-native, no worker).\n\nCloses #{ticket}\n\n" +
                          $"{change.summary}\n\n✅ Built and tests passed in-clone before this PR was opened.";
            var prArgs = new List<string> { "pr", "create", "--repo", Repo!, "--base", DefaultBranch, "--head", branch,
                "--title", $"mutate: #{ticket} {title}", "--body", bodyTxt };
            var (prok, prout, prerr) = await Task.Run(() => EvolutionService.RunProc("gh", prArgs.ToArray(), work, null, 60000), ct);
            if (!prok) return new(false, $"pr create failed: {prerr}", false);
            var url = prout.Split('\n').FirstOrDefault(l => l.StartsWith("https://"))?.Trim() ?? prout.Trim();
            return new(true, url, false);
        }
        catch (Exception ex) { return new(false, ex.Message, false); }
        finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { } }
    }
}
