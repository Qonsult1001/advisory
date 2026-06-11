using System.Text;
using System.Text.Json;
using Advisory.Api.Models;
using Advisory.Api.Policy;

namespace Advisory.Api.Research;

/// <summary>
/// Calls an LLM (Groq, OpenAI-compatible chat completions) to write a plain-language,
/// audit-grade rationale for a gate decision. It is explicitly told to account for what was
/// NOT checked (errored/missing feeds), so the record reflects informed judgement under
/// uncertainty — never "clean" inferred from a single feed's silence. Falls back to a
/// deterministic local rationale if no API key or the call fails, so the trail is never empty.
/// </summary>
public interface IResearchAgent
{
    Task<string> ExplainAsync(GateResult result, CancellationToken ct);
}

/// <summary>
/// Thin Groq client (OpenAI-compatible chat completions). Resolves credentials from the signed
/// policy first (entered via admin UI, stored server-side) and falls back to env GROQ_API_KEY.
/// Shared by the audit research agent and the "Ask AI" assistant so there is one key, one place.
/// </summary>
public interface IGroqClient
{
    bool IsConfigured { get; }
    string Model { get; }
    /// <summary>Run a chat completion. Returns (ok, text). Throws nothing — errors come back as (false, message).</summary>
    Task<(bool Ok, string Text)> ChatAsync(string systemPrompt, string userPrompt, int maxTokens, double temperature, CancellationToken ct);
    /// <summary>Validate a specific key/model/endpoint (admin "Test" — does not touch stored config).</summary>
    Task<(bool Ok, string Detail)> TestAsync(string apiKey, string model, string endpoint, CancellationToken ct);
}

public class GroqClient : IGroqClient
{
    private readonly HttpClient _http;
    private readonly IPolicyStore _policy;
    private readonly IConfiguration _cfg;

    public GroqClient(IHttpClientFactory f, IPolicyStore policy, IConfiguration cfg)
    {
        _http = f.CreateClient("groq");
        _policy = policy;
        _cfg = cfg;
    }

