using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using Advisory.Api.Policy;

namespace Advisory.Api.Agents;

/// <summary>Token usage for a single agent run (so the dashboard can show real cost per phase).</summary>
public record TokenUsage(int Prompt, int Completion, int Total)
{
    public static readonly TokenUsage Zero = new(0, 0, 0);
}

/// <summary>Result of running one phase on one agent: the text it produced + token usage + which agent.</summary>
public record AgentRunResult(string Text, string AgentId, string Model, TokenUsage Usage, bool Ok = true, string? Error = null);

/// <summary>A request to run a single phase on a routed agent.</summary>
public record AgentRunRequest(string Phase, string Persona, string Instructions, string UserMessage);

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
        // Resolve the credential: agent key, else env (Groq/OpenAI), else stub.
        var key = !string.IsNullOrWhiteSpace(agent.ApiKey) ? agent.ApiKey
            : cfg["GROQ_API_KEY"] ?? cfg["PKGFW_GROQ_API_KEY"] ?? cfg["OPENAI_API_KEY"] ?? cfg["ANTHROPIC_API_KEY"];

        // claude-cli / cursor-cli are driven by the local worker's CLI, not an API client — the
        // orchestrator handles those out-of-band; here we only run API-standard agents (openai/anthropic).
        if (agent.Standard is "claude-cli" or "cursor-cli" || string.IsNullOrWhiteSpace(key))
        {
            return Stub(agent, request, string.IsNullOrWhiteSpace(key) ? "no API key — stubbed" : $"{agent.Standard} runs via the local CLI");
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
                ChatOptions = new ChatOptions { Instructions = instructions }
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
