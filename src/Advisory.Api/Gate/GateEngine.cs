using Advisory.Api.Audit;
using Advisory.Api.Catalog;
using Advisory.Api.Integrations;
using Advisory.Api.Auth;
using Advisory.Api.Models;
using Advisory.Api.Policy;
using Advisory.Api.Research;
using Advisory.Api.Resolve;
using Advisory.Api.Scan;
using Advisory.Api.VulnSources;

namespace Advisory.Api.Gate;

public interface IGateEngine
{
    Task<GateResult> EvaluateAsync(PackageRef pkg, CancellationToken ct);
}

/// <summary>
/// resolve tree -> query every enabled source (health-aware) -> enrich -> evaluate policy
/// -> derive coverage -> quarantine if a REQUIRED source was inconclusive -> agent rationale -> audit.
/// A feed failure becomes uncertainty (Quarantine), never a silent Allow.
/// </summary>
public class GateEngine : IGateEngine
{
    private readonly IEnumerable<IVulnSource> _sources;
    private readonly IEnumerable<IDependencyResolver> _resolvers;
    private readonly KevSource _kev;
    private readonly EpssSource _epss;
    private readonly PickleScanner _pickle;
    private readonly SecretScanner _secrets;
    private readonly IacScanner _iac;
    private readonly ReachabilityAnalyzer _reach;
    private readonly OpRiskService _opRisk;
    private readonly IPolicyStore _policy;
    private readonly IAuditLog _audit;
    private readonly IResearchAgent _agent;
    private readonly IItsmNotifier _itsm;
    private readonly ICurrentUser _user;

    public GateEngine(IEnumerable<IVulnSource> sources, IEnumerable<IDependencyResolver> resolvers,
                      KevSource kev, EpssSource epss, PickleScanner pickle, SecretScanner secrets,
                      IacScanner iac, ReachabilityAnalyzer reach, OpRiskService opRisk, IPolicyStore policy,
                      IAuditLog audit, IResearchAgent agent, IItsmNotifier itsm, ICurrentUser user)
    {
        _sources = sources; _resolvers = resolvers; _kev = kev; _epss = epss;
        _pickle = pickle; _secrets = secrets; _iac = iac; _reach = reach; _opRisk = opRisk;
        _policy = policy; _audit = audit; _agent = agent; _itsm = itsm; _user = user;
    }

