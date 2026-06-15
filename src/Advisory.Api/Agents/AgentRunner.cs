using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using Advisory.Api.Policy;
using System.Text.Json;

namespace Advisory.Api.Agents;

/// <summary>Token usage for a single agent run (so the dashboard can show real cost per phase).</summary>
public record TokenUsage(int Prompt, int Completion, int Total)
{
    public static readonly TokenUsage Zero = new(0, 0, 0);
}

/// <summary>Result of running one phase on one agent: the text it produced + token usage + which agent.</summary>
public record AgentRunResult(string Text, string AgentId, string Model, TokenUsage Usage, bool Ok = true, string? Error = null, string? Reasoning = null);

/// <summary>A request to run a single phase on a routed agent. JsonObject=true forces the provider to
/// return valid JSON (OpenAI/Groq "json_object" response_format) — the market-standard way to get
/// reliable structured output instead of hand-parsing free-form text.</summary>
public record AgentRunRequest(string Phase, string Persona, string Instructions, string UserMessage, bool JsonObject = false);

/// <summary>Application contract — the orchestrator depends on this, never on MAF types directly
/// (clean-architecture boundary, mirroring BeapiGlobalAiService's IAgentRunner).</summary>
public interface IAgentRunner
{
    Task<AgentRunResult> RunAsync(AiAgent agent, AgentRunRequest request, CancellationToken ct = default);
}

/// <summary>
/// Microsoft Agent Framework runner. Turns an <see cref="AiAgent"/> (provider/model/endpoint/key +
/// persona) into a MAF <c>ChatClientAgent</c> whose Instructions are the agent's persona, runs the
/// phase, and returns text + token usage. All MAF types are confined to this class.
/// Falls back to a deterministic stub when the agent has no usable credential (lets the graph run
/// end-to-end without a key, just like Beapi's mock path).
/// </summary>
public sealed class MafAgentRunner(IConfiguration cfg, ILogger<MafAgentRunner> log) : IAgentRunner
{
    public async Task<AgentRunResult> RunAsync(AiAgent agent, AgentRunRequest request, CancellationToken ct = default)
    {
        // Resolve the credential: agent key, else env. For an OpenRouter endpoint, prefer
        // OPENROUTER_API_KEY so it doesn't accidentally send the Groq key to OpenRouter.
        var isOpenRouter = (agent.Endpoint ?? "").Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase);
        var key = !string.IsNullOrWhiteSpace(agent.ApiKey) ? agent.ApiKey
            : isOpenRouter ? (cfg["OPENROUTER_API_KEY"] ?? cfg["OPENAI_API_KEY"])
            : cfg["GROQ_API_KEY"] ?? cfg["PKGFW_GROQ_API_KEY"] ?? cfg["OPENAI_API_KEY"] ?? cfg["ANTHROPIC_API_KEY"];

        // claude-cli / cursor-cli are driven by the local worker's CLI, not an API client — the
        // orchestrator handles those out-of-band; here we only run API-standard agents (openai/anthropic).
        if (agent.Standard is "claude-cli" or "cursor-cli" || string.IsNullOrWhiteSpace(key))
        {
            return Stub(agent, request, string.IsNullOrWhiteSpace(key) ? "no API key — stubbed" : $"{agent.Standard} runs via the local CLI");
        }

        // Reasoning path: when the agent has reasoning ON and the provider supports it (Groq /
        // OpenRouter, OpenAI-compatible), call directly so we can send reasoning_effort AND capture
        // the model's THINKING (a separate `reasoning` field the OpenAI SDK drops). Off by default.
        var epLower = (agent.Endpoint ?? "").ToLowerInvariant();
        var reasoningCapable = epLower.Contains("api.groq.com") || epLower.Contains("openrouter.ai");
        if (agent.Reasoning && reasoningCapable)
        {
            try { return await RunWithReasoningAsync(agent, request, key!, epLower.Contains("openrouter.ai"), ct); }
            catch (Exception ex) { log.LogWarning(ex, "reasoning path failed for {Agent}; falling back to MAF", agent.Id); }
        }

        try
        {
            IChatClient chat = BuildChatClient(agent, key!);
            var instructions = string.IsNullOrWhiteSpace(agent.Persona)
                ? request.Instructions
                : agent.Persona + "\n\n" + request.Instructions;

            var ai = chat.AsAIAgent(new ChatClientAgentOptions
            {
                Name = agent.Id,
                // High output cap so code-generation JSON isn't truncated. When the caller needs
                // structured output, set ResponseFormat=Json — this is OpenAI/Groq "json_object" mode,
                // which FORCES the provider to return syntactically valid JSON (the market-standard fix
                // for "model returned free-form text"; replaces fragile hand-parsing of prose).
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    MaxOutputTokens = 16000,
                    ResponseFormat = request.JsonObject ? ChatResponseFormat.Json : null,
                }
            }) as ChatClientAgent;
            if (ai is null) return new AgentRunResult("", agent.Id, agent.Model, TokenUsage.Zero, false, "MAF did not return a ChatClientAgent");

