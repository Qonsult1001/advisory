using System.Text;
using System.Text.Json;
using Advisory.Api.Models;

namespace Advisory.Api.Integrations;

/// <summary>
/// Fires an outbound event to the org's ITSM (Jira / ServiceNow) so IT opens/links a
/// ticket. The firewall is the system of record for the DECISION; ITSM is the system of
/// record for the APPROVAL WORKFLOW. We do not create tickets here — we emit the event
/// ITSM consumes, and the resulting ticket id is stored back on the exception (bidirectional
/// reference). This mirrors how Sonatype/Artifactory integrate: webhook out, ticket id in.
/// </summary>
public interface IItsmNotifier
{
    Task NotifyAsync(GateResult result, CancellationToken ct);
}

public class ItsmWebhook : IItsmNotifier
{
    private readonly HttpClient _http;
    private readonly string? _url;
    private readonly ILogger<ItsmWebhook> _log;

    public ItsmWebhook(IHttpClientFactory f, IConfiguration cfg, ILogger<ItsmWebhook> log)
    {
        _http = f.CreateClient("itsm");
        _url = cfg["ITSM_WEBHOOK_URL"];
        _log = log;
    }

    public async Task NotifyAsync(GateResult r, CancellationToken ct)
    {
        if (r.Decision == GateDecision.Allow) return;            // only blocks/quarantines need a workflow
        if (string.IsNullOrWhiteSpace(_url))
        { _log.LogInformation("ITSM webhook not configured; skipping ticket event for {Pkg}", r.Package.Name); return; }

        var payload = new
        {
            eventType = r.Decision == GateDecision.Quarantine ? "package.quarantined" : "package.blocked",
            component = $"{r.Package.Ecosystem}:{r.Package.Name}@{r.Package.Version}",
            decision = r.Decision.ToString(),
            triggeredControls = r.TriggeredRules,
            componentsEvaluated = r.ComponentsEvaluated,
            coverageComplete = r.Coverage?.AllRequiredConclusive ?? true,
            gaps = r.Coverage?.Gaps ?? new List<string>(),
            rationale = r.ResearchRationale,
            suggestedTicketType = r.Decision == GateDecision.Quarantine ? "Review" : "Security Block",
            occurredAt = r.EvaluatedAt
        };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _url)
            { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
            using var resp = await _http.SendAsync(req, ct);
            _log.LogInformation("ITSM webhook {Status} for {Pkg}", (int)resp.StatusCode, r.Package.Name);
        }
        catch (Exception ex) { _log.LogWarning(ex, "ITSM webhook failed for {Pkg}", r.Package.Name); }
    }
}