    /// <summary>
    /// Curation-style conditions on the ROOT package, mirroring JFrog Curation + Xray operational
    /// risk: immature version (SEC-SC-01), prohibited license (LEG-LIC-01), operational risk
    /// (SEC-OPR-01: EOL/deprecated, version age, # new versions, cadence health), OpenSSF
    /// scorecard floor (SEC-OSSF-01). One registry call; unsupported ecosystems are recorded as
    /// Skipped — never silently treated as clean.
    /// </summary>
    private async Task<(SourceCoverage Coverage, OperationalRisk? Risk)> EvaluateCuration(
        PackageRef root, FirewallPolicy p, List<string> triggered,
        System.Runtime.CompilerServices.StrongBox<GateDecision> decision, CancellationToken ct)
    {
        if (!_opRisk.Supports(root.Ecosystem))
            return (new SourceCoverage("package-intel", "Skipped", 0,
                $"operational-risk / license intel not available for {root.Ecosystem} (registry exposes no version-date metadata)", 0, false), null);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var risk = await _opRisk.AnalyzeAsync(root.Ecosystem, root.Name, root.Version, ct);
        if (risk is null)
            return (new SourceCoverage("package-intel", "Errored", 0,
                "registry metadata unavailable — operational/license dimensions not verified", sw.ElapsedMilliseconds, false), null);

        int hits = 0;

        // SEC-SC-01 — immature version (JFrog Curation "package version is immature").
        if (p.MinPackageAgeDays > 0 && OpRiskService.VersionAgeDays(risk) is double ageDays && ageDays < p.MinPackageAgeDays)
        { decision.Value = GateDecision.Block; triggered.Add($"SEC-SC-01:IMMATURE:{ageDays:0}d<{p.MinPackageAgeDays}d"); hits++; }

        // LEG-LIC-01 — prohibited license (was a declared-but-unenforced policy field).
        if (risk.License is { Length: > 0 } lic &&
            p.LicenseBlocklist.Any(b => lic.Contains(b, StringComparison.OrdinalIgnoreCase)))
        { decision.Value = GateDecision.Block; triggered.Add($"LEG-LIC-01:LICENSE:{risk.License}"); hits++; }

        // SEC-OPR-01 — operational risk High (EOL, stale, unhealthy project).
        if (risk.Severity == "High" && !p.OperationalRiskAction.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            hits++;
            if (p.OperationalRiskAction.Equals("Block", StringComparison.OrdinalIgnoreCase))
            { decision.Value = GateDecision.Block; triggered.Add($"SEC-OPR-01:OPRISK:{risk.RiskReason}"); }
            else triggered.Add($"SEC-OPR-01:NOTIFY:{risk.RiskReason}"); // recorded, does not block
        }

        // SEC-OSSF-01 — OpenSSF scorecard floor (JFrog Curation OpenSSF condition). Opt-in.
        if (p.MinScorecardScore > 0)
        {
            var score = await _opRisk.ScorecardScoreAsync(risk.RepoUrl, ct);
            if (score is double sc2 && sc2 < p.MinScorecardScore)
            { decision.Value = GateDecision.Block; triggered.Add($"SEC-OSSF-01:SCORECARD:{sc2:0.0}<{p.MinScorecardScore:0.0}"); hits++; }
        }

        return (new SourceCoverage("package-intel", "Ok", hits,
            $"op-risk={risk.Severity}{(risk.RiskReason is null ? "" : $" ({risk.RiskReason})")}, license={risk.License ?? "unknown"}, version-age={risk.VersionAgeMonths?.ToString("0.0") ?? "?"}mo",
            sw.ElapsedMilliseconds, false), risk);
    }

    /// <summary>
    /// Runs content-based scans (secrets, IaC misconfig) when artifact bytes are available via
    /// LocalPath. Adds triggered controls and returns a coverage row reflecting whether the scan
    /// actually ran. When no content is present (coordinate-only eval), it is recorded as Skipped —
    /// never silently treated as clean.
    /// </summary>
    private SourceCoverage ScanArtifactContent(PackageRef pkg, FirewallPolicy p, List<string> triggered, ref GateDecision decision)
    {
        if (!p.EnableContentScan)
            return new SourceCoverage("content-scan", "Skipped", 0, "content scanning disabled in policy", 0, false);
        if (string.IsNullOrEmpty(pkg.LocalPath) || !File.Exists(pkg.LocalPath))
            return new SourceCoverage("content-scan", "Skipped", 0,
                "no artifact bytes available at evaluation (coordinate-only) — secrets/IaC not verified", 0, p.RequiredSources.Contains("content-scan"));

        string text;
        try { text = File.ReadAllText(pkg.LocalPath); }
        catch (Exception ex) { return new SourceCoverage("content-scan", "Errored", 0, ex.Message, 0, p.RequiredSources.Contains("content-scan")); }

        int hits = 0;
        foreach (var s in _secrets.Scan(text))
        { decision = GateDecision.Block; triggered.Add($"SEC-SECRET-01:{s.Rule}:{s.Detail}"); hits++; }
        foreach (var i in _iac.Scan(text))
        {
            hits++;
            if (i.Severity >= Severity.High) { decision = GateDecision.Block; triggered.Add($"SEC-IAC-01:{i.Rule}:{i.Detail}"); }
            else triggered.Add($"SEC-IAC-01:{i.Rule}:{i.Detail}"); // recorded; sub-High does not block by itself
        }
        return new SourceCoverage("content-scan", hits == 0 ? "Empty" : "Ok", hits, "secrets + IaC misconfiguration scan", 0, p.RequiredSources.Contains("content-scan"));
    }

