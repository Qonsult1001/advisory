using System.Text;

namespace PkgFirewall.Api.Evolution;

/// <summary>
/// Executes one evolution run end-to-end, PR-ONLY:
///   clone sandbox repo → new branch → run the EVOLVE engine with the ticket as the task →
///   run the project's tests → push branch → open a pull request for human review.
/// It NEVER pushes to the default branch and NEVER merges. If tests fail, it opens a DRAFT PR
/// flagged for review rather than a clean one, so a human always sees the work.
/// </summary>
public class EvolutionRunner
{
    private readonly EvolutionService _svc;
    private readonly IConfiguration _cfg;
    private readonly ILogger<EvolutionRunner> _log;

    public EvolutionRunner(EvolutionService svc, IConfiguration cfg, ILogger<EvolutionRunner> log)
    { _svc = svc; _cfg = cfg; _log = log; }

    public Task StartAsync(EvoRun run, EvoTicket ticket)
        => Task.Run(() => RunAsync(run, ticket));

    private async Task RunAsync(EvoRun run, EvoTicket ticket)
    {
        var workRoot = _cfg["EVOLUTION_WORKDIR"] ?? Path.Combine(Path.GetTempPath(), "pkgfw-evolution");
        Directory.CreateDirectory(workRoot);
        var dir = Path.Combine(workRoot, $"run-{run.Id}");
        var repo = _svc.Repo!;
        var branch = $"evolve/issue-{ticket.Number}-{run.Id}";
        var apiKey = _cfg["ANTHROPIC_API_KEY"] ?? "";
        var env = new Dictionary<string, string> { ["GH_PROMPT_DISABLED"] = "1" };
        var engineEnv = new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = apiKey };

