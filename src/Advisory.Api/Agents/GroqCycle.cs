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

    /// <summary>Read the actual complete `app.MapGet(...)...AllowAnonymous();` registration lines from
    /// Program.cs so the model copies a REAL anchor verbatim (never hallucinates one like /healthz).
    /// Reads the read-only /workspace mount (current source). Returns "" if unavailable.</summary>
    static string RealEndpointAnchors()
    {
        foreach (var p in new[] { "/workspace/src/Advisory.Api/Program.cs", "src/Advisory.Api/Program.cs" })
        {
            try
            {
                if (!File.Exists(p)) continue;
                var lines = File.ReadAllLines(p);
                // Anchor on the LINE THAT ENDS a registration (`.AllowAnonymous();`), so inserting AFTER it
                // lands the new endpoint BETWEEN complete registrations — never inside a multi-line statement
                // (that was the Program.cs-split bug). These are single, unique, on-disk lines.
                var hits = lines.Select(x => x.Trim())
                                .Where(l => l.StartsWith(".AllowAnonymous();") || (l.StartsWith("app.MapGet(") && l.Contains("AllowAnonymous();")))
                                .ToList();
                if (hits.Count > 0)
                    return "To add the endpoint, use mode 'insert-after-text' with \"anchor\" = one of these EXACT "
                           + "single lines copied VERBATIM (each ends a registration; your new "
                           + "`app.MapGet(\"/api/...\", ...).AllowAnonymous();` will be inserted on the line after it):\n"
                           + string.Join("\n", hits.Distinct().TakeLast(6));
            }
            catch { }
        }
        return "";
    }

    /// <summary>Read the REAL test fixture from HealthTests.cs so the model copies the proven pattern
    /// (the `_client` field, the actual `using`s, one real `[Fact]`) instead of GUESSING field names
    /// (`_factory`) or spinning its own `new WebApplicationFactory<Program>()` — the CS0103/CS1061
    /// class of first-attempt failures. Reads the read-only /workspace mount (current source).
    /// Returns "" if unavailable.</summary>
    static string RealTestFixture()
    {
        foreach (var p in new[] { "/workspace/tests/Advisory.Tests/HealthTests.cs", "tests/Advisory.Tests/HealthTests.cs" })
        {
            try
            {
                if (!File.Exists(p)) continue;
                var lines = File.ReadAllLines(p);
                // The fixture header = the `using`s + class declaration + ctor (where `_client` is set up).
                // Grab through the constructor line so the model sees EXACTLY how the HttpClient is named
                // and obtained. The ctor is the first line containing "factory.CreateClient".
                int ctor = Array.FindIndex(lines, l => l.Contains("CreateClient"));
                if (ctor < 0) return "";
                var header = string.Join("\n", lines.Take(ctor + 1));
                // One canonical sample [Fact] (the first complete one) so the model copies the call shape.
                int fa = Array.FindIndex(lines, l => l.TrimStart().StartsWith("[Fact]"));
                var sample = "";
                if (fa >= 0)
                {
                    // take from [Fact] until the matching closing brace of the method (first line that is
                    // exactly "}" at the method's indent — good enough: stop at the next [Fact] or 14 lines).
                    var buf = new List<string>();
                    for (int i = fa; i < lines.Length && buf.Count < 14; i++)
                    {
                        if (buf.Count > 0 && lines[i].TrimStart().StartsWith("[Fact]")) break;
                        buf.Add(lines[i]);
                    }
                    sample = string.Join("\n", buf);
                }
                return "The test class ALREADY EXISTS with this exact fixture — your [Fact] runs INSIDE it, "
                       + "so use the field `_client` (already created). Do NOT declare a new field, a new "
                       + "constructor, or `new WebApplicationFactory`. These usings are already present "
                       + "(do not re-add them):\n```csharp\n" + header.Trim()
                       + "\n```\nA real existing test in this class (copy its call shape — `_client.GetAsync`, "
                       + "`JsonDocument.Parse`):\n```csharp\n" + sample.Trim() + "\n```";
            }
            catch { }
        }
        return "";
    }

    /// <summary>Pre-validate a member insertion via `said edit --explain` (v0.6.0): returns the raw JSON
    /// menu of valid anchors for adding to a class/scope, so the model can pick the right move up front.</summary>
    public string SaidExplain(string saidPath, string file, string symbol)
    {
        try
        {
            var (ok, outp, _) = EvolutionService.RunProc(SaidBin,
                new[] { "edit", "--path", saidPath, "--file", file, "--explain", "--symbol", symbol, "--json" },
                Path.GetDirectoryName(saidPath), null, 15000);
            return ok ? outp : "";
        }
        catch { return ""; }
    }

    /// <summary>One SURGICAL edit applied via `said edit` — an anchored insert/replace, NEVER a whole-file
    /// rewrite. mode is a said edit mode; anchor is exact text (text modes) or symbol (symbol modes).</summary>
    public record Edit(string file, string mode, string? anchor, string? symbol, string content);
    public record ChangeSet(string summary, List<Edit> edits);

    static readonly HashSet<string> ValidModes = new()
    {
        "insert-after-text", "insert-before-text", "replace-text",
        "insert-after-symbol", "insert-before-symbol", "replace-symbol", "delete-symbol",
        "append-into-symbol",
        "insert-after-context", "insert-before-context", "replace-context"
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
        // The endpoint anchor MUST be a REAL existing line (else `said edit insert-after-text` errors
        // "anchor not found" and the model wastes repairs). .said recall can't reliably surface Program.cs's
        // top-level minimal-API registrations (they're file-scope statements, not a class symbol, and grep
        // is noisy), so read the actual `app.MapGet(...).AllowAnonymous();` lines straight from the source.
        // /workspace is the read-only repo mount with current source; fall back to .said recall if absent.
        var routeCtx = RealEndpointAnchors();
        if (string.IsNullOrWhiteSpace(routeCtx))
            routeCtx = SaidRecall("grep", "MapGet");

        var sys =
            "You implement ONE minimal change for the Advisory .NET 10 API, test-first, as SURGICAL EDITS. " +
            "Return ONLY strict JSON: {\"summary\":\"...\",\"edits\":[{\"file\":\"relative/path\",\"mode\":\"<mode>\"," +
            "\"anchor\":\"...\",\"symbol\":\"...\",\"content\":\"new code\"}]}. NEVER return whole-file content. " +
            "TWO edits, using these EXACT patterns:\n" +
            "1) ENDPOINT → file src/Advisory.Api/Program.cs, mode \"insert-after-text\", anchor = a COMPLETE existing " +
            "line that ends a statement (copy a full `.AllowAnonymous();` line VERBATIM from the recall below — " +
            "never a prefix or the first line of a multi-line statement). content = one `app.MapGet(...).AllowAnonymous();` line.\n" +
            "2) TEST → file tests/Advisory.Tests/HealthTests.cs, mode \"append-into-symbol\", symbol = \"HealthTests\" " +
            "(NO anchor). This appends your [Fact] method at CLASS scope (auto-indented) so it can NEVER nest inside " +
            "another method. content = the full `[Fact] public async Task ...() { ... }` method body. " +
            "Use the EXISTING `_client` field shown in the fixture below — do NOT declare a new field, constructor, " +
            "or `new WebApplicationFactory`, and do NOT re-add usings that are already present. Only call .NET APIs " +
            "you are certain exist (a wrong member name like GCMemoryInfo.TotalAllocatedBytes fails to compile).\n" +
            "No prose, JSON only.";
        var repair = "";
        if (!string.IsNullOrWhiteSpace(priorFailure))
            repair =
                "\n\n=== YOUR PREVIOUS ATTEMPT FAILED — FIX IT ===\n" +
                "Here is what you returned:\n" + JsonSerializer.Serialize(priorAttempt) +
                "\n\nThe error was:\n" + Trim(priorFailure, 2500) +
                "\nIf the error JSON contains a `valid_anchors` array, use one of those EXACT {mode,symbol/anchor} " +
                "pairs. For adding a member, prefer the `append-into-symbol` entry. Other common causes: anchor was " +
                "inside a multi-line statement (pick a complete line ending in `;`), a duplicate definition, or a missing using.";
        // Inject the REAL test fixture (the `_client` field + existing usings + one sample [Fact]) so the
        // model copies the proven pattern instead of guessing `_factory` / spinning its own factory — the
        // CS0103/CS1061 first-attempt-failure class. Read from source (same approach as RealEndpointAnchors).
        var fixtureCtx = RealTestFixture();
        var user =
            $"Ticket #{ticket}: {title}\n\n{body}\n\nApproved plan:\n{plan}\n\n" +
            // Keep the injected context SMALL — a large recalled blob made gpt-oss emit longer, less
            // reliable JSON (unescaped chars). A few hundred chars is enough to show one anchor pattern.
            $"=== .said recall: an existing endpoint line to anchor after (copy a COMPLETE .AllowAnonymous(); line) ===\n{Trim(routeCtx, 700)}\n" +
            (string.IsNullOrWhiteSpace(fixtureCtx) ? "" : $"\n=== THE TEST FIXTURE (use `_client`; do not reinvent it) ===\n{Trim(fixtureCtx, 900)}\n") +
            repair;
        // JsonObject=true → provider is forced to emit valid JSON (json_object mode). This is the real
        // fix for the flaky parsing: stop hoping the model returns clean JSON, make the API guarantee it.
        var rr = await runner.RunAsync(agent, new AgentRunRequest("execution", agent.Persona ?? "", sys, user, JsonObject: true), ct);
        if (!rr.Ok) { log.LogWarning("Groq execution call failed: {err}", rr.Error); return null; }
        var raw = rr.Text ?? "";
        // Write the EXACT bytes we are about to parse, BEFORE parsing, and log the first chars so file and
        // parse can't diverge. Decisive instrumentation.
        try { File.WriteAllText("/tmp/groq-raw.txt", raw); } catch { }
        log.LogWarning("Groq exec raw: len={len} c0={c0} c1={c1} c2={c2}", raw.Length,
            raw.Length > 0 ? (int)raw[0] : -1, raw.Length > 1 ? (int)raw[1] : -1, raw.Length > 2 ? (int)raw[2] : -1);
        var cs = ParseChangeSet(raw, out var note);
        if (cs is null)
        {
            try { File.WriteAllText("/tmp/groq-unparseable.txt", rr.Text ?? ""); } catch { }
            log.LogWarning("Groq execution UNPARSEABLE. ok={ok} len={len} reason={note} (full at /tmp/groq-unparseable.txt)",
                rr.Ok, rr.Text?.Length ?? 0, note);
        }
        else if (string.IsNullOrWhiteSpace(priorFailure)) SaidRemember($"Groq cycle #{ticket}: {cs.summary}");
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

    static ChangeSet? ParseChangeSet(string text, out string note)
    {
        note = "";
        if (string.IsNullOrWhiteSpace(text)) { note = "empty reply"; return null; }
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var t = text.Trim();
        // FAST PATH: json_object mode returns a clean object — try the raw text directly first.
        try
        {
            var direct = JsonSerializer.Deserialize<ChangeSet>(t, opts);
            if (direct is not { edits: not null } || direct.edits.Count == 0) note = "direct: deserialized but edits null/empty";
            else
            {
                var bad = direct.edits.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.file) || !ValidModes.Contains(e.mode ?? "")
                                       || e.content is null
                                       || (string.IsNullOrWhiteSpace(e.anchor) && string.IsNullOrWhiteSpace(e.symbol)));
                if (bad is null) return direct;
                note = $"direct: validation rejected mode='{bad.mode}' file='{bad.file}' anchorEmpty={string.IsNullOrWhiteSpace(bad.anchor)} symbolEmpty={string.IsNullOrWhiteSpace(bad.symbol)} contentNull={bad.content is null}";
                return null;  // don't fall through to escapers and lose the real reason
            }
        }
        catch (Exception ex) { note = "direct parse threw: " + ex.Message[..Math.Min(80, ex.Message.Length)]; }
        // Strip a ```json fence if present, then try candidate extractions.
        if (t.StartsWith("```"))
        {
            int nl = t.IndexOf('\n'); if (nl > 0) t = t[(nl + 1)..];
            int fence = t.LastIndexOf("```"); if (fence > 0) t = t[..fence];
        }
        foreach (var cand0 in CandidateJsons(t))
        {
            // With json_object mode the provider returns valid JSON; the unescape/escape variants are
            // belt-and-suspenders for any odd model. Try each; first valid+validated change set wins.
            foreach (var cand in new[] { cand0, MaybeUnescape(cand0), EscapeRawControlCharsInStrings(cand0) })
            {
                try
                {
                    var cs = JsonSerializer.Deserialize<ChangeSet>(cand, opts);
                    if (cs is not { edits: not null } || cs.edits.Count == 0) { note = "deserialized but no edits"; continue; }
                    var bad = cs.edits.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.file) || !ValidModes.Contains(e.mode ?? "")
                                          || e.content is null
                                          || (string.IsNullOrWhiteSpace(e.anchor) && string.IsNullOrWhiteSpace(e.symbol)));
                    if (bad is null) return cs;
                    note = $"validation rejected edit: mode={bad.mode} file={bad.file} anchorEmpty={string.IsNullOrWhiteSpace(bad.anchor)} symbolEmpty={string.IsNullOrWhiteSpace(bad.symbol)} contentNull={bad.content is null}";
                }
                catch (Exception ex) { note = "deserialize threw: " + ex.Message[..Math.Min(120, ex.Message.Length)]; }
            }
        }
        return null;
    }

    /// <summary>Some responses come back as ESCAPED JSON text (literal `\n`, `\"` sequences) — i.e. a
    /// JSON-string-encoded object rather than a raw object (STJ then errors "'\' is an invalid start of
    /// a property name"). If the candidate contains `\"` (escaped quotes) but isn't already a quoted
    /// string, JSON-unescape the backslash sequences so it becomes a real object.</summary>
    static string MaybeUnescape(string s)
    {
        if (!s.Contains("\\\"") && !s.Contains("\\n")) return s;   // nothing escaped
        // Single left-to-right pass so we don't re-interpret characters produced by an earlier replace.
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\', _ => n });
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>Escape raw newlines/tabs/CRs that appear INSIDE JSON string values (LLMs emit them
    /// unescaped, which is invalid JSON). Walks the text tracking string context; control chars inside a
    /// string become \n / \t / \r; everything outside strings is untouched. Makes lenient JSON parseable.</summary>
    static string EscapeRawControlCharsInStrings(string s)
    {
        var sb = new StringBuilder(s.Length + 32);
        bool inStr = false, esc = false;
        foreach (var ch in s)
        {
            if (inStr)
            {
                if (esc) { sb.Append(ch); esc = false; continue; }
                if (ch == '\\') { sb.Append(ch); esc = true; continue; }
                if (ch == '"') { sb.Append(ch); inStr = false; continue; }
                switch (ch)
                {
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            else
            {
                if (ch == '"') inStr = true;
                sb.Append(ch);
            }
        }
        return sb.ToString();
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
            // Surface the ACTUAL error into the run log (not just "didn't build/test"), so each
            // first-attempt failure leaves per-run evidence of WHY (compiler/anchor error), instead
            // of discarding it. `r.detail` is already tagged + tail-trimmed; keep the log line compact.
            progress?.Invoke("fix",
                $"attempt {attempt + 1}/{maxRepairs} failed — asking Groq to fix. Error: {FirstErrorLine(r.detail)}");
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