            var resp = await ai.RunAsync(new[] { new ChatMessage(ChatRole.User, request.UserMessage) }, cancellationToken: ct)
                .ConfigureAwait(false);

            return new AgentRunResult(resp.Text ?? "", agent.Id, agent.Model, MapUsage(resp.Usage));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "MAF run failed for agent {Agent} in phase {Phase}", agent.Id, request.Phase);
            return new AgentRunResult("", agent.Id, agent.Model, TokenUsage.Zero, false, ex.Message);
        }
    }

    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    /// <summary>Direct OpenAI-compatible call with reasoning enabled, capturing both the answer
    /// (`content`) and the model's THINKING (`reasoning`). Groq uses `reasoning_effort`; OpenRouter
    /// uses `reasoning: { effort }`. Used only when the agent has Reasoning=on.</summary>
    async Task<AgentRunResult> RunWithReasoningAsync(AiAgent agent, AgentRunRequest request, string key, bool isOpenRouter, CancellationToken ct)
    {
        var baseUrl = (agent.Endpoint ?? "https://api.openai.com/v1").TrimEnd('/');
        var instructions = string.IsNullOrWhiteSpace(agent.Persona) ? request.Instructions : agent.Persona + "\n\n" + request.Instructions;
        var body = new Dictionary<string, object?>
        {
            ["model"] = agent.Model,
            ["max_tokens"] = 16000,
            ["messages"] = new object[]
            {
                new { role = "system", content = instructions },
                new { role = "user", content = request.UserMessage },
            },
        };
        if (request.JsonObject) body["response_format"] = new { type = "json_object" };
        // Provider-specific reasoning param (medium effort = a useful, not excessive, thinking budget).
        if (isOpenRouter) body["reasoning"] = new { effort = "medium" };
        else body["reasoning_effort"] = "medium";

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {key}");
        if (isOpenRouter) { req.Headers.Add("HTTP-Referer", "https://advisory.local"); req.Headers.Add("X-Title", "Advisory"); }
        req.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return new AgentRunResult("", agent.Id, agent.Model, TokenUsage.Zero, false, $"{(int)resp.StatusCode}: {Trim(raw, 300)}");

        using var doc = JsonDocument.Parse(raw);
        var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        var content = msg.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        var reasoning = msg.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        TokenUsage usage = TokenUsage.Zero;
        if (doc.RootElement.TryGetProperty("usage", out var u))
        {
            int pt = u.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
            int ctk = u.TryGetProperty("completion_tokens", out var co) ? co.GetInt32() : 0;
            usage = new TokenUsage(pt, ctk, u.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : pt + ctk);
        }
        return new AgentRunResult(content, agent.Id, agent.Model, usage, true, null, reasoning);
    }
    static string Trim(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s[..n] + "…";

    /// <summary>AiAgent → IChatClient. OpenAI-compatible (Groq, on-prem gpt-oss, OpenAI) via a custom
    /// endpoint; Anthropic via its OpenAI-compatible endpoint. Mirrors Beapi's OpenAiMafChatClientFactory.</summary>
    static IChatClient BuildChatClient(AiAgent agent, string key)
    {
        var endpoint = !string.IsNullOrWhiteSpace(agent.Endpoint)
            ? agent.Endpoint!
            : agent.Standard == "anthropic" ? "https://api.anthropic.com/v1" : "https://api.openai.com/v1";
        return new OpenAIClient(
                new System.ClientModel.ApiKeyCredential(key),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .GetChatClient(agent.Model)
            .AsIChatClient();
    }

    static AgentRunResult Stub(AiAgent a, AgentRunRequest r, string why) =>
        new($"[{a.Name} · {r.Phase}] {why}. (Would run with persona + .said context.)", a.Id, a.Model, TokenUsage.Zero);

    static TokenUsage MapUsage(UsageDetails? u)
    {
        if (u is null) return TokenUsage.Zero;
        int p = (int)(u.InputTokenCount ?? 0), c = (int)(u.OutputTokenCount ?? 0);
        return new TokenUsage(p, c, (int)(u.TotalTokenCount ?? p + c));
    }
}