        try
        {
            run.Status = "running"; run.Stage = "clone";
            run.Append($"[clone] {repo}");
            // gh repo clone respects auth; shallow clone for speed.
            var (cok, _, cerr) = await Task.Run(() => EvolutionService.RunProc("gh",
                new[] { "repo", "clone", repo, dir, "--", "--depth", "1" }, workRoot, env, 120000));
            if (!cok) { Fail(run, $"clone failed: {cerr}"); return; }

            run.Stage = "branch"; run.Append($"[branch] {branch}");
            Git(dir, env, "checkout", "-b", branch);

            // ---- run the EVOLVE engine on the ticket ----
            run.Stage = "engine"; run.Append("[engine] invoking yoyo on the ticket");
            var context = await _svc.TicketContextAsync(ticket.Number, CancellationToken.None);
            var prompt = BuildEnginePrompt(ticket, context);
            if (!_svc.EngineConfigured)
            {
                run.Append("[engine] not configured (no binary or ANTHROPIC_API_KEY) — opening a triage PR with the plan instead");
                await TriageOnly(run, ticket, dir, branch, env, repo, prompt);
                return;
            }
            // yoyo reads a single prompt in piped mode: `echo "<prompt>" | yoyo --model <m>`
            var (eok, eout, eerr) = await Task.Run(() => RunEngine(_svc.EngineBin, _svc.Model, prompt, dir, engineEnv));
            run.Append(eout.Length > 4000 ? eout[^4000..] : eout);
            if (!eok) run.Append($"[engine] non-zero exit: {eerr}");

            // ---- did the engine actually change anything? ----
            var (_, status, _) = Git(dir, env, "status", "--porcelain");
            if (string.IsNullOrWhiteSpace(status))
            {
                run.Status = "skipped"; run.Stage = "no-change";
                run.Append("[result] engine produced no changes — nothing to PR");
                run.FinishedAt = DateTimeOffset.UtcNow;
                return;
            }
            Git(dir, env, "add", "-A");
            Git(dir, env, "commit", "-m", $"evolve: address issue #{ticket.Number} — {Trim(ticket.Title, 60)}");

            // ---- run tests (best-effort, language-agnostic detection) ----
            run.Status = "tests"; run.Stage = "tests";
            run.TestsPassed = RunTests(dir, run);

            // ---- push branch + open PR (NEVER merge, NEVER push default branch) ----
            run.Stage = "push"; run.Append($"[push] {branch}");
            var (pok, _, perr) = Git(dir, env, "push", "-u", "origin", branch);
            if (!pok) { Fail(run, $"push failed: {perr}"); return; }

            run.Stage = "pr";
            var draft = !run.TestsPassed;
            var prArgs = new List<string> {
                "pr", "create", "--repo", repo, "--head", branch,
                "--title", $"evolve: issue #{ticket.Number} — {Trim(ticket.Title, 70)}",
                "--body", PrBody(ticket, run),
            };
            if (draft) prArgs.Add("--draft");
            var (prok, prout, prerr) = await Task.Run(() => EvolutionService.RunProc("gh", prArgs.ToArray(), dir, env, 60000));
            if (!prok) { Fail(run, $"pr create failed: {prerr}"); return; }

            run.PrUrl = prout.Trim().Split('\n').LastOrDefault(l => l.StartsWith("http"))?.Trim() ?? prout.Trim();
            run.Branch = branch;
            run.Status = "pr-open"; run.Stage = draft ? "pr-draft (tests red)" : "pr-open";
            run.Append($"[pr] {(draft ? "DRAFT (tests failed — needs review) " : "")}{run.PrUrl}");

            // Reply on the ticket linking the PR (the tester sees the response).
            await Task.Run(() => EvolutionService.RunProc("gh",
                new[] { "issue", "comment", ticket.Number.ToString(), "--repo", repo,
                        "--body", $"🤖 Evolution opened a {(draft ? "draft " : "")}PR for this: {run.PrUrl}\n\nTests {(run.TestsPassed ? "passed ✅" : "did not pass — flagged for review ⚠️")}. A human will review before merge." },
                dir, env, 30000));
        }
        catch (Exception ex) { Fail(run, $"exception: {ex.Message}"); }
        finally
        {
            run.FinishedAt = DateTimeOffset.UtcNow;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    private (bool, string, string) Git(string dir, Dictionary<string, string> env, params string[] args)
        => EvolutionService.RunProc("git", args, dir, env, 60000);

    private (bool ok, string outp, string err) RunEngine(string bin, string model, string prompt,
        string cwd, Dictionary<string, string> env)
    {
        // yoyo piped mode: stdin = prompt, single-shot (no REPL). We write the prompt to stdin.
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = bin, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = cwd,
        };
        psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(model);
        psi.ArgumentList.Add("--skills"); psi.ArgumentList.Add("./skills");
        foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardInput.Write(prompt); p.StandardInput.Close();
        var so = p.StandardOutput.ReadToEndAsync(); var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15 * 60 * 1000)) { try { p.Kill(true); } catch { } return (false, so.Result, "engine timeout"); }
        return (p.ExitCode == 0, so.Result, se.Result);
    }

    /// <summary>Language-agnostic test detection. Returns true only on a green run.</summary>
    private bool RunTests(string dir, EvoRun run)
    {
        (string file, string[] args)? cmd =
            File.Exists(Path.Combine(dir, "Cargo.toml")) ? ("cargo", new[] { "test", "--quiet" })
          : Directory.GetFiles(dir, "*.sln").Length > 0 || Directory.GetFiles(dir, "*.csproj", SearchOption.AllDirectories).Length > 0 ? ("dotnet", new[] { "test", "--nologo" })
          : File.Exists(Path.Combine(dir, "package.json")) ? ("npm", new[] { "test", "--silent" })
          : File.Exists(Path.Combine(dir, "go.mod")) ? ("go", new[] { "test", "./..." })
          : null;
        if (cmd is null) { run.Append("[tests] no test runner detected — skipping (PR will note this)"); return false; }
        run.Append($"[tests] {cmd.Value.file} {string.Join(' ', cmd.Value.args)}");
        var (ok, outp, err) = EvolutionService.RunProc(cmd.Value.file, cmd.Value.args, dir, null, 8 * 60 * 1000);
        run.Append((outp.Length > 3000 ? outp[^3000..] : outp) + (ok ? "\n[tests] PASSED" : $"\n[tests] FAILED\n{Trim(err, 1500)}"));
        return ok;
    }

    private async Task TriageOnly(EvoRun run, EvoTicket ticket, string dir, string branch,
        Dictionary<string, string> env, string repo, string prompt)
    {
        // No engine available: write a TRIAGE.md with the plan and open a draft PR so the loop is
        // still demonstrable and a human can act. Honest about what happened.
        await File.WriteAllTextAsync(Path.Combine(dir, $"TRIAGE-{ticket.Number}.md"),
            $"# Evolution triage — issue #{ticket.Number}\n\n{prompt}\n\n_(Engine binary not configured; this is a plan, not an automated fix.)_");
        Git(dir, env, "add", "-A");
        Git(dir, env, "commit", "-m", $"evolve(triage): plan for issue #{ticket.Number}");
        var (pok, _, _) = Git(dir, env, "push", "-u", "origin", branch);
        if (!pok) { Fail(run, "triage push failed"); return; }
        var (prok, prout, _) = await Task.Run(() => EvolutionService.RunProc("gh", new[] {
            "pr", "create", "--repo", repo, "--head", branch, "--draft",
            "--title", $"evolve(triage): issue #{ticket.Number}", "--body", PrBody(ticket, run) }, dir, env, 60000));
        run.PrUrl = prout.Trim();
        run.Status = "pr-open"; run.Stage = "triage-pr"; run.Branch = branch;
        run.Append("[result] engine not configured → opened a triage draft PR");
        run.FinishedAt = DateTimeOffset.UtcNow;
    }

    private static string BuildEnginePrompt(EvoTicket t, string context) => $@"You are addressing a single GitHub issue in this repository. Make ONE focused, correct change.

{context}

Rules:
- Read the relevant source before editing. Make the minimum change that fixes the issue.
- Add or update a test that proves the fix.
- Run the project's tests and make them pass.
- Do not touch unrelated code. Do not change CI, secrets, or the default branch.
- When done, stop. A human will review your PR.";

    private static string PrBody(EvoTicket t, EvoRun run) => $@"🤖 **Automated evolution** addressing issue #{t.Number}.

**Ticket:** {t.Title}
**Run:** `{run.Id}`
**Tests:** {(run.TestsPassed ? "✅ passing" : "⚠️ not passing — opened as draft for review")}

This PR was written by the EVOLVE engine and is **for human review** — it will not auto-merge.

---
<details><summary>Run log (tail)</summary>

```
{Trim(run.Log, 3000)}
```
</details>

Closes #{t.Number}";

    private void Fail(EvoRun run, string why)
    {
        run.Status = "failed"; run.Stage = "error"; run.Append("[error] " + why);
        run.FinishedAt = DateTimeOffset.UtcNow;
        _log.LogWarning("evolution run {Id} failed: {Why}", run.Id, why);
    }

    private static string Trim(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");
}