    /// <summary>
    /// Runs npm contextual analysis when a consuming ProjectPath is available, annotating each
    /// collected finding with Reachable / NotReachable / Unknown. Mutates the list in place.
    /// Returns a coverage row for the reachability dimension (Skipped when not applicable).
    /// </summary>
    private async Task<SourceCoverage> AnnotateReachability(PackageRef root, FirewallPolicy p,
        List<(Finding Finding, DepNode Node)> collected,
        Action<string, SourceStatus, int, string?, long> merge, CancellationToken ct)
    {
        if (!p.EnableReachability)
            return new SourceCoverage("reachability", "Skipped", 0, "contextual analysis disabled in policy", 0, false);
        if (!_reach.IsAvailable(root))
            return new SourceCoverage("reachability", "Skipped", 0,
                root.Ecosystem != Ecosystem.npm ? "reachability supported for npm only"
                : string.IsNullOrEmpty(root.ProjectPath) ? "no consuming project supplied — reachability not analysed"
                : "analyzer or project path unavailable", 0, false);
        if (collected.Count == 0)
            return new SourceCoverage("reachability", "Empty", 0, "no findings to analyse", 0, false);

        // One target per finding; package is the node it came from. Symbols unknown from OSV here
        // (package-level reachability) — extend with affected-symbol extraction later.
        var targets = collected.Select((c, i) =>
            new ReachabilityAnalyzer.Target($"f{i}", c.Node.Package.Name, Array.Empty<string>())).ToList();

        var verdicts = await _reach.AnalyzeAsync(root.ProjectPath!, targets, ct);
        if (verdicts.Count == 0)
            return new SourceCoverage("reachability", "Errored", 0, "analyzer returned no result", 0, false);

        int reachable = 0, notReachable = 0;
        for (int i = 0; i < collected.Count; i++)
        {
            if (verdicts.TryGetValue($"f{i}", out var v))
            {
                collected[i] = (collected[i].Finding with { Reachability = v.Reachability, ReachabilityDetail = v.Detail }, collected[i].Node);
                if (v.Reachability == "Reachable") reachable++;
                else if (v.Reachability == "NotReachable") notReachable++;
            }
        }
        return new SourceCoverage("reachability", "Ok", reachable,
            $"contextual analysis: {reachable} reachable, {notReachable} not-reachable of {collected.Count} findings", 0, false);
    }