    private AiSettings Ai => _policy.Current.Ai;
    // Policy key wins; fall back to env (GROQ_API_KEY / legacy ANTHROPIC_API_KEY) for zero-config bootstrap.
    private string? Key => !string.IsNullOrWhiteSpace(Ai.ApiKey) ? Ai.ApiKey
        : (_cfg["GROQ_API_KEY"] ?? _cfg["ANTHROPIC_API_KEY"]);
    private string Endpoint => string.IsNullOrWhiteSpace(Ai.Endpoint) ? "https://api.groq.com/openai/v1/chat/completions" : Ai.Endpoint;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key);
    public string Model => string.IsNullOrWhiteSpace(Ai.Model) ? (_cfg["GROQ_MODEL"] ?? "openai/gpt-oss-120b") : Ai.Model;

    public async Task<(bool Ok, string Text)> ChatAsync(string systemPrompt, string userPrompt, int maxTokens, double temperature, CancellationToken ct)
    {
        var key = Key;
        if (string.IsNullOrWhiteSpace(key)) return (false, "AI is not configured. Add a Groq API key under Administration → AI assistant.");
        try
        {
            var (ok, text) = await CallAsync(key!, Model, Endpoint, systemPrompt, userPrompt, maxTokens, temperature, ct);
            return ok ? (true, text) : (false, text);
        }
        catch (Exception ex) { return (false, $"AI error: {ex.Message}"); }
    }

    public async Task<(bool Ok, string Detail)> TestAsync(string apiKey, string model, string endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return (false, "no API key supplied");
        try
        {
            var (ok, text) = await CallAsync(apiKey, string.IsNullOrWhiteSpace(model) ? Model : model,
                string.IsNullOrWhiteSpace(endpoint) ? Endpoint : endpoint,
                "You are a connectivity probe. Reply with the single word: OK.", "Reply OK.", 8, 0, ct);
            return ok ? (true, "connection OK") : (false, text);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private async Task<(bool Ok, string Text)> CallAsync(string key, string model, string endpoint,
        string systemPrompt, string userPrompt, int maxTokens, double temperature, CancellationToken ct)
    {
        var body = new
        {
            model,
            max_completion_tokens = maxTokens,
            temperature,
            top_p = 1,
            reasoning_effort = "medium",
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Add("Authorization", $"Bearer {key}");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return (false, $"HTTP {(int)resp.StatusCode}: {Truncate(raw, 240)}");
        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return (true, text ?? "");
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}

/// <summary>
/// Groq-backed research agent (audit rationale). Delegates the actual LLM call to <see cref="IGroqClient"/>
/// so the key lives in one place (the signed policy). Falls back to a deterministic local rationale.
/// (Class name kept as ClaudeResearchAgent only to avoid churn in DI/test registrations.)
/// </summary>
public class ClaudeResearchAgent : IResearchAgent
{
    private readonly IGroqClient _groq;

    public ClaudeResearchAgent(IGroqClient groq) => _groq = groq;

    public async Task<string> ExplainAsync(GateResult r, CancellationToken ct)
    {
        var local = LocalRationale(r);
        if (!_groq.IsConfigured) return local + "\n\n[agent offline: deterministic rationale]";
        var (ok, text) = await _groq.ChatAsync(SystemPrompt, BuildPrompt(r), 1024, 0.4, ct);
        if (!ok) return local + $"\n\n[{text}: deterministic rationale]";
        return string.IsNullOrWhiteSpace(text) ? local : text;
    }

    private const string SystemPrompt =
        "You are a software supply-chain risk analyst writing an audit record for a bank's package " +
        "firewall. Be precise, neutral and complete. CRITICAL: a decision must reflect what was actually " +
        "verified. If any intelligence source errored, timed out or was not configured, you MUST state that " +
        "the assessment is INCOMPLETE for that dimension and that absence of findings from a failed source " +
        "is NOT evidence of safety. Never imply a package is clean on the basis of a single source. Cover: " +
        "(1) decision summary, (2) findings across the dependency tree, (3) coverage gaps - each missing or " +
        "failed source and why it matters, (4) remediation - for each finding state the fixed-in version to " +
        "upgrade to when one is published (or note that none exists), (5) residual risk and what a reviewer " +
        "should confirm before any override. Plain prose, no markdown headers.";

    private static string BuildPrompt(GateResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Package: {r.Package.Ecosystem}:{r.Package.Name}@{r.Package.Version}");
        sb.AppendLine($"Automated decision: {r.Decision}");
        sb.AppendLine($"Components evaluated (incl. transitive): {r.ComponentsEvaluated}");
        sb.AppendLine($"Triggered controls: {(r.TriggeredRules.Count == 0 ? "none" : string.Join("; ", r.TriggeredRules))}");
        sb.AppendLine().AppendLine("Findings:");
        if (r.TreeFindings is { Count: > 0 })
            foreach (var tf in r.TreeFindings)
                sb.AppendLine($"  - {tf.Component} (depth {tf.Depth}): {tf.Finding.Id} sev={tf.Finding.Severity} " +
                    $"cvss={tf.Finding.CvssScore?.ToString() ?? "n/a"} epss={tf.Finding.EpssScore?.ToString() ?? "n/a"} " +
                    $"kev={tf.Finding.KnownExploited}" +
                    (tf.Finding.FixedVersion is { } fv ? $" fixed-in={fv}" : " fixed-in=none-published"));
        else sb.AppendLine("  none recorded");
        sb.AppendLine().AppendLine("Source coverage for THIS evaluation:");
        if (r.Coverage is not null)
        {
            foreach (var c in r.Coverage.Sources)
                sb.AppendLine($"  - {c.Source}: status={c.Status} findings={c.FindingCount} required={c.Required} " +
                    $"{(c.Detail is null ? "" : "detail=" + c.Detail)}");
            sb.AppendLine($"All required sources conclusive: {r.Coverage.AllRequiredConclusive}");
            if (r.Coverage.Gaps.Count > 0) sb.AppendLine("Gaps: " + string.Join("; ", r.Coverage.Gaps));
        }
        sb.AppendLine().AppendLine("Write the audit record now.");
        return sb.ToString();
    }

    private static string LocalRationale(GateResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Decision {r.Decision} for {r.Package.Name}@{r.Package.Version} across {r.ComponentsEvaluated} component(s).");
        sb.AppendLine(r.TriggeredRules.Count == 0
            ? "No policy controls were triggered by the recorded findings."
            : "Triggered controls: " + string.Join("; ", r.TriggeredRules) + ".");
        if (r.Coverage is { AllRequiredConclusive: false })
        {
            sb.AppendLine("ASSESSMENT INCOMPLETE: one or more required intelligence sources did not return " +
                "conclusively. Absence of findings from a failed source is not evidence of safety.");
            foreach (var g in r.Coverage.Gaps) sb.AppendLine("  gap: " + g);
        }
        else if (r.Coverage is not null)
        {
            var failed = r.Coverage.Sources.Where(c => c.Status is "Errored" or "Timeout").ToList();
            if (failed.Count > 0)
                sb.AppendLine("Note: non-required sources unavailable (" +
                    string.Join(", ", failed.Select(f => f.Source)) + "); assessment breadth reduced.");
        }
        return sb.ToString().TrimEnd();
    }
}
