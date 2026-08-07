using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Advisory.Api.Auth;
using Advisory.Api.Policy;
using Advisory.Api.Scan;

namespace Advisory.Api.Llm;

/// <summary>One intercepted LLM API call — the audit record the bank keeps.</summary>
public class LlmCallRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..10];
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Provider { get; set; } = "";      // openai | anthropic | groq
    public string Path { get; set; } = "";
    public string Model { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Decision { get; set; } = "";      // Allowed | Blocked
    public string? Reason { get; set; }              // why blocked
    public int PromptChars { get; set; }
    public int ResponseChars { get; set; }
    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }
    public int Status { get; set; }                  // upstream HTTP status (0 when blocked)
    public long DurationMs { get; set; }
    public string? Preview { get; set; }             // REDACTED prompt preview (what crossed the wire)
    public string? Original { get; set; }             // ORIGINAL prompt preview (what was attempted)
    public List<DlpFinding> Dlp { get; set; } = new(); // PII/card/secret/code findings (redacted samples)
}

/// <summary>In-memory ring buffer of intercepted LLM calls (most recent 2000).</summary>
public class LlmAuditService
{
    private readonly ConcurrentQueue<LlmCallRecord> _records = new();
    public void Add(LlmCallRecord r)
    {
        _records.Enqueue(r);
        while (_records.Count > 2000 && _records.TryDequeue(out _)) { }
    }
    public IReadOnlyList<LlmCallRecord> List(int limit = 200) =>
        _records.Reverse().Take(limit).ToList();
    public object Stats()
    {
        var all = _records.ToList();
        return new
        {
            total = all.Count,
            blocked = all.Count(r => r.Decision == "Blocked"),
            byProvider = all.GroupBy(r => r.Provider).ToDictionary(g => g.Key, g => g.Count()),
            tokensIn = all.Sum(r => (long)(r.TokensIn ?? 0)),
            tokensOut = all.Sum(r => (long)(r.TokensOut ?? 0)),
            dlpHits = all.SelectMany(r => r.Dlp).GroupBy(d => d.Category).ToDictionary(g => g.Key, g => g.Sum(x => x.Count)),
        };
    }
}

/// <summary>
/// The LLM Gateway (controls SEC-LLM-01/02): a transparent forward proxy for OpenAI, Anthropic
/// and Groq. Point any SDK's base URL at /api/llm/{provider} and calls pass through with the
/// caller's own API key — but every call is recorded, provider/model policy is enforced, and
/// outbound prompts are scanned for embedded secrets (DLP) before they leave the building.
/// </summary>
[ApiController]
[Route("api/llm")]
public class LlmGatewayController : ControllerBase
{
    // Upstream provider bases. Overridable via env (LLM_OPENAI_BASE / LLM_ANTHROPIC_BASE / LLM_GROQ_BASE)
    // so a site can point at Azure OpenAI, a regional endpoint, or a test double without a code change.
    private readonly Dictionary<string, string> Upstreams;
    // Caller headers we forward upstream (auth + protocol); everything else is dropped.
    private static readonly string[] ForwardHeaders =
        { "Authorization", "x-api-key", "anthropic-version", "anthropic-beta", "OpenAI-Organization", "Content-Type" };

    private readonly IHttpClientFactory _f;
    private readonly IPolicyStore _policy;
    private readonly LlmAuditService _audit;
    private readonly DlpInspector _dlp;
    private readonly IPrivacyFilter _pf;
    private readonly ICurrentUser _user;

    public LlmGatewayController(IHttpClientFactory f, IPolicyStore policy, LlmAuditService audit,
        DlpInspector dlp, IPrivacyFilter pf, ICurrentUser user, IConfiguration cfg)
    {
        _f = f; _policy = policy; _audit = audit; _dlp = dlp; _pf = pf; _user = user;
        Upstreams = new(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"]    = (cfg["LLM_OPENAI_BASE"]    ?? "https://api.openai.com").TrimEnd('/'),
            ["anthropic"] = (cfg["LLM_ANTHROPIC_BASE"] ?? "https://api.anthropic.com").TrimEnd('/'),
            ["groq"]      = (cfg["LLM_GROQ_BASE"]       ?? "https://api.groq.com").TrimEnd('/'),
        };
    }

