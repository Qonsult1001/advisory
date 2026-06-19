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

    /// <summary>Save a memory to .said after a change so future cycles recall it. The v0.6.0 CLI command
    /// is `add` (the MCP tool is `remember`); we shell to the CLI here. Best-effort; never blocks.</summary>
    public void SaidRemember(string note)
    {
        if (!File.Exists(SaidBin) || !File.Exists(SaidFile)) return;
        try { EvolutionService.RunProc(SaidBin, new[] { "add", note, "--path", SaidFile }, null, null, 15000); }
        catch { }
    }

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



    /// <summary>Last N non-empty lines of build/test output, for a concise failure reason.</summary>
    static string LastLines(string s, int n)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(no output)";
        var lines = s.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - n)));
    }

    /// <summary>Pick the most informative single line from a build/test/edit error for the run log:
    /// prefer the first real compiler/anchor error (`error CS...`, `anchor ... not found`, `said edit
    /// failed`), else the first non-empty line. Bounded length so the log stays readable.</summary>
    static string FirstErrorLine(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "(no error text)";
        var lines = detail.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        // Bare header tags from ImplementAndPrAsync ("does NOT BUILD:", "TESTS FAILED:") carry no
        // detail — keep the tag as a prefix but append the first line that actually says WHY.
        bool IsHeaderTag(string l) => l is "does NOT BUILD:" or "TESTS FAILED:"
                                      || l.StartsWith("said edit failed", StringComparison.OrdinalIgnoreCase);
        // The most informative line: a real compiler/assert/anchor error that ISN'T just a header tag.
        var detailLine = lines.FirstOrDefault(l => !IsHeaderTag(l) && (
                             l.Contains("error CS", StringComparison.OrdinalIgnoreCase)
                             || l.Contains("anchor", StringComparison.OrdinalIgnoreCase)
                             || l.Contains("Assert", StringComparison.OrdinalIgnoreCase)
                             || l.Contains("Expected", StringComparison.OrdinalIgnoreCase)
                             || l.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                             || l.Contains("error", StringComparison.OrdinalIgnoreCase)
                             || l.Contains("Failed", StringComparison.OrdinalIgnoreCase)));
        var header = lines.FirstOrDefault(IsHeaderTag);
        var pick = (header, detailLine) switch
        {
            ({ } h, { } d) when h != d => $"{h} {d}",
            (_, { } d)                 => d,
            ({ } h, _)                 => h,
            _                          => lines.FirstOrDefault() ?? "(no error text)"
        };
        return pick.Length > 240 ? pick.Substring(0, 240) + "…" : pick;
    }

    string OrchBin => cfg["ORCH_BIN"] ?? "/app/said-orchestrate";

    /// <summary>Map the routed MAF agent to the env said-orchestrate reads (model-agnostic). Groq →
    /// GROQ_API_KEY/GROQ_MODEL; any other openai-compatible endpoint (OpenRouter, on-prem) →
    /// OPENAI_API_KEY/SAID_LLM_BASE_URL/SAID_LLM_MODEL. Key comes from the agent, else the process env.</summary>
    Dictionary<string, string> OrchestratorEnv(AiAgent agent)
    {
        var env = new Dictionary<string, string>();
        var ep = (agent.Endpoint ?? "").ToLowerInvariant();
        var key = !string.IsNullOrWhiteSpace(agent.ApiKey) ? agent.ApiKey! : null;
        if (ep.Contains("api.groq.com") || ep.Length == 0)
        {
            env["GROQ_API_KEY"] = key ?? cfg["GROQ_API_KEY"] ?? cfg["PKGFW_GROQ_API_KEY"] ?? "";
            env["GROQ_MODEL"] = string.IsNullOrWhiteSpace(agent.Model) ? "openai/gpt-oss-120b" : agent.Model;
        }
        else
        {
            env["OPENAI_API_KEY"] = key ?? (ep.Contains("openrouter.ai") ? cfg["OPENROUTER_API_KEY"] ?? "" : cfg["OPENAI_API_KEY"] ?? "");
            env["SAID_LLM_BASE_URL"] = agent.Endpoint!;
            env["SAID_LLM_MODEL"] = string.IsNullOrWhiteSpace(agent.Model) ? "moonshotai/kimi-k2" : agent.Model;
        }
        return env;
    }

    /// <summary>
    /// THE in-container mutation step (replaces the old produce-changeset + said-edit + repair loop).
    /// Clones the repo to a writable temp dir, runs said-orchestrate there — which drives the routed LLM
    /// through plan→design→code→test→repair→learn and gate-verifies with the project's build+test — then
    /// re-runs the gate as a safety net and opens a PR ON GREEN. The orchestrator never merges red; if it
    /// can't make the gate pass within max-attempts it stops and we open NO PR. /workspace stays read-only
    /// (all writes are in the throwaway clone). gh is already authed in-container (GH_TOKEN).
    /// </summary>
    public async Task<(bool ok, string detail)> ImplementWithRepairAsync(
        AiAgent agent, int ticket, string title, string body, string plan,
        Action<string, string>? progress, int maxRepairs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return (false, "no EVOLUTION_REPO");
        if (!File.Exists(OrchBin)) return (false, $"said-orchestrate not found at {OrchBin} (rebuild the image with the v0.11.1 binary)");
        var env = OrchestratorEnv(agent);
        if ((env.GetValueOrDefault("GROQ_API_KEY", "") + env.GetValueOrDefault("OPENAI_API_KEY", "")).Length == 0)
            return (false, "no LLM key for the routed execution agent (set the agent's key or the provider env)");

        var work = Path.Combine(Path.GetTempPath(), $"advisory-orch-{ticket}-{Guid.NewGuid():N}".Substring(0, 30));
        var buildCmd = "dotnet build src/Advisory.Api/Advisory.Api.csproj -c Release --nologo";
        var testCmd  = "dotnet test tests/Advisory.Tests/Advisory.Tests.csproj --nologo";
        try
        {
            // git uses gh's auth for https push.
            EvolutionService.RunProc("gh", new[] { "auth", "setup-git" }, null, null, 30000);

            progress?.Invoke("plan", "cloning the repo (writable) for the orchestrator");
            var (cok, _, cerr) = await Task.Run(() => EvolutionService.RunProc("gh",
                new[] { "repo", "clone", Repo!, work, "--", "--depth", "1" }, null, null, 120000), ct);
            if (!cok) return (false, $"clone failed: {cerr}");

            var branch = $"mutation/orch-{ticket}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            EvolutionService.RunProc("git", new[] { "checkout", "-b", branch }, work, null, 30000);
            // Brain in the clone so the orchestrator's recall (--brain) resolves; removed before commit.
            try { if (File.Exists(SaidFile)) File.Copy(SaidFile, Path.Combine(work, "Advisory.said"), true); } catch { }

            // RUN THE CLOSED LOOP. The orchestrator plans/codes/tests/repairs in the clone and only
            // succeeds (exit 0) when the gate is green.
            progress?.Invoke("fix", $"said-orchestrate: plan→design→code→test→repair (gate-verified, {agent.Model})");
            var task = $"Ticket #{ticket}: {title}\n\n{body}\n\nPlan:\n{plan}";
            var (ook, oout, oerr) = await Task.Run(() => EvolutionService.RunProc(OrchBin,
                new[] { "--brain", "Advisory.said", "--repo", work, "--task", task,
                        "--build", buildCmd, "--test", testCmd, "--max-attempts", maxRepairs.ToString() },
                work, env, 1500000), ct);   // up to ~25 min for the full loop
            // Surface a compact tail of the orchestrator's phase log into the run.
            var phaseTail = LastLines((oout + "\n" + oerr).Replace("[SAID] BLAKE3 mismatch", ""), 14);
            progress?.Invoke("fix", $"orchestrator: {(ook ? "gate green" : "stopped (not green)")}\n{FirstErrorLine(phaseTail)}");
            try { File.Delete(Path.Combine(work, "Advisory.said")); } catch { }   // never commit the brain

            if (!ook)
                return (false, $"orchestrator stopped — gate not green within {maxRepairs} attempts (no PR; clone discarded):\n{phaseTail}");

            // SAFETY-NET gate: re-run build+test in the clone before pushing (defence in depth — never
            // trust a green claim without verifying it here too).
            progress?.Invoke("build", "verifying the orchestrator's change builds + tests before PR");
            var (bok, bout, berr) = await Task.Run(() => EvolutionService.RunProc("dotnet",
                new[] { "build", "src/Advisory.Api/Advisory.Api.csproj", "-c", "Release", "--nologo" }, work, null, 300000), ct);
            if (!bok) return (false, $"orchestrator claimed green but does NOT BUILD (no PR):\n{LastLines(bout + "\n" + berr, 12)}");
            var (tok, tout, terr) = await Task.Run(() => EvolutionService.RunProc("dotnet",
                new[] { "test", "tests/Advisory.Tests/Advisory.Tests.csproj", "--nologo" }, work, null, 300000), ct);
            if (!tok) return (false, $"orchestrator claimed green but TESTS FAILED (no PR):\n{LastLines(tout + "\n" + terr, 12)}");

            // commit + push (only reached when the gate is verified green).
            EvolutionService.RunProc("git", new[] { "add", "-A" }, work, null, 30000);
            EvolutionService.RunProc("git", new[] { "-c", "user.name=Advisory Orchestrator", "-c", "user.email=orchestrate@advisory.local",
                "commit", "-m", $"mutate: #{ticket} {title}" }, work, null, 30000);
            var (pok, _, perr) = await Task.Run(() => EvolutionService.RunProc("git",
                new[] { "push", "-u", "origin", branch }, work, null, 120000), ct);
            if (!pok) return (false, $"push failed: {perr}");

            var bodyTxt = $"Automated mutation via said-orchestrate (in-container, no worker).\n\nCloses #{ticket}\n\n" +
                          $"Driven by {agent.Name} ({agent.Model}) through plan→design→code→test→repair.\n" +
                          $"✅ Built and tests passed in-clone before this PR was opened.";
            var (prok, prout, prerr) = await Task.Run(() => EvolutionService.RunProc("gh",
                new[] { "pr", "create", "--repo", Repo!, "--base", DefaultBranch, "--head", branch,
                        "--title", $"mutate: #{ticket} {title}", "--body", bodyTxt }, work, null, 60000), ct);
            if (!prok) return (false, $"pr create failed: {prerr}");
            var url = prout.Split('\n').FirstOrDefault(l => l.StartsWith("https://"))?.Trim() ?? prout.Trim();
            return (true, url);
        }
        catch (Exception ex) { return (false, ex.Message); }
        finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { } }
    }

}