    public async Task<GateResult> EvaluateAsync(PackageRef root, CancellationToken ct)
    {
        var p = _policy.Current;
        var triggered = new List<string>();

        var ex = p.Exceptions.FirstOrDefault(e => e.Matches(root));
        if (ex is not null)
            return await Finish(new GateResult(root, GateDecision.Allow, Array.Empty<Finding>(),
                new[] { $"EXCEPTION:{ex.Ticket}" }, ex.Ticket, DateTimeOffset.UtcNow), p, ct);

        if (root.Ecosystem == Ecosystem.HuggingFace)
            return await Finish(EvaluateWeights(root, p, triggered), p, ct);

        // Resolve full tree.
        var resolver = _resolvers.FirstOrDefault(r => r.Ecosystem == root.Ecosystem);
        var tree = resolver is not null
            ? await resolver.ResolveAsync(root, p.MaxTreeDepth, ct)
            : new List<DepNode> { new(root, 0, null) };

        // Ensure KEV catalogue is loaded; capture its health.
        var kevStatus = p.EnabledSources.Contains("kev") ? await _kev.EnsureLoaded(ct) : SourceStatus.Skipped;

        var treeFindings = new List<TreeFinding>();
        var flat = new List<Finding>();
        var decision = GateDecision.Allow;

        // Aggregate per-source health across the whole tree (worst status wins per source).
        var health = new Dictionary<string, (SourceStatus status, int findings, string? detail, long ms)>();
        void Merge(string key, SourceStatus st, int n, string? detail, long ms)
        {
            if (!health.TryGetValue(key, out var cur)) health[key] = (st, n, detail, ms);
            else health[key] = (Worse(cur.status, st), cur.findings + n, detail ?? cur.detail, cur.ms + ms);
        }
        if (p.EnabledSources.Contains("kev")) Merge("kev", kevStatus, 0, kevStatus == SourceStatus.Errored ? "KEV catalogue never loaded" : null, 0);

        // Collect enriched findings tied to their node first; blocking is derived AFTER reachability.
        var collected = new List<(Finding Finding, DepNode Node)>();
        foreach (var node in tree)
        {
            var nodeFindings = new List<Finding>();
            foreach (var src in _sources.Where(s => p.EnabledSources.Contains(s.Key)))
            {
                if (!src.IsAvailable) { Merge(src.Key, SourceStatus.NotConfigured, 0, "source not configured", 0); continue; }
                if (src.Key is "kev" or "epss") continue; // enrichment, handled separately
                var res = await src.QueryAsync(node.Package, ct);
                Merge(src.Key, res.Status, res.Findings.Count, res.Detail, res.ElapsedMs);
                nodeFindings.AddRange(res.Findings);
            }

            foreach (var f in nodeFindings)
            {
                var kev = p.EnabledSources.Contains("kev") && _kev.IsKnownExploited(f.Id);
                double? epss = f.EpssScore;
                if (epss is null && p.EnabledSources.Contains("epss"))
                {
                    var (sc, st, detail) = await _epss.ScoreAsync(f.Id, ct);
                    epss = sc; Merge("epss", st, sc is null ? 0 : 1, detail, 0);
                }
                collected.Add((f with { KnownExploited = f.KnownExploited || kev, EpssScore = epss }, node));
            }
        }

        // Contextual analysis (npm reachability): annotate findings in place, then derive blocks.
        var reachCov = await AnnotateReachability(root, p, collected, Merge, ct);

        foreach (var (enriched, node) in collected)
        {
            flat.Add(enriched);
            treeFindings.Add(new TreeFinding($"{node.Package.Name}@{node.Package.Version}", node.Depth, enriched));

            // A finding PROVEN not-reachable does not block on its own when DowngradeUnreachable is set.
            if (p.DowngradeUnreachable && enriched.Reachability == "NotReachable") continue;

            var label = node.Depth == 0 ? "" : $"[transitive d{node.Depth}:{node.Package.Name}]";
            // SEC-MAL-01 — a malicious-package advisory (OpenSSF MAL-*, or a Socket behavioural exfil
            // signal) is confirmed-bad, not a severity score. It ALWAYS blocks, regardless of CVSS,
            // source, or ecosystem. This is the control that stops a malicious package being "allowed"
            // just because it carries no high-CVSS CVE.
            if (IsMalicious(enriched))
            { decision = GateDecision.Block; triggered.Add($"SEC-MAL-01:MALICIOUS:{enriched.Id}{label}"); }
            if (p.BlockKnownExploited && enriched.KnownExploited)
            { decision = GateDecision.Block; triggered.Add($"SEC-VULN-02:KEV:{enriched.Id}{label}"); }
            if (enriched.EpssScore is double e && e >= p.EpssBlockThreshold)
            { decision = GateDecision.Block; triggered.Add($"SEC-VULN-03:EPSS:{enriched.Id}{label}"); }
            if (enriched.CvssScore is double c && c >= p.CvssBlockThreshold)
            { decision = GateDecision.Block; triggered.Add($"SEC-VULN-01:CVSS:{enriched.Id}{label}"); }
            // Severity-label gate: many advisories (e.g. GHSA via OSV) carry High/Critical labels
            // without a numeric CVSS. Map the label to its CVSS-band floor so a labelled-High
            // finding still trips a 7.0 threshold — JFrog's severity rules behave the same way.
            else if (enriched.CvssScore is null && SeverityFloor(enriched.Severity) >= p.CvssBlockThreshold)
            { decision = GateDecision.Block; triggered.Add($"SEC-VULN-01:SEVERITY:{enriched.Severity}:{enriched.Id}{label}"); }
        }

        // Content-based scans (secrets, IaC) run when artifact bytes are available (e.g. promotion bridge).
        var contentCov = ScanArtifactContent(root, p, triggered, ref decision);

        // Curation-style root-package conditions: immature version, license, operational risk, OpenSSF.
        var decisionBox = new System.Runtime.CompilerServices.StrongBox<GateDecision>(decision);
        var (curationCov, opRisk) = await EvaluateCuration(root, p, triggered, decisionBox, ct);
        decision = decisionBox.Value;

        // Build coverage report; decide if required sources were conclusive.
        var coverage = BuildCoverage(p, health, contentCov, reachCov, curationCov);
        if (decision == GateDecision.Allow && p.QuarantineOnUncertainty && !coverage.AllRequiredConclusive)
        {
            decision = GateDecision.Quarantine;
            triggered.Add("SEC-COV-02:REQUIRED_SOURCE_INCONCLUSIVE");
        }

        var purl = $"pkg:{root.Ecosystem.ToString().ToLower()}/{root.Name}@{root.Version}";
        return await Finish(new GateResult(root, decision, flat, triggered, null, DateTimeOffset.UtcNow,
            tree.Count, treeFindings, purl, coverage, null, opRisk), p, ct);
    }