    [HttpGet("/api/llm/records")]
    [Authorize(Policy = Policies.CanViewer)]
    public ActionResult Records([FromQuery] int limit = 200)
        => Ok(new { stats = _audit.Stats(), records = _audit.List(limit), policy = _policy.Current.Llm });

    /// <summary>DLP engine health — is the on-prem OpenAI Privacy Filter model loaded and ready?</summary>
    [HttpGet("/api/llm/engine")]
    [Authorize(Policy = Policies.CanViewer)]
    public async Task<ActionResult> Engine(CancellationToken ct)
    {
        var state = await _pf.StateAsync(ct);
        return Ok(new { privacyFilterConfigured = _pf.Configured, privacyFilterReady = state == "ready", privacyFilterState = state });
    }

    /// <summary>
    /// Standalone DLP redaction. Takes a block of text and returns it with every detected PII/POPIA/PCI/
    /// secret span replaced by [CATEGORY:REDACTED]. This is what CLIENT-SIDE integrations call — most
    /// importantly editor HOOKS (Cursor `beforeReadFile` / prompt hooks, Claude Code hooks): the hook sends
    /// the file/prompt text here BEFORE the editor forwards it to its AI backend, and substitutes the
    /// redacted version — so sensitive data never leaves the machine in the clear, even for tools (like
    /// Cursor's built-in AI) whose network traffic can't be intercepted. Anonymous so a lightweight hook
    /// script can call it without an auth dance; gate it at the network if needed.
    /// </summary>
    public record RedactRequest(string Text, bool? Block);
    [HttpPost("/api/dlp/redact")]
    [AllowAnonymous]
    public async Task<ActionResult> Redact([FromBody] RedactRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrEmpty(req.Text))
            return Ok(new { redacted = req?.Text ?? "", hasSensitive = false, findings = Array.Empty<object>() });
        var p = _policy.Current.Llm;
        var cfg = new DlpSettings
        {
            ScanPii = p.ScanPii, ScanCards = p.ScanCards, ScanSecrets = p.ScanSecrets, ScanCode = p.ScanCode,
            // For the hook we REDACT (never hard-block a developer's file read); Block is opt-in per call.
            BlockPii = req?.Block == true, BlockCards = req?.Block == true, BlockSecrets = req?.Block == true, BlockCode = false,
            UseAi = p.UseAiDlp, UsePrivacyFilter = p.UsePrivacyFilter,
            CustomRules = p.CustomDlpRules.Where(r => r.Enabled).Select(r => (r.Name, r.Pattern, r.Block)).ToList(),
        };
        var dlp = await _dlp.InspectAsync(req.Text, cfg, ct);
        return Ok(new
        {
            redacted = dlp.RedactedBody,
            hasSensitive = dlp.Findings.Count > 0,
            blocked = dlp.Block,
            findings = dlp.Findings.Select(f => new { f.Category, f.Rule }).Distinct(),
        });
    }

    /// <summary>CSV export of the call audit trail (compliance evidence).</summary>
    [HttpGet("/api/llm/export")]
    [Authorize(Policy = Policies.CanViewer)]
    public ActionResult Export()
    {
        var sb = new StringBuilder("timestamp,provider,model,actor,decision,reason,dlpCategories,tokensIn,tokensOut,status\n");
        foreach (var r in _audit.List(2000))
        {
            string Esc(string? v) { v ??= ""; return v.Contains(',') || v.Contains('"') ? $"\"{v.Replace("\"", "\"\"")}\"" : v; }
            var cats = string.Join("|", r.Dlp.Select(d => d.Category).Distinct());
            sb.AppendLine(string.Join(",", new[] { r.Timestamp.ToString("o"), r.Provider, Esc(r.Model), Esc(r.Actor),
                r.Decision, Esc(r.Reason), cats, (r.TokensIn ?? 0).ToString(), (r.TokensOut ?? 0).ToString(), r.Status.ToString() }));
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "llm-gateway-audit.csv");
    }

    // ── OpenAI-compatible surface (the global spec — drop-in base_url "http://host/v1") ──
    // Any OpenAI SDK / LangChain / LiteLLM client works unchanged: standard paths at root, the
    // provider is routed from the model name (LiteLLM convention: "anthropic/…", "groq/…", else
    // OpenAI). No vendor-specific path rewriting required.

    // NOTE on auth: these AI-passthrough routes are [AllowAnonymous] because the CREDENTIAL is the
    // caller's own provider key — Claude Code sends x-api-key / an OpenAI SDK sends Authorization: Bearer,
    // which the gateway forwards upstream (ForwardHeaders). The gateway itself holds no session here; it
    // is protected by NETWORK PLACEMENT (only dev machines / the reverse proxy can reach it) and by the
    // fact that a call is useless without a valid upstream key. The admin/console endpoints above keep
    // their [Authorize] policies. This is what lets a standard tool point ANTHROPIC_BASE_URL / OpenAI
    // base-URL at the gateway with no Advisory login.
    [HttpPost("/v1/chat/completions")]
    [AllowAnonymous]
    public Task<IActionResult> ChatCompletions(CancellationToken ct) => Proxy(null, "v1/chat/completions", ct);

    // Anthropic-native surface — Claude Code POSTs here. Force the "anthropic" provider so it routes to
    // api.anthropic.com/v1/messages (MapPath leaves /v1/messages as-is for anthropic).
    [HttpPost("/v1/messages")]
    [AllowAnonymous]
    public Task<IActionResult> Messages(CancellationToken ct) => Proxy("anthropic", "v1/messages", ct);

    [HttpPost("/v1/completions")]
    [AllowAnonymous]
    public Task<IActionResult> Completions(CancellationToken ct) => Proxy(null, "v1/completions", ct);

    [HttpPost("/v1/embeddings")]
    [AllowAnonymous]
    public Task<IActionResult> Embeddings(CancellationToken ct) => Proxy(null, "v1/embeddings", ct);

    [HttpPost("/v1/responses")]
    [AllowAnonymous]
    public Task<IActionResult> Responses(CancellationToken ct) => Proxy(null, "v1/responses", ct);

    /// <summary>GET /v1/models — OpenAI-spec model list (returns the providers the gateway allows).</summary>
    [HttpGet("/v1/models")]
    [Authorize(Policy = Policies.CanViewer)]
    public ActionResult Models()
    {
        var p = _policy.Current.Llm;
        // When the gateway is disabled by policy, advertise NO models — consistent with the chat
        // endpoint returning 403. A client must not discover usable models from a disabled gateway.
        if (!p.Enabled) return Ok(new { @object = "list", data = Array.Empty<object>() });
        var ids = new List<string>();
        if (p.AllowOpenAI) ids.AddRange(new[] { "gpt-4o", "gpt-4o-mini", "gpt-4.1" });
        if (p.AllowAnthropic) ids.AddRange(new[] { "anthropic/claude-3-5-sonnet", "anthropic/claude-3-5-haiku" });
        if (p.AllowGroq) ids.AddRange(new[] { "groq/llama-3.3-70b-versatile", "groq/openai/gpt-oss-120b" });
        ids.RemoveAll(id => p.BlockedModels.Any(b => id.Contains(b, StringComparison.OrdinalIgnoreCase)));
        return Ok(new { @object = "list", data = ids.Select(id => new { id, @object = "model", owned_by = "package-firewall-gateway" }) });
    }

    /// <summary>Escape hatch: force a provider via path. POST /api/llm/{provider}/{rest}.</summary>
    [HttpPost("/api/llm/{provider}/{**path}")]
    [Authorize(Policy = Policies.CanViewer)]
    public Task<IActionResult> ForwardExplicit(string provider, string path, CancellationToken ct)
        => Proxy(provider, path, ct);

    /// <summary>Shared pipeline: DLP-inspect, policy-check, then forward to the resolved provider.</summary>
    private async Task<IActionResult> Proxy(string? forcedProvider, string path, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = _policy.Current.Llm;
        if (!p.Enabled) return Blocked(new LlmCallRecord { Actor = _user.Name, Path = "/" + path }, sw, 403, "LLM gateway is disabled by policy");

        var body = await new StreamReader(Request.Body, Encoding.UTF8).ReadToEndAsync(ct);
        var rawModel = ExtractModel(body) ?? "";

        // Route provider: explicit path wins; else model prefix ("anthropic/…","groq/…"); else OpenAI.
        var (provider, model) = ResolveProvider(forcedProvider, rawModel);
        var rec = new LlmCallRecord { Provider = provider, Path = "/" + path, Actor = _user.Name, Model = model, PromptChars = body.Length };

        if (!Upstreams.TryGetValue(provider, out var upstream))
            return Blocked(rec, sw, 404, $"unknown provider '{provider}' (openai | anthropic | groq)");
        var providerAllowed = provider switch
        { "openai" => p.AllowOpenAI, "anthropic" => p.AllowAnthropic, "groq" => p.AllowGroq, _ => false };
        if (!providerAllowed) return Blocked(rec, sw, 403, $"provider '{provider}' is not allowed by policy (SEC-LLM-01)");
        if (model.Length > 0 && p.BlockedModels.Any(m => model.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return Blocked(rec, sw, 403, $"model '{model}' is on the deny-list (SEC-LLM-01)");

        // Outbound DLP (PII/POPIA-GDPR, cards, secrets, proprietary code) — record + optionally block.
        var dlpCfg = new DlpSettings
        {
            ScanPii = p.ScanPii, ScanCards = p.ScanCards, ScanSecrets = p.ScanSecrets, ScanCode = p.ScanCode,
            BlockPii = p.BlockPii, BlockCards = p.BlockCards, BlockSecrets = p.BlockSecrets, BlockCode = p.BlockCode,
            UseAi = p.UseAiDlp, UsePrivacyFilter = p.UsePrivacyFilter,
            CustomRules = p.CustomDlpRules.Where(r => r.Enabled).Select(r => (r.Name, r.Pattern, r.Block)).ToList(),
        };
        var dlp = await _dlp.InspectAsync(body, dlpCfg, ct);
        rec.Dlp = dlp.Findings;
        if (p.CaptureTranscripts) { rec.Preview = dlp.RedactedPreview; rec.Original = dlp.OriginalPreview; }
        if (dlp.Block) return Blocked(rec, sw, 403, $"outbound DLP — {dlp.BlockReason} (SEC-LLM-02)");

        // REDACT MODE: forward the REDACTED prompt (sensitive spans replaced) instead of the original, so
        // POPIA/PCI data never reaches the AI provider in the clear — while the call still succeeds. This
        // is what makes routing Cursor / Claude Code through the gateway safe. Falls back to the original
        // body when redact mode is off (the historical behaviour: scan+log, or hard-block on a Block rule).
        var outboundBody = p.RedactAndForward && dlp.Findings.Count > 0 ? dlp.RedactedBody : body;
        if (p.RedactAndForward && dlp.Findings.Count > 0) rec.Decision = "Redacted";

        // Strip the LiteLLM "provider/" prefix from the model before forwarding to the real provider.
        var fwdBody = StripModelPrefix(outboundBody, rawModel, model);
        var upstreamPath = MapPath(provider, path);
        var wantsStream = RequestWantsStream(body);   // read from the ORIGINAL body (client's stream flag)

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{upstream}/{upstreamPath}{Request.QueryString}");
        req.Content = new StringContent(fwdBody, Encoding.UTF8, "application/json");
        foreach (var h in ForwardHeaders)
            if (Request.Headers.TryGetValue(h, out var v) && h != "Content-Type")
                req.Headers.TryAddWithoutValidation(h, (string)v!);
        // Anthropic needs its version header; supply a default if the caller didn't.
        if (provider == "anthropic" && !req.Headers.Contains("anthropic-version"))
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        try
        {
            var http = _f.CreateClient("llm-gw");

            // STREAMING PASSTHROUGH. Cursor, Claude Code and most chat tools request "stream": true and read
            // a Server-Sent-Events (SSE) token stream. The INBOUND prompt was already fully inspected +
            // (in redact mode) redacted above — that's complete before the first response byte — so we can
            // relay the upstream SSE stream straight back to the client untouched. Buffering it (the old
            // behaviour) would break these tools. We read response headers first, then copy the body stream.
            if (wantsStream)
            {
                using var upResp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                rec.Status = (int)upResp.StatusCode;
                rec.Decision = rec.Decision == "Redacted" ? "Redacted" : "Allowed";
                rec.DurationMs = sw.ElapsedMilliseconds;
                _audit.Add(rec);   // record now; token counts come from usage in the final SSE chunk (best-effort)

                Response.StatusCode = (int)upResp.StatusCode;
                Response.ContentType = upResp.Content.Headers.ContentType?.ToString() ?? "text/event-stream";
                Response.Headers["Cache-Control"] = "no-cache";
                Response.Headers["X-Accel-Buffering"] = "no";   // don't let nginx buffer the SSE stream
                await using var upStream = await upResp.Content.ReadAsStreamAsync(ct);
                var buf = new byte[8192];
                int n;
                while ((n = await upStream.ReadAsync(buf, ct)) > 0)
                {
                    await Response.Body.WriteAsync(buf.AsMemory(0, n), ct);
                    await Response.Body.FlushAsync(ct);   // flush each chunk so tokens arrive live
                }
                return new EmptyResult();
            }

            using var resp = await http.SendAsync(req, ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);
            rec.Status = (int)resp.StatusCode;
            rec.ResponseChars = respBody.Length;
            (rec.TokensIn, rec.TokensOut) = ExtractUsage(respBody);
            if (rec.Decision != "Redacted") rec.Decision = "Allowed";
            rec.DurationMs = sw.ElapsedMilliseconds;
            _audit.Add(rec);
            return new ContentResult { Content = respBody, ContentType = "application/json", StatusCode = (int)resp.StatusCode };
        }
        catch (Exception ex) { return Blocked(rec, sw, 502, $"upstream error: {ex.Message}"); }
    }

    /// <summary>Does the request ask for a streaming (SSE) response? Reads the JSON "stream": true flag.</summary>
    private static bool RequestWantsStream(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("stream", out var s)
                && s.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch { return false; }
    }

    /// <summary>Resolve (provider, bareModel) from an optional forced provider + the model string.</summary>
    private static (string Provider, string Model) ResolveProvider(string? forced, string model)
    {
        if (!string.IsNullOrWhiteSpace(forced)) return (forced.ToLowerInvariant(), model);
        var lower = model.ToLowerInvariant();
        foreach (var prov in new[] { "anthropic", "groq", "openai" })
            if (lower.StartsWith(prov + "/")) return (prov, model);
        if (lower.StartsWith("claude")) return ("anthropic", model);
        return ("openai", model);   // OpenAI is the spec default
    }

    /// <summary>Translate the OpenAI-spec path to the provider's native path. OpenAI uses /v1/…;
    /// Anthropic uses /v1/messages; Groq's OpenAI-compatible API is served under /openai/v1/….</summary>
    private static string MapPath(string provider, string path)
    {
        if (provider == "anthropic" && path.EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
            return "v1/messages";                       // Anthropic's native chat endpoint
        if (provider == "groq" && path.StartsWith("v1/", StringComparison.OrdinalIgnoreCase))
            return "openai/" + path;                    // Groq: /openai/v1/chat/completions
        return path;
    }

    /// <summary>Remove the "provider/" prefix from the model field so the upstream sees its own id.</summary>
    private static string StripModelPrefix(string body, string rawModel, string routedModel)
    {
        var bare = routedModel.Contains('/') && (routedModel.StartsWith("anthropic/") || routedModel.StartsWith("groq/") || routedModel.StartsWith("openai/"))
            ? routedModel[(routedModel.IndexOf('/') + 1)..] : routedModel;
        if (bare == rawModel || string.IsNullOrEmpty(rawModel)) return body;
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(body)!;
            node["model"] = bare;
            return node.ToJsonString();
        }
        catch { return body; }
    }

    private ObjectResult Blocked(LlmCallRecord rec, System.Diagnostics.Stopwatch sw, int status, string reason)
    {
        rec.Decision = "Blocked"; rec.Reason = reason; rec.Status = 0; rec.DurationMs = sw.ElapsedMilliseconds;
        _audit.Add(rec);
        return StatusCode(status, new { error = new { message = $"Package Firewall LLM gateway: {reason}", type = "policy_blocked" } });
    }

    private static string? ExtractModel(string body)
    {
        try { using var doc = JsonDocument.Parse(body); return doc.RootElement.TryGetProperty("model", out var m) ? m.GetString() : null; }
        catch { return null; }
    }

    private static (int?, int?) ExtractUsage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("usage", out var u)) return (null, null);
            int? In = u.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32()
                : u.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : null;
            int? Out = u.TryGetProperty("completion_tokens", out var ot) ? ot.GetInt32()
                : u.TryGetProperty("output_tokens", out var o2) ? o2.GetInt32() : null;
            return (In, Out);
        }
        catch { return (null, null); }
    }
}
