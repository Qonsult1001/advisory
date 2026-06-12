using Advisory.Api.Policy;

namespace Advisory.Api.Agents;

/// <summary>One phase's result in the orchestrated graph run.</summary>
public record PhaseResult(string Phase, string AgentId, string Text, TokenUsage Usage, bool Ok, string? Error);

/// <summary>The whole graph run: per-phase results + aggregate token usage + the run mode used.</summary>
public record GraphRunResult(string Cycle, string Mode, List<PhaseResult> Phases, TokenUsage TotalUsage);

/// <summary>
/// Microsoft-Agent-Framework-style graph orchestrator. Given a cycle's routing (which agent runs
/// research / planning / execution / documentation, and sequential vs parallel), it runs each phase
/// on its assigned agent via <see cref="IAgentRunner"/> — sequentially (each phase sees the prior
/// phase's output) or in parallel (independent phases concurrently). Mirrors BeapiGlobalAiService's
/// GraphExecution: the orchestration is a real agent graph, not ad-hoc delegation.
///
/// NOTE: this drives the API-standard agents (openai/groq/anthropic) directly. claude-cli/cursor-cli
/// phases are executed by the local worker's CLI; this orchestrator marks them as CLI-routed so the
/// worker runs them, keeping one coherent graph across both execution modes.
/// </summary>
public sealed class PhaseOrchestrator(IAgentRunner runner, IPolicyStore policy, ILogger<PhaseOrchestrator> log)
{
    static readonly string[] Phases = { "research", "planning", "execution", "documentation" };

    public async Task<GraphRunResult> RunAsync(string cycle, string ticket, CancellationToken ct = default)
    {
        var admin = policy.Current.Admin;
        var routing = cycle.Equals("evolution", StringComparison.OrdinalIgnoreCase) ? admin.EvolutionRouting : admin.MutationRouting;
        var mode = string.IsNullOrWhiteSpace(routing.Mode) ? "sequential" : routing.Mode;

        string? AgentIdFor(string phase) => phase switch
        {
            "research" => routing.Research, "planning" => routing.Planning,
            "execution" => routing.Execution, "documentation" => routing.Documentation, _ => null
        };
        AiAgent? Resolve(string? id) => string.IsNullOrWhiteSpace(id) ? null : admin.Agents.FirstOrDefault(a => a.Id == id && a.Enabled);

        var results = new List<PhaseResult>();

        async Task<PhaseResult> RunPhase(string phase, string priorContext)
        {
            var agent = Resolve(AgentIdFor(phase));
            if (agent is null)
                return new PhaseResult(phase, "(default engine)", "", TokenUsage.Zero, true, null);

            var instructions =
                $"You are handling the **{phase}** phase of a mutation cycle for ticket {ticket}. " +
                "Use the shared .said project brain to recall full context (said ask/sym/grep). " +
                (string.IsNullOrWhiteSpace(priorContext) ? "" : "Prior phase output to build on:\n" + priorContext);
            var rr = await runner.RunAsync(agent, new AgentRunRequest(phase, agent.Persona ?? "", instructions, $"Ticket {ticket}: do the {phase} phase."), ct)
                .ConfigureAwait(false);
            return new PhaseResult(phase, rr.AgentId, rr.Text, rr.Usage, rr.Ok, rr.Error);
        }

        if (mode == "parallel")
        {
            // Independent phases (research + planning) fan out; then execution; then documentation.
            var firstTwo = await Task.WhenAll(RunPhase("research", ""), RunPhase("planning", "")).ConfigureAwait(false);
            results.AddRange(firstTwo);
            var ctx = string.Join("\n\n", firstTwo.Where(r => !string.IsNullOrWhiteSpace(r.Text)).Select(r => $"[{r.Phase}] {r.Text}"));
            var exec = await RunPhase("execution", ctx).ConfigureAwait(false); results.Add(exec);
            var docs = await RunPhase("documentation", exec.Text).ConfigureAwait(false); results.Add(docs);
        }
        else
        {
            var ctx = "";
            foreach (var phase in Phases)
            {
                var r = await RunPhase(phase, ctx).ConfigureAwait(false);
                results.Add(r);
                if (!string.IsNullOrWhiteSpace(r.Text)) ctx = $"[{phase}] {r.Text}";   // hand off to next phase
            }
        }

        var total = new TokenUsage(results.Sum(r => r.Usage.Prompt), results.Sum(r => r.Usage.Completion), results.Sum(r => r.Usage.Total));
        log.LogInformation("Graph run for {Cycle} ticket {Ticket}: {Mode}, {Tokens} tokens", cycle, ticket, mode, total.Total);
        return new GraphRunResult(cycle, mode, results, total);
    }
}