    private static CoverageReport BuildCoverage(FirewallPolicy p,
        Dictionary<string, (SourceStatus status, int findings, string? detail, long ms)> health,
        SourceCoverage? contentScan = null, SourceCoverage? reachScan = null, SourceCoverage? curation = null)
    {
        var rows = new List<SourceCoverage>();
        var gaps = new List<string>();
        bool allRequiredOk = true;

        foreach (var key in p.EnabledSources)
        {
            var required = p.RequiredSources.Contains(key);
            var (status, findings, detail, ms) = health.TryGetValue(key, out var h)
                ? h : (SourceStatus.Skipped, 0, "not queried", 0L);
            rows.Add(new SourceCoverage(key, status.ToString(), findings, detail, ms, required));

            var conclusive = status is SourceStatus.Ok or SourceStatus.Empty;
            if (!conclusive)
            {
                var why = status switch
                {
                    SourceStatus.Errored => "returned an error",
                    SourceStatus.Timeout => "timed out",
                    SourceStatus.NotConfigured => "is not configured/licensed",
                    SourceStatus.Skipped => "did not run",
                    _ => "was inconclusive"
                };
                gaps.Add($"{key} {why} — its coverage dimension was not verified" +
                         (required ? " (REQUIRED for clean allow)" : ""));
                if (required) allRequiredOk = false;
            }
        }

        // Append the content-scan dimension (secrets/IaC). Only an Errored required scan breaks
        // conclusiveness; a Skipped scan (no bytes) is a known, surfaced gap, not a hard failure.
        if (contentScan is not null)
        {
            rows.Add(contentScan);
            var conclusive = contentScan.Status is "Ok" or "Empty";
            if (!conclusive)
            {
                gaps.Add($"content-scan {contentScan.Detail ?? "was inconclusive"}");
                if (contentScan.Required && contentScan.Status == "Errored") allRequiredOk = false;
            }
        }
        // Reachability is an advisory dimension — it never breaks required-conclusiveness.
        if (reachScan is not null) rows.Add(reachScan);
        // Curation intel (op-risk / license / scorecard) — advisory dimension; gaps surfaced.
        if (curation is not null)
        {
            rows.Add(curation);
            if (curation.Status is not ("Ok" or "Empty"))
                gaps.Add($"package-intel {curation.Detail ?? "was inconclusive"}");
        }
        return new CoverageReport(rows, allRequiredOk, gaps);
    }

    private GateResult EvaluateWeights(PackageRef pkg, FirewallPolicy p, List<string> triggered)
    {
        var decision = GateDecision.Allow;
        var fn = pkg.FileName ?? "";
        var isSafetensors = fn.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase);
        var isPickle = fn.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                    || fn.EndsWith(".pt", StringComparison.OrdinalIgnoreCase)
                    || fn.EndsWith(".pkl", StringComparison.OrdinalIgnoreCase)
                    || fn.EndsWith(".ckpt", StringComparison.OrdinalIgnoreCase);

        // SEC-AIML-02 — AI Catalog registry: when enforcement is on, only models an admin has
        // approved onto the allow-list may pass, regardless of format checks below.
        if (p.EnforceModelAllowList
            && !p.AllowedModels.Any(a => a.Id.Equals(pkg.Name, StringComparison.OrdinalIgnoreCase)))
        { decision = GateDecision.Block; triggered.Add("SEC-AIML-02:NOT_ON_MODEL_REGISTRY"); }

        if (p.Weights.RequireHashPin && string.IsNullOrWhiteSpace(pkg.Sha256))
        { decision = GateDecision.Block; triggered.Add("SEC-AIML-01:NO_HASH_PIN"); }
        if (p.Weights.SafetensorsOnly && !isSafetensors)
        { decision = GateDecision.Block; triggered.Add("SEC-AIML-01:NON_SAFETENSORS"); }

        if (isPickle && pkg.LocalPath is not null && File.Exists(pkg.LocalPath))
        {
            var hits = _pickle.ScanBytes(File.ReadAllBytes(pkg.LocalPath));
            foreach (var h in hits.Where(h => h.Severity >= Severity.High))
            { decision = GateDecision.Block; triggered.Add($"SEC-AIML-01:{h.Rule}:{h.Detail}"); }
        }
        else if (p.Weights.BlockPickle && isPickle)
        { decision = GateDecision.Block; triggered.Add("SEC-AIML-01:PICKLE_FORMAT"); }

        var cov = new CoverageReport(
            new[] { new SourceCoverage("weights-scan", "Ok", triggered.Count, null, 0, true) },
            true, Array.Empty<string>());
        return new GateResult(pkg, decision, Array.Empty<Finding>(), triggered, null,
            DateTimeOffset.UtcNow, 1, null, null, cov);
    }

    private async Task<GateResult> Finish(GateResult r, FirewallPolicy p, CancellationToken ct)
    {
        // AI rationale fires only when there is something to explain: a non-Allow decision,
        // any finding, any triggered control, or incomplete source coverage. A clean Allow
        // (no findings, all required sources conclusive) gets no agent call.
        string? rationale = null;
        if (p.EnableResearchAgent && HasIssues(r))
            rationale = await _agent.ExplainAsync(r, ct);
        var final = r with { ResearchRationale = rationale };

        await _audit.AppendAsync(new AuditEntry(Guid.NewGuid(), final.Package, final.Decision, final.Findings,
            final.TriggeredRules, final.ExceptionRef, p.Version, final.ComponentsEvaluated,
            final.EvaluatedAt, final.Coverage, final.ResearchRationale, _user.Name));
        await _itsm.NotifyAsync(final, ct);
        return final;
    }

    /// <summary>
    /// True when a decision has something worth explaining: not a clean Allow, has findings or
    /// triggered controls, or required source coverage was inconclusive. Drives whether the AI
    /// research agent is invoked at all.
    /// </summary>
    private static bool HasIssues(GateResult r)
        => r.Decision != GateDecision.Allow
           || r.Findings.Count > 0
           || r.TriggeredRules.Count > 0
           || (r.Coverage is { AllRequiredConclusive: false });

    /// <summary>
    /// True for a confirmed-malicious finding — an OpenSSF Malicious Packages advisory (MAL-*),
    /// GitHub's malware advisories (GHSA flagged malware via the "MAL" alias), or a Socket
    /// behavioural exfiltration/injection signal. These are categorically bad and must hard-block
    /// regardless of CVSS — a malicious package rarely carries a high numeric score.
    /// </summary>
    private static bool IsMalicious(Finding f)
        => f.Id.StartsWith("MAL-", StringComparison.OrdinalIgnoreCase)
           || f.Id.StartsWith("SOCKET-", StringComparison.OrdinalIgnoreCase)
           || (f.Aliases?.Any(a => a.StartsWith("MAL-", StringComparison.OrdinalIgnoreCase)) ?? false);

    /// <summary>CVSS-band floor for a severity label (NVD v3 bands), for advisories with no numeric score.</summary>
    private static double SeverityFloor(Severity s) => s switch
    {
        Severity.Critical => 9.0, Severity.High => 7.0, Severity.Medium => 4.0, Severity.Low => 0.1, _ => 0
    };

    private static SourceStatus Worse(SourceStatus a, SourceStatus b)
    {
        int Rank(SourceStatus s) => s switch
        {
            SourceStatus.Errored => 5, SourceStatus.Timeout => 4, SourceStatus.NotConfigured => 3,
            SourceStatus.Skipped => 2, SourceStatus.Empty => 1, SourceStatus.Ok => 0, _ => 0
        };
        return Rank(a) >= Rank(b) ? a : b;
    }
}
