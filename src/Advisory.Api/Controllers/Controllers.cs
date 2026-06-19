using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Advisory.Api.Auth;
using Advisory.Api.Audit;
using Advisory.Api.Gate;
using Advisory.Api.Models;
using Advisory.Api.Policy;
using Advisory.Api.VulnSources;
using Advisory.Api.Nexus;
using Advisory.Api.Queue;
using Advisory.Api.Research;
using Advisory.Api.Integrations;

namespace Advisory.Api.Controllers;

[ApiController]
[Route("api/gate")]
[Authorize(Policy = Policies.CanViewer)]
public class GateController : ControllerBase
{
    private readonly IGateEngine _gate;
    public GateController(IGateEngine gate) => _gate = gate;

    /// <summary>The proxy calls this before promoting a quarantined package.</summary>
    [HttpPost("evaluate")]
    public async Task<ActionResult<GateResult>> Evaluate([FromBody] PackageRef pkg, CancellationToken ct)
        => Ok(await _gate.EvaluateAsync(pkg, ct));
}

[ApiController]
[Route("api/policy")]
[Authorize(Policy = Policies.CanViewer)]
public class PolicyController : ControllerBase
{
    private readonly IPolicyStore _store;
    private readonly ICurrentUser _user;
    public PolicyController(IPolicyStore store, ICurrentUser user) { _store = store; _user = user; }

    [HttpGet]
    public ActionResult Get() => Ok(new { policy = _store.Current, signature = _store.CurrentSignature });

    [HttpPut]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Update([FromBody] FirewallPolicy policy)
    {
        var saved = await _store.UpdateAsync(policy, _user.Name);
        return Ok(new { policy = saved, signature = _store.CurrentSignature });
    }
}

[ApiController]
[Route("api/audit")]
[Authorize(Policy = Policies.CanViewer)]
public class AuditController : ControllerBase
{
    private readonly IAuditLog _audit;
    public AuditController(IAuditLog audit) => _audit = audit;

    [HttpGet]
    public ActionResult Get([FromQuery] GateDecision? decision, [FromQuery] int limit = 200)
        => Ok(_audit.Query(decision, limit));
}

/// <summary>
/// OSS package catalog — a JFrog-Catalog-style overview for any package, aggregated from free
/// public sources (npm registry + downloads, PyPI JSON, OSV vulns, CISA KEV, OpenSSF Scorecard).
/// npm + PyPI are live; other ecosystems return a "supported soon" overview.
/// </summary>
[ApiController]
[Route("api/catalog")]
[Authorize(Policy = Policies.CanViewer)]
public class CatalogController : ControllerBase
{
    private readonly Advisory.Api.Catalog.CatalogService _catalog;
    public CatalogController(Advisory.Api.Catalog.CatalogService catalog) => _catalog = catalog;

    /// <summary>The supported ecosystems and which are live today.</summary>
    [HttpGet("ecosystems")]
    public ActionResult Ecosystems() => Ok(
        Enum.GetValues<Ecosystem>().Select(e => new { ecosystem = e.ToString(), live = _catalog.IsLiveEcosystem(e) }));

    /// <summary>Search packages by name. ?ecosystem=npm&amp;q=express[&amp;limit=30]</summary>
    [HttpGet("search")]
    public async Task<ActionResult> Search([FromQuery] string ecosystem, [FromQuery] string q,
        [FromQuery] int limit = 30, [FromQuery] string? registry = null, CancellationToken ct = default)
    {
        if (!Enum.TryParse<Ecosystem>(ecosystem, true, out var eco)) eco = Ecosystem.npm;
        var hits = await _catalog.SearchAsync(eco, q?.Trim() ?? "", Math.Clamp(limit, 1, 50), ct, registry);
        return Ok(new { query = q, ecosystem = eco.ToString(), registry, count = hits.Count, results = hits });
    }

    /// <summary>Full package overview. ?ecosystem=npm&amp;name=express[&amp;version=4.18.2]</summary>
    [HttpGet("package")]
    public async Task<ActionResult> Package([FromQuery] string ecosystem, [FromQuery] string name,
        [FromQuery] string? version, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name is required" });
        if (!Enum.TryParse<Ecosystem>(ecosystem, true, out var eco)) eco = Ecosystem.npm;
        return Ok(await _catalog.OverviewAsync(eco, name.Trim(), string.IsNullOrWhiteSpace(version) ? null : version.Trim(), ct));
    }

    /// <summary>Live CVE/advisory detail by id (CVE-…, GHSA-…, PYSEC-…). Real OSV lookup +
    /// CISA-KEV exploited flag + EPSS probability. ?id=CVE-2021-44228</summary>
    [HttpGet("cve")]
    public async Task<ActionResult> Cve([FromQuery] string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "id is required" });
        return Ok(await _catalog.CveDetailAsync(id.Trim(), ct));
    }
}

/// <summary>
/// Browse the live CISA Known-Exploited Vulnerabilities (KEV) catalogue — the actual list behind
/// the "known-exploited" gate rule. Loads on demand (24h cached) and supports a text query.
/// </summary>
[ApiController]
[Route("api/kev")]
[Authorize(Policy = Policies.CanViewer)]
public class KevController : ControllerBase
{
    private readonly KevSource _kev;
    public KevController(KevSource kev) => _kev = kev;

    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] string? q, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var status = await _kev.EnsureLoaded(ct);
        return Ok(new
        {
            status = status.ToString(),
            total = _kev.Count,
            loadedAt = _kev.LoadedAt,
            entries = _kev.Browse(q, limit)
        });
    }
}

/// <summary>
/// Watches — named bindings of a rule-set to a resource scope (JFrog-style governance view).
/// Read for any viewer; edited as part of the signed policy (Admin) via PUT /api/policy.
/// </summary>
[ApiController]
[Route("api/watches")]
[Authorize(Policy = Policies.CanViewer)]
public class WatchesController : ControllerBase
{
    private readonly IPolicyStore _policy;
    public WatchesController(IPolicyStore policy) => _policy = policy;

    [HttpGet]
    public ActionResult Get() => Ok(_policy.Current.Watches);
}

/// <summary>
/// Structured policy violations — every non-Allow decision projected into a "what broke policy"
/// record (resource, worst severity, rules, status). A violation is Waived when a matching,
/// unexpired exception exists; otherwise Open. Derived from the audit ledger + current policy,
/// so it stays in lockstep with decisions without a second source of truth.
/// </summary>
[ApiController]
[Route("api/violations")]
[Authorize(Policy = Policies.CanViewer)]
public class ViolationsController : ControllerBase
{
    private readonly IAuditLog _audit;
    private readonly IPolicyStore _policy;
    public ViolationsController(IAuditLog audit, IPolicyStore policy) { _audit = audit; _policy = policy; }

    [HttpGet]
    public ActionResult Get([FromQuery] string? status, [FromQuery] int limit = 200)
    {
        var p = _policy.Current;
        // Both non-Allow decisions are violations.
        var entries = _audit.Query(GateDecision.Block, limit)
            .Concat(_audit.Query(GateDecision.Quarantine, limit))
            .OrderByDescending(e => e.Timestamp);

        var violations = entries.Select(e =>
        {
            var ex = p.Exceptions.FirstOrDefault(x => x.Matches(e.Package));
            var sev = WorstSeverity(e);
            return new Violation(
                e.Id,
                $"{e.Package.Ecosystem}:{e.Package.Name}@{e.Package.Version}",
                e.Package.Ecosystem,
                e.Decision,
                sev,
                e.TriggeredRules,
                ex is not null ? "Waived" : "Open",
                ex?.Ticket,
                e.Timestamp,
                MatchingWatch(p, e));
        });

        if (!string.IsNullOrWhiteSpace(status))
            violations = violations.Where(v => v.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

        return Ok(violations.Take(limit).ToList());
    }

    private static Severity WorstSeverity(AuditEntry e)
    {
        var fromFindings = e.Findings.Count > 0 ? e.Findings.Max(f => f.Severity) : Severity.None;
        // A Block with no scored findings (e.g. malware/pickle/secret) is still High by intent.
        return fromFindings == Severity.None && e.Decision == GateDecision.Block ? Severity.High : fromFindings;
    }

    // Policy names for watches persisted before Watch.PolicyName existed (matches UI fallback).
    private static readonly Dictionary<string, string> PolicyNameFallback = new()
    {
        ["PROD-watch"] = "Block-Promotion-On-High-Vulnerability",
        ["Security-watch"] = "Security_policy_1",
        ["License-watch"] = "license-policy",
    };

    /// <summary>
    /// Per-finding violation rows (JFrog watch-violations drill-down): one row per vulnerability /
    /// license / malicious finding inside each violating decision, attributed to a watch. Filter
    /// with ?watch=NAME. Columns mirror Xray: id, severity, type, component, impacted artifact,
    /// updated, policy.
    /// </summary>
    [HttpGet("detailed")]
    public ActionResult Detailed([FromQuery] string? watch, [FromQuery] int limit = 500)
    {
        var p = _policy.Current;
        var entries = _audit.Query(GateDecision.Block, limit)
            .Concat(_audit.Query(GateDecision.Quarantine, limit))
            .OrderByDescending(e => e.Timestamp);

        string PolicyLabel(Watch w) => !string.IsNullOrWhiteSpace(w.PolicyName) ? w.PolicyName
            : PolicyNameFallback.TryGetValue(w.Name, out var fb) ? fb : $"{w.Name}-policy";

        var rows = new List<object>();
        foreach (var e in entries)
        {
            var artifact = $"{e.Package.Ecosystem}:{e.Package.Name}@{e.Package.Version}";
            var isLicense = e.TriggeredRules.Any(r => r.Contains("LIC", StringComparison.OrdinalIgnoreCase));
            var scoped = p.Watches.Where(w => w.Enabled
                && (w.Ecosystems.Count == 0 || w.Ecosystems.Contains(e.Package.Ecosystem))).ToList();

            if (e.Findings.Count > 0)
                foreach (var f in e.Findings)
                {
                    // JFrog semantics: the finding appears under EVERY watch whose rules match it
                    // (severity threshold / KEV-only / malicious), not just the blocking one.
                    var matches = scoped.Where(w => w.Rules.Any(r => RuleMatchesFinding(r, f))).ToList();
                    foreach (var w in matches)
                    {
                        if (!string.IsNullOrWhiteSpace(watch) && !string.Equals(w.Name, watch, StringComparison.OrdinalIgnoreCase)) continue;
                        rows.Add(new
                        {
                            id = f.Id,
                            severity = f.Severity.ToString(),
                            type = "Security",
                            component = $"{e.Package.Name} : {e.Package.Version}",
                            impactedArtifact = artifact,
                            watch = w.Name,
                            policy = PolicyLabel(w),
                            knownExploited = f.KnownExploited,
                            cvss = f.CvssScore,
                            fixedVersion = f.FixedVersion,
                            updated = e.Timestamp,
                            decision = e.Decision.ToString(),
                        });
                    }
                }
            else
            {
                // No scored findings (license / secret / pickle block): attribute to watches
                // carrying the matching rule kind.
                var kind = isLicense ? "License"
                    : e.TriggeredRules.Any(r => r.Contains("MAL", StringComparison.OrdinalIgnoreCase)) ? "Malicious" : null;
                var matches = scoped.Where(w => kind is null
                    ? w.Rules.Any(r => r.Type == "CVEs")
                    : w.Rules.Any(r => r.Type == kind)).ToList();
                foreach (var w in matches)
                {
                    if (!string.IsNullOrWhiteSpace(watch) && !string.Equals(w.Name, watch, StringComparison.OrdinalIgnoreCase)) continue;
                    rows.Add(new
                    {
                        id = e.TriggeredRules.FirstOrDefault()?.Split(':')[0] ?? "POLICY",
                        severity = WorstSeverity(e).ToString(),
                        type = isLicense ? "License" : "Security",
                        component = $"{e.Package.Name} : {e.Package.Version}",
                        impactedArtifact = artifact,
                        watch = w.Name,
                        policy = PolicyLabel(w),
                        knownExploited = false,
                        cvss = (double?)null,
                        fixedVersion = (string?)null,
                        updated = e.Timestamp,
                        decision = e.Decision.ToString(),
                    });
                }
            }
        }
        return Ok(new { count = rows.Count, rows = rows.Take(limit) });
    }

    /// <summary>Does one watch rule match one finding? Mirrors JFrog rule semantics.</summary>
    private static bool RuleMatchesFinding(WatchRule r, Finding f) => r.Type switch
    {
        "CVEs" => r.KnownExploitedOnly
            ? f.KnownExploited
            : f.Severity >= ParseSeverity(r.MinSeverity),
        "Malicious" => f.Id.StartsWith("MAL-", StringComparison.OrdinalIgnoreCase),
        _ => false, // License rules match license violations, handled on the no-findings path
    };

    private static Severity ParseSeverity(string s) =>
        Enum.TryParse<Severity>(s, true, out var sev) ? sev : Severity.High;

    /// <summary>
    /// Attribute a violation to the most relevant enabled watch: one whose ecosystem scope covers
    /// this package and whose rule type matches what was triggered (license / malicious / CVE).
    /// Prefers a blocking watch. Best-effort labelling for the governance view.
    /// </summary>
    private static string? MatchingWatch(FirewallPolicy p, AuditEntry e)
    {
        var rules = string.Join(" ", e.TriggeredRules);
        var kind = rules.Contains("LIC", StringComparison.OrdinalIgnoreCase) ? "License"
            : rules.Contains("MAL", StringComparison.OrdinalIgnoreCase) ? "Malicious"
            : "CVEs";
        var candidates = p.Watches.Where(w => w.Enabled
            && (w.Ecosystems.Count == 0 || w.Ecosystems.Contains(e.Package.Ecosystem))
            && w.Rules.Any(r => r.Type == kind));
        // Prefer a watch that actually blocks this kind.
        return (candidates.FirstOrDefault(w => w.Rules.Any(r => r.Type == kind && r.Block))
                ?? candidates.FirstOrDefault())?.Name;
    }
}

[ApiController]
[Route("api/sources")]
[Authorize(Policy = Policies.CanViewer)]
public class SourcesController : ControllerBase
{
    private readonly IEnumerable<IVulnSource> _sources;
    private readonly IPolicyStore _policy;
    private readonly IHttpClientFactory _http;
    public SourcesController(IEnumerable<IVulnSource> sources, IPolicyStore policy, IHttpClientFactory http)
    { _sources = sources; _policy = policy; _http = http; }

    // Built-in source catalogue (the known integration types) — the admin "registry".
    // DefaultEndpoint = the hard-coded upstream URL each source uses out of the box; an admin can
    // override it (e.g. point at an on-prem mirror) via SourceConfig.Endpoint in the signed policy.
    // Egress = the host data leaves to. DataSent = exactly what we transmit (for the data-flow view —
    // proves we send coordinates, never your source/artifacts, to public feeds).
    private static readonly (string Key, string Label, string Scope, string Tier, bool NeedsCredential, string? CredEnv, string? DefaultEndpoint, string Egress, string DataSent)[] Catalogue =
    {
        ("osv", "OSV.dev", "Multi-ecosystem CVE", "Included", false, null, "https://api.osv.dev/v1/query",
            "api.osv.dev (Google/OpenSSF)", "Package name + version + ecosystem only. No source code, no artifact bytes."),
        ("malware", "OpenSSF Malicious Packages", "Typosquat / malicious-package", "Included", false, null, "https://api.osv.dev/v1/query",
            "api.osv.dev (OpenSSF feed via OSV)", "Package name + ecosystem only (queried as MAL-* advisories). No code."),
        ("kev", "CISA KEV", "Known-exploited catalog", "Included", false, null, "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json",
            "cisa.gov", "Nothing is sent — the full KEV catalogue is DOWNLOADED and matched locally."),
        ("epss", "EPSS (FIRST.org)", "Exploit probability", "Included", false, null, "https://api.first.org/data/v1/epss",
            "api.first.org", "A CVE id only (to fetch its exploit-probability score). No package data."),
        ("artifactory", "JFrog Artifactory scan API", "Cross-referenced CVE scan", "Included", true, "ARTIFACTORY_TOKEN", null,
            "your configured Artifactory host", "Package coordinates to your own Artifactory (self-hosted — no third party)."),
        ("vulncheck", "VulnCheck (exploited intel)", "Exploited-in-the-wild enrichment (vulncheck-kev — superset of CISA KEV)", "Included", true, "VULNCHECK_API_KEY", "https://api.vulncheck.com/v3/index/vulncheck-kev",
            "api.vulncheck.com", "A CVE id to the free vulncheck-kev index + your API key. No package data, no source code."),
        ("socket", "Socket (behavioural)", "Install-script / runtime behaviour", "Licensed", true, "SOCKET_API_KEY", "https://api.socket.dev/v0",
            "api.socket.dev", "Package name + version + your API key. Socket fetches the package itself upstream."),
        ("vsix-scanner", "Code Exfiltration Scanner (extensions)", "Deep code scan of AI-editor/VS Code extensions: data-exfiltration, RAT, credential-theft, IOC", "Included", false, null, "http://vsix-scanner:8099",
            "Self-hosted sidecar → marketplace.visualstudio.com / open-vsx.org",
            "Only an extension id goes to the LOCAL sidecar. The sidecar downloads the .vsix and analyses the code IN YOUR INFRASTRUCTURE (vsix-audit + YARA-X). The scanner vendor receives nothing; only the public Marketplace is contacted to fetch the package."),
    };

    [HttpGet]
    public ActionResult Get() => Ok(_sources.Select(s => new { s.Key, s.IsAvailable }));

    /// <summary>
    /// Admin source list: every known source type + admin-added custom feeds, joined to their
    /// policy config (enabled, has-credential, endpoint). Powers the JFrog-style sources admin.
    /// </summary>
    [HttpGet("admin")]
    public ActionResult Admin()
    {
        var p = _policy.Current;
        var live = _sources.ToDictionary(s => s.Key, s => s.IsAvailable, StringComparer.OrdinalIgnoreCase);
        var builtins = Catalogue.Select(c =>
        {
            var cfg = p.SourceConfigs.FirstOrDefault(x => x.Key.Equals(c.Key, StringComparison.OrdinalIgnoreCase));
            var hasCred = !c.NeedsCredential || !string.IsNullOrWhiteSpace(cfg?.Credential)
                          || (c.CredEnv is not null && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(c.CredEnv)));
            // vsix-scanner is a sidecar, not an IVulnSource — its availability is "is VSIX_SCANNER_URL set".
            var available = c.Key == "vsix-scanner"
                ? !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VSIX_SCANNER_URL"))
                : live.TryGetValue(c.Key, out var a) && a;
            return new {
                c.Key, c.Label, c.Scope, c.Tier, c.NeedsCredential,
                custom = false,
                enabled = p.EnabledSources.Contains(c.Key),
                required = p.RequiredSources.Contains(c.Key),
                endpoint = cfg?.Endpoint,              // admin override (null = using built-in default)
                defaultEndpoint = c.DefaultEndpoint,   // the hard-coded upstream URL
                hasCredential = hasCred,
                available,
                egress = c.Egress,                     // where data leaves to (data-flow view)
                dataSent = c.DataSent,                  // exactly what we transmit
            };
        });
        var customs = p.CustomSources.Select(cs => new {
            Key = cs.Id, cs.Label, Scope = "Custom OSV-format feed", Tier = "Custom", NeedsCredential = false,
            custom = true, cs.Enabled, cs.Required, Endpoint = cs.OsvQueryUrl,
            hasCredential = !string.IsNullOrWhiteSpace(cs.Credential), available = cs.Enabled,
        });
        return Ok(new { builtins, customs });
    }

    /// <summary>Test one built-in source against a benign package (admin "Test connection").</summary>
    [HttpPost("test/{key}")]
    public async Task<ActionResult> TestOne(string key, CancellationToken ct)
    {
        // vsix-scanner is a sidecar, not an IVulnSource — probe its /health endpoint live.
        if (key.Equals("vsix-scanner", StringComparison.OrdinalIgnoreCase))
        {
            var url = Environment.GetEnvironmentVariable("VSIX_SCANNER_URL");
            if (string.IsNullOrWhiteSpace(url))
                return Ok(new { key, ok = false, status = "NotConfigured", detail = "VSIX_SCANNER_URL not set" });
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var client = _http.CreateClient("catalog");
                client.Timeout = TimeSpan.FromSeconds(10);
                using var resp = await client.GetAsync($"{url.TrimEnd('/')}/health", ct);
                using var d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var healthy = resp.IsSuccessStatusCode && d.RootElement.TryGetProperty("ok", out var okv) && okv.ValueKind == JsonValueKind.True;
                var ver = d.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
                return Ok(new { key, ok = healthy, status = healthy ? "Ok" : "Errored",
                    elapsedMs = sw2.ElapsedMilliseconds, detail = healthy ? $"vsix-audit {ver} reachable" : "scanner returned not-ok" });
            }
            catch (Exception ex) { return Ok(new { key, ok = false, status = "Errored", detail = $"scanner unreachable: {ex.Message}" }); }
        }
        // VulnCheck is queried per-CVE (vulncheck-kev index), not per-package — probe it with a known
        // exploited CVE so Test reflects real reachability of the free index.
        if (key.Equals("vulncheck", StringComparison.OrdinalIgnoreCase)
            && _sources.FirstOrDefault(s => s.Key == "vulncheck") is VulnCheckSource vc)
        {
            if (!vc.IsAvailable) return Ok(new { key, ok = false, status = "NotConfigured", detail = "VULNCHECK_API_KEY not set" });
            var sw3 = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var hit = await vc.LookupCveAsync("CVE-2021-44228", ct);
                return hit is not null
                    ? Ok(new { key, ok = true, status = "Ok", elapsedMs = sw3.ElapsedMilliseconds, detail = $"vulncheck-kev reachable ({hit.ExploitRefCount} exploit refs for the probe CVE)" })
                    : Ok(new { key, ok = false, status = "Errored", elapsedMs = sw3.ElapsedMilliseconds, detail = "no data — key may lack the vulncheck-kev index or was rate-limited" });
            }
            catch (Exception ex) { return Ok(new { key, ok = false, status = "Errored", detail = ex.Message }); }
        }

        var src = _sources.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (src is null) return NotFound(new { error = $"unknown source '{key}'" });
        if (!src.IsAvailable) return Ok(new { key, ok = false, status = "NotConfigured", detail = "credential/endpoint not set" });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var r = await src.QueryAsync(new PackageRef(Ecosystem.npm, "lodash", "4.17.21"), ct);
            return Ok(new { key, ok = r.Status is SourceStatus.Ok or SourceStatus.Empty, status = r.Status.ToString(), elapsedMs = sw.ElapsedMilliseconds, detail = r.Detail });
        }
        catch (Exception ex) { return Ok(new { key, ok = false, status = "Errored", detail = ex.Message }); }
    }

    /// <summary>Test an arbitrary OSV-format feed URL before saving it as a custom source.</summary>
    [HttpPost("test-custom")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> TestCustom([FromBody] CustomSourceTest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Url)) return BadRequest(new { error = "url required" });
        var client = _http.CreateClient("catalog");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var body = new { version = "4.17.21", package = new { name = "lodash", ecosystem = "npm" } };
            using var msg = new HttpRequestMessage(HttpMethod.Post, req.Url) { Content = JsonContent.Create(body) };
            if (!string.IsNullOrWhiteSpace(req.Credential)) msg.Headers.Add("Authorization", $"Bearer {req.Credential}");
            using var resp = await client.SendAsync(msg, ct);
            var ok = resp.IsSuccessStatusCode;
            var hasVulns = false;
            if (ok) { try { using var d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)); hasVulns = d.RootElement.TryGetProperty("vulns", out _); } catch { } }
            return Ok(new { ok, status = ok ? (hasVulns ? "Ok (OSV-format)" : "Reachable, but no 'vulns' field") : $"HTTP {(int)resp.StatusCode}", elapsedMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex) { return Ok(new { ok = false, status = "Errored", detail = ex.Message }); }
    }
    public record CustomSourceTest(string Url, string? Credential);

    /// <summary>
    /// Live health probe: actually query each source against a benign reference package and report
    /// status + latency. Powers a real "is this feed reachable right now" view, not just config state.
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult> Health(CancellationToken ct)
    {
        var probe = new PackageRef(Ecosystem.npm, "lodash", "4.17.21");
        var results = await Task.WhenAll(_sources.Select(async s =>
        {
            if (!s.IsAvailable)
                return new { key = s.Key, status = "NotConfigured", elapsedMs = 0L, detail = "not configured / no credential" };
            try
            {
                var r = await s.QueryAsync(probe, ct);
                return new { key = s.Key, status = r.Status.ToString(), elapsedMs = r.ElapsedMs, detail = r.Detail ?? "" };
            }
            catch (Exception ex)
            {
                return new { key = s.Key, status = "Errored", elapsedMs = 0L, detail = ex.Message };
            }
        }));
        return Ok(results);
    }
}

/// <summary>
/// Nexus enforcement hook. Nexus OSS calls this (via pre-download webhook / routing
/// rule) before serving any proxied artifact. A Block response stops the download —
/// this is what makes the gate non-bypassable.
/// </summary>
[ApiController]
[Route("api/enforce")]
[Authorize(Policy = Policies.CanViewer)]
public class EnforceController : ControllerBase
{
    private readonly IGateEngine _gate;
    public EnforceController(IGateEngine gate) => _gate = gate;

    public record NexusAsset(string Format, string Name, string Version, string? FileName, string? Sha256);

    [HttpPost]
    public async Task<ActionResult> Enforce([FromBody] NexusAsset a, CancellationToken ct)
    {
        var eco = a.Format.ToLowerInvariant() switch
        {
            "pypi" => Ecosystem.PyPI, "npm" => Ecosystem.npm, "nuget" => Ecosystem.NuGet,
            "cargo" => Ecosystem.Cargo, "go" => Ecosystem.Go, _ => Ecosystem.HuggingFace
        };
        var result = await _gate.EvaluateAsync(
            new PackageRef(eco, a.Name, a.Version, a.Sha256, a.FileName), ct);

        // Nexus expects 200=allow serve, 403=block.
        return result.Decision == GateDecision.Allow
            ? Ok(new { allow = true })
            : StatusCode(403, new { allow = false, rules = result.TriggeredRules });
    }
}

/// <summary>Generates a CycloneDX SBOM by resolving the package's full tree.</summary>
[ApiController]
[Route("api/sbom")]
[Authorize(Policy = Policies.CanViewer)]
public class SbomController : ControllerBase
{
    private readonly IEnumerable<Advisory.Api.Resolve.IDependencyResolver> _resolvers;
    public SbomController(IEnumerable<Advisory.Api.Resolve.IDependencyResolver> r) => _resolvers = r;

    [HttpPost]
    public async Task<ActionResult> Generate([FromBody] PackageRef pkg, CancellationToken ct)
    {
        var resolver = _resolvers.FirstOrDefault(r => r.Ecosystem == pkg.Ecosystem);
        var tree = resolver is not null
            ? await resolver.ResolveAsync(pkg, 8, ct)
            : new List<Advisory.Api.Resolve.DepNode> { new(pkg, 0, null) };
        var sbom = Advisory.Api.Scan.SbomGenerator.CycloneDx(pkg, tree);
        return Content(sbom, "application/json");
    }
}


/// <summary>
/// Xray-style Scans List: the indexed Nexus repositories (name, format, artifact count, latest,
/// configurations) and a drill-in to a repo's artifacts. Idle/empty until NEXUS_URL is set.
/// </summary>
[ApiController]
[Route("api/scans")]
[Authorize(Policy = Policies.CanViewer)]
public class ScansController : ControllerBase
{
    private readonly INexusClient _nexus;
    private readonly Advisory.Api.Scan.ScanStore _scans;
    private readonly Advisory.Api.Scan.GitRepoScanService _gitScans;
    private readonly IPolicyStore _policy;
    private readonly ICurrentUser _user;
    public ScansController(INexusClient nexus, Advisory.Api.Scan.ScanStore scans,
        Advisory.Api.Scan.GitRepoScanService gitScans, IPolicyStore policy, ICurrentUser user)
    { _nexus = nexus; _scans = scans; _gitScans = gitScans; _policy = policy; _user = user; }

    [HttpGet("repositories")]
    public async Task<ActionResult> Repositories(CancellationToken ct)
    {
        var repos = _nexus.IsConfigured ? (await _nexus.ListRepositoriesAsync(ct)).Cast<object>().ToList() : new List<object>();
        // Surface real scanned-image repos (e.g. docker-local) from the live ScanStore — actual scans,
        // not fixtures. Any repo we have stored scans for that Nexus didn't list gets added here.
        var nexusNames = repos.Select(r => (r as dynamic)?.Name as string ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var grp in _scans.All().GroupBy(s => s.Repository))
        {
            if (nexusNames.Contains(grp.Key)) continue;
            var first = grp.First();
            repos.Add(new
            {
                Name = grp.Key,
                Format = first.Ecosystem.ToString(),
                Type = "hosted",
                IndexedArtifacts = grp.Select(s => $"{s.Name}|{s.Version}").Distinct().Count(),
                LatestArtifact = $"{first.Name}/{first.Version}",
                IndexedOn = grp.Max(s => s.ScannedAt),
            });
        }
        return Ok(new { configured = true, count = repos.Count, repositories = repos });
    }

    /// <summary>
    /// Git repositories manually linked for observation (control: SEC-SRC-01) — powers the
    /// "Git Repositories" tab in Xray Scans List. Always returns configured:true; the list is
    /// empty until an admin adds repos via POST. No external GitHub config required.
    /// </summary>
    [HttpGet("git-repositories")]
    public ActionResult GitRepositories()
    {
        var repos = _policy.Current.LinkedGitRepos;
        return Ok(new { configured = true, count = repos.Count, repositories = repos });
    }

    /// <summary>Packages tab — every real scanned package across all repos (live from the scan store).</summary>
    [HttpGet("packages")]
    public ActionResult Packages()
    {
        var pkgs = _scans.All().Select(s => new
        {
            s.Name, s.Version, Ecosystem = s.Ecosystem.ToString(), s.Repository,
            RepositoryPath = $"{s.Repository}/{s.Name}",
            Vulnerabilities = s.Vulnerabilities.Count, s.Critical, s.High, s.Verdict,
            LastScan = s.ScannedAt,
        }).ToList();
        return Ok(new { configured = true, count = pkgs.Count, packages = pkgs });
    }

    /// <summary>Builds tab — CI builds indexed for scanning. Live: from Nexus if it exposes builds,
    /// else an honest empty state (no builds indexed yet) — never seed data.</summary>
    [HttpGet("builds")]
    public ActionResult Builds()
        => Ok(new { configured = true, count = 0, builds = Array.Empty<object>(),
            hint = "No CI builds indexed yet. Builds appear here once a build is published with its build-info to a scanned repo." });

    /// <summary>Release Bundles tab — signed release bundles. Live empty state until one is created.</summary>
    [HttpGet("release-bundles")]
    public ActionResult ReleaseBundles()
        => Ok(new { configured = true, count = 0, bundles = Array.Empty<object>(),
            hint = "No release bundles yet. A release bundle is a signed, immutable set of artifacts promoted together." });

    public record LinkGitRepoRequest(string FullName, string Url, string? DefaultBranch, string? Visibility, string? Language);

    /// <summary>Link a git repository for observation (Admin). Idempotent by FullName.</summary>
    [HttpPost("git-repositories")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> LinkGitRepo([FromBody] LinkGitRepoRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Url))
            return BadRequest(new { error = "fullName and url are required" });
        var nameParts = req.FullName.Trim().Split('/');
        if (nameParts.Length != 2 || string.IsNullOrWhiteSpace(nameParts[0]) || string.IsNullOrWhiteSpace(nameParts[1]))
            return BadRequest(new { error = "fullName must be in owner/repo format (e.g. myorg/payments-api)" });
        var next = JsonSerializer.Deserialize<FirewallPolicy>(JsonSerializer.Serialize(_policy.Current))!;
        if (next.LinkedGitRepos.Any(r => r.FullName.Equals(req.FullName, StringComparison.OrdinalIgnoreCase)))
            return Ok(new { linked = true, already = true });
        next.LinkedGitRepos.Add(new LinkedGitRepo
        {
            FullName = req.FullName,
            Url = req.Url,
            DefaultBranch = req.DefaultBranch ?? "main",
            Visibility = req.Visibility ?? "private",
            Language = req.Language,
        });
        await _policy.UpdateAsync(next, _user.Name);
        return Ok(new { linked = true, count = next.LinkedGitRepos.Count });
    }

    /// <summary>Unlink a git repository (Admin). Uses a catch-all route so slashes in FullName are preserved.</summary>
    [HttpDelete("git-repositories/{*fullName}")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> UnlinkGitRepo(string fullName, CancellationToken ct)
    {
        var next = JsonSerializer.Deserialize<FirewallPolicy>(JsonSerializer.Serialize(_policy.Current))!;
        var removed = next.LinkedGitRepos.RemoveAll(r => r.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) await _policy.UpdateAsync(next, _user.Name);
        return Ok(new { removed });
    }

    /// <summary>
    /// Start a scan of a linked git repository (control SEC-SRC-01): fetch manifest files
    /// (package.json, requirements.txt) and evaluate declared dependencies through the gate.
    /// Returns 404 if the repo is not linked — only explicitly approved repos are scanned.
    /// The scan runs asynchronously; poll GET .../scan for results.
    /// </summary>
    [HttpPost("git-repositories/{owner}/{repo}/scan")]
    public ActionResult StartGitRepoScan(string owner, string repo)
    {
        var fullName = $"{owner}/{repo}";
        var linked = _policy.Current.LinkedGitRepos
            .Any(r => r.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));
        if (!linked) return NotFound(new { error = $"Repository '{fullName}' is not linked. Link it first via POST /api/scans/git-repositories." });
        var result = _gitScans.Start(fullName);
        return Accepted(result);
    }

    /// <summary>Retrieve the stored scan result for a linked git repository. 404 if no scan has been run yet.</summary>
    [HttpGet("git-repositories/{owner}/{repo}/scan")]
    public ActionResult GetGitRepoScan(string owner, string repo)
    {
        var fullName = $"{owner}/{repo}";
        var result = _gitScans.Get(fullName);
        return result is null ? NotFound(new { error = "No scan result yet. Trigger one via POST .../scan." }) : Ok(result);
    }

    /// <summary>
    /// Artifacts in a repo, each joined to its STORED scan (counts + last-scan time) where one
    /// exists — so the table shows real indexed results, not "scan on open".
    /// </summary>
    [HttpGet("repository/{repo}/artifacts")]
    public async Task<ActionResult> Artifacts(string repo, CancellationToken ct)
    {
        var items = _nexus.IsConfigured ? await _nexus.ListComponentsAsync(repo, ct) : (IReadOnlyList<NexusComponent>)Array.Empty<NexusComponent>();
        var artifacts = items.Select(a =>
        {
            var scan = _scans.Get(repo, a.Name, a.Version);
            return (object)new
            {
                a.Name, a.Version, a.Ecosystem, a.FileName, RepositoryPath = $"{repo}/{a.Name}",
                Scanned = scan is not null,
                ScanStatus = scan is null ? "Not scanned" : "Done",
                LastScan = scan?.ScannedAt,
                Vulnerabilities = scan?.Vulnerabilities.Count ?? 0,
                Critical = scan?.Critical ?? 0, High = scan?.High ?? 0,
                Verdict = scan?.Verdict
            };
        }).ToList();
        // Add real scanned artifacts (e.g. Docker images) the ScanStore holds for this repo but Nexus
        // doesn't list — live scan data, not fixtures.
        var listed = items.Select(a => $"{a.Name}|{a.Version}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var scan in _scans.ForRepository(repo))
        {
            if (listed.Contains($"{scan.Name}|{scan.Version}")) continue;
            artifacts.Add(new
            {
                scan.Name, scan.Version, Ecosystem = scan.Ecosystem.ToString(), scan.FileName,
                RepositoryPath = $"{repo}/{scan.Name}",
                Scanned = true, ScanStatus = "Done", LastScan = (DateTimeOffset?)scan.ScannedAt,
                Vulnerabilities = scan.Vulnerabilities.Count, scan.Critical, scan.High, Verdict = (string?)scan.Verdict
            });
        }
        return Ok(new { configured = true, repository = repo, count = artifacts.Count, artifacts });
    }

    /// <summary>The full stored scan for one artifact (runs + indexes it if not already scanned).</summary>
    [HttpGet("artifact")]
    public async Task<ActionResult> Artifact([FromQuery] string repo, [FromQuery] string ecosystem,
        [FromQuery] string name, [FromQuery] string version, [FromQuery] bool rescan = false, CancellationToken ct = default)
    {
        if (!Enum.TryParse<Ecosystem>(ecosystem, true, out var eco)) eco = Ecosystem.npm;
        var pkg = new PackageRef(eco, name, version);
        var scan = rescan
            ? await _scans.ScanArtifactAsync(repo, pkg, ct)
            : await _scans.GetOrScanAsync(repo, pkg, ct);
        return Ok(scan);
    }
}

/// <summary>Inspect what is physically held in the Nexus quarantine repo right now.</summary>
[ApiController]
[Route("api/quarantine")]
[Authorize(Policy = Policies.CanViewer)]
public class QuarantineController : ControllerBase
{
    private readonly INexusClient _nexus;
    public QuarantineController(INexusClient nexus) => _nexus = nexus;

    [HttpGet]
    public async Task<ActionResult> Held(CancellationToken ct)
    {
        if (!_nexus.IsConfigured) return Ok(new { configured = false, held = Array.Empty<object>() });
        var items = await _nexus.ListQuarantineAsync(ct);
        return Ok(new { configured = true, count = items.Count, held = items });
    }
}


/// <summary>Producer + observability for the durable intake queue.</summary>
[ApiController]
[Route("api/queue")]
[Authorize(Policy = Policies.CanViewer)]
public class QueueController : ControllerBase
{
    private readonly IIntakeQueue _queue;
    public QueueController(IIntakeQueue queue) => _queue = queue;

    /// <summary>Enqueue a package for async evaluation. Returns immediately — caller never waits.</summary>
    [HttpPost("enqueue")]
    public async Task<ActionResult> Enqueue([FromBody] PackageRef pkg, CancellationToken ct)
    {
        var id = await _queue.EnqueueAsync(pkg, ct);
        return Accepted(new { messageId = id, status = "queued" });
    }

    /// <summary>Queue depth: pending, dead-lettered, processed.</summary>
    [HttpGet("depth")]
    public async Task<ActionResult> Depth(CancellationToken ct) => Ok(await _queue.DepthAsync(ct));
}


/// <summary>
/// Exception management for Approvers (PCI 7.2 separation: grant exceptions without holding
/// policy-edit rights). Every grant/revoke is attributed to the authenticated user and audited.
/// </summary>
[ApiController]
[Route("api/exceptions")]
[Authorize(Policy = Policies.CanApprove)]
public class ExceptionsController : ControllerBase
{
    private readonly IPolicyStore _store;
    private readonly IAuditLog _audit;
    private readonly ICurrentUser _user;
    public ExceptionsController(IPolicyStore store, IAuditLog audit, ICurrentUser user)
    { _store = store; _audit = audit; _user = user; }

    public record GrantRequest(string Package, string Reason, string Ticket, DateTimeOffset Expires);

    [HttpGet]
    public ActionResult List() => Ok(_store.Current.Exceptions);

    [HttpPost]
    public async Task<ActionResult> Grant([FromBody] GrantRequest req, CancellationToken ct)
    {
        var p = _store.Current;
        var ex = new PolicyException {
            Package = req.Package, Reason = req.Reason, Ticket = req.Ticket,
            Expires = req.Expires, ApprovedBy = _user.Name };
        var updated = ClonePolicy(p);
        updated.Exceptions.Add(ex);
        await _store.UpdateAsync(updated, _user.Name);
        await _audit.AppendAsync(new AuditEntry(Guid.NewGuid(),
            new PackageRef(Ecosystem.PyPI, req.Package, "*"), GateDecision.Allow,
            Array.Empty<Finding>(), new[] { $"SEC-EXC-GRANT:{req.Ticket}" }, req.Ticket,
            _store.Current.Version, 0, DateTimeOffset.UtcNow, null,
            $"Exception granted for {req.Package} by {_user.Name}, ticket {req.Ticket}, expires {req.Expires:o}.",
            _user.Name));
        return Ok(ex);
    }

    [HttpDelete("{ticket}")]
    public async Task<ActionResult> Revoke(string ticket, CancellationToken ct)
    {
        var p = _store.Current;
        var updated = ClonePolicy(p);
        var removed = updated.Exceptions.RemoveAll(e => e.Ticket == ticket);
        await _store.UpdateAsync(updated, _user.Name);
        await _audit.AppendAsync(new AuditEntry(Guid.NewGuid(),
            new PackageRef(Ecosystem.PyPI, ticket, "*"), GateDecision.Block,
            Array.Empty<Finding>(), new[] { $"SEC-EXC-REVOKE:{ticket}" }, ticket,
            _store.Current.Version, 0, DateTimeOffset.UtcNow, null,
            $"Exception {ticket} revoked by {_user.Name} ({removed} removed).", _user.Name));
        return Ok(new { revoked = removed });
    }

    // Full JSON round-trip clone: a field-by-field copy here silently dropped Watches /
    // EnableContentScan / EnableReachability whenever an exception was granted or revoked.
    private static FirewallPolicy ClonePolicy(FirewallPolicy p) =>
        System.Text.Json.JsonSerializer.Deserialize<FirewallPolicy>(
            System.Text.Json.JsonSerializer.Serialize(p))!;
}

/// <summary>
/// Xray-style Reports (docs.jfrog.com/security/docs/xray-reports): Vulnerabilities, Legal
/// (licenses), Violations, and Operational Risk — aggregated views over the decision ledger,
/// with JSON or CSV export. Vulnerabilities/Violations come straight from the signed ledger;
/// Legal/Operational enrich the distinct packages in the ledger with live registry intel.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = Policies.CanViewer)]
public class ReportsController : ControllerBase
{
    private readonly IAuditLog _audit;
    private readonly IPolicyStore _policy;
    private readonly Advisory.Api.Catalog.OpRiskService _opRisk;
    public ReportsController(IAuditLog audit, IPolicyStore policy, Advisory.Api.Catalog.OpRiskService opRisk)
    { _audit = audit; _policy = policy; _opRisk = opRisk; }

    [HttpGet("{type}")]
    public async Task<ActionResult> Get(string type, [FromQuery] string format = "json",
        [FromQuery] int limit = 500, CancellationToken ct = default)
    {
        var rows = type.ToLowerInvariant() switch
        {
            "vulnerabilities" => Vulnerabilities(limit),
            "violations" => Violations(limit),
            "licenses" or "legal" => await Legal(limit, ct),
            "operational" => await Operational(limit, ct),
            _ => null,
        };
        if (rows is null) return BadRequest(new { error = "type must be vulnerabilities | violations | licenses | operational" });

        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            return File(System.Text.Encoding.UTF8.GetBytes(ToCsv(rows)), "text/csv", $"{type}-report.csv");
        return Ok(new { report = type, generatedAt = DateTimeOffset.UtcNow, count = rows.Count, rows });
    }

    private List<Dictionary<string, object?>> Vulnerabilities(int limit)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var e in _audit.Query(null, limit))
            foreach (var f in e.Findings)
                rows.Add(new()
                {
                    ["package"] = e.Package.Name, ["version"] = e.Package.Version,
                    ["ecosystem"] = e.Package.Ecosystem.ToString(),
                    ["vulnerability"] = f.Id, ["severity"] = f.Severity.ToString(),
                    ["cvss"] = f.CvssScore, ["epss"] = f.EpssScore,
                    ["knownExploited"] = f.KnownExploited, ["fixedVersion"] = f.FixedVersion,
                    ["cves"] = f.Aliases is null ? null : string.Join(" ", f.Aliases.Where(a => a.StartsWith("CVE"))),
                    ["decision"] = e.Decision.ToString(), ["detectedAt"] = e.Timestamp,
                });
        return rows;
    }

    private List<Dictionary<string, object?>> Violations(int limit)
    {
        var p = _policy.Current;
        var rows = new List<Dictionary<string, object?>>();
        foreach (var e in _audit.Query(GateDecision.Block, limit)
                     .Concat(_audit.Query(GateDecision.Quarantine, limit))
                     .OrderByDescending(e => e.Timestamp).Take(limit))
        {
            var ex = p.Exceptions.FirstOrDefault(x => x.Matches(e.Package));
            rows.Add(new()
            {
                ["resource"] = $"{e.Package.Ecosystem}:{e.Package.Name}@{e.Package.Version}",
                ["decision"] = e.Decision.ToString(),
                ["severity"] = e.Findings.Count > 0 ? e.Findings.Max(f => f.Severity).ToString() : "High",
                ["policyControls"] = string.Join("; ", e.TriggeredRules),
                ["status"] = ex is not null ? "Waived" : "Open",
                ["waivedBy"] = ex?.Ticket, ["actor"] = e.Actor, ["detectedAt"] = e.Timestamp,
            });
        }
        return rows;
    }

    /// <summary>Distinct root packages seen by the gate, for ledger-derived enrichment reports.</summary>
    private List<PackageRef> DistinctPackages(int limit) =>
        _audit.Query(null, limit)
            .Where(e => e.ComponentsEvaluated > 0)
            .Select(e => e.Package)
            .DistinctBy(pk => $"{pk.Ecosystem}:{pk.Name}@{pk.Version}")
            .Take(40).ToList();

    private async Task<List<Dictionary<string, object?>>> Legal(int limit, CancellationToken ct)
    {
        var block = _policy.Current.LicenseBlocklist;
        var pkgs = DistinctPackages(limit);
        var risks = await Task.WhenAll(pkgs.Select(pk => _opRisk.AnalyzeAsync(pk.Ecosystem, pk.Name, pk.Version, ct)));
        return pkgs.Zip(risks).Select(z => new Dictionary<string, object?>
        {
            ["package"] = z.First.Name, ["version"] = z.First.Version,
            ["ecosystem"] = z.First.Ecosystem.ToString(),
            ["license"] = z.Second?.License ?? "Unknown",
            ["prohibited"] = z.Second?.License is { Length: > 0 } lic
                && block.Any(b => lic.Contains(b, StringComparison.OrdinalIgnoreCase)),
            ["dueDiligence"] = z.Second?.License is null or "" ? "Unknown license — review required" : null,
        }).ToList();
    }

    private async Task<List<Dictionary<string, object?>>> Operational(int limit, CancellationToken ct)
    {
        var pkgs = DistinctPackages(limit);
        var risks = await Task.WhenAll(pkgs.Select(pk => _opRisk.AnalyzeAsync(pk.Ecosystem, pk.Name, pk.Version, ct)));
        return pkgs.Zip(risks).Select(z => new Dictionary<string, object?>
        {
            ["package"] = z.First.Name, ["version"] = z.First.Version,
            ["ecosystem"] = z.First.Ecosystem.ToString(),
            ["risk"] = z.Second?.Severity ?? "Unknown", ["riskReason"] = z.Second?.RiskReason,
            ["eol"] = z.Second?.Eol, ["versionAgeMonths"] = z.Second?.VersionAgeMonths,
            ["newerVersions"] = z.Second?.NewerVersions, ["releasesLastYear"] = z.Second?.ReleasesLastYear,
            ["released"] = z.Second?.ReleaseDate, ["latestVersion"] = z.Second?.LatestVersion,
        }).ToList();
    }

    private static string ToCsv(List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return "";
        var cols = rows[0].Keys.ToList();
        string Esc(object? v) { var s = v?.ToString() ?? ""; return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s; }
        var sb = new System.Text.StringBuilder(string.Join(",", cols)).AppendLine();
        foreach (var r in rows) sb.AppendLine(string.Join(",", cols.Select(c => Esc(r.GetValueOrDefault(c)))));
        return sb.ToString();
    }
}

/// <summary>
/// AppTrust applications (JFrog AppTrust parity): registered applications and their bound packages.
/// Each application's post-release CVE posture is computed live from the audit ledger for its
/// packages, so the Insights view reflects real gate decisions, not a static record.
/// </summary>
[ApiController]
[Route("api/apptrust")]
[Authorize(Policy = Policies.CanViewer)]
public class AppTrustController : ControllerBase
{
    private readonly IPolicyStore _policy;
    private readonly IAuditLog _audit;
    private readonly Advisory.Api.Auth.ICurrentUser _user;
    public AppTrustController(IPolicyStore policy, IAuditLog audit, Advisory.Api.Auth.ICurrentUser user)
    { _policy = policy; _audit = audit; _user = user; }

    [HttpGet("applications")]
    public ActionResult Applications()
    {
        var ledger = _audit.Query(null, 1000);
        var apps = _policy.Current.Applications.Select(a =>
        {
            var pkgKeys = a.Packages.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var entries = ledger.Where(e => pkgKeys.Contains($"{e.Package.Ecosystem}:{e.Package.Name}")).ToList();
            var crit = entries.SelectMany(e => e.Findings).Count(f => f.Severity == Severity.Critical);
            var blocks = entries.Count(e => e.Decision != GateDecision.Allow);
            return new
            {
                a.Key, a.Name, a.Project, a.Criticality, a.Type, a.Team, a.Owners, a.Description, a.CreatedAt,
                packages = a.Packages.Count, evaluated = entries.Count,
                criticalCves = crit, blockedVersions = blocks,
                trustedReleases = entries.Count(e => e.Decision == GateDecision.Allow),
            };
        });
        return Ok(new { count = _policy.Current.Applications.Count, applications = apps });
    }

    [HttpGet("application")]
    public ActionResult Application([FromQuery] string key)
    {
        var a = _policy.Current.Applications.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (a is null) return NotFound(new { error = $"application '{key}' not found" });
        var pkgKeys = a.Packages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = _audit.Query(null, 1000)
            .Where(e => pkgKeys.Contains($"{e.Package.Ecosystem}:{e.Package.Name}"))
            .OrderByDescending(e => e.Timestamp).ToList();
        var postRelease = entries.SelectMany(e => e.Findings.Select(f => new { e.Package, f }))
            .Where(x => x.f.Severity == Severity.Critical)
            .Select(x => new { resource = $"{x.Package.Ecosystem}:{x.Package.Name}@{x.Package.Version}", cve = x.f.Id, x.f.CvssScore, x.f.KnownExploited, fixedVersion = x.f.FixedVersion })
            .Take(50).ToList();
        return Ok(new
        {
            application = a,
            insights = new
            {
                trustedReleases = entries.Count(e => e.Decision == GateDecision.Allow),
                blockedVersions = entries.Count(e => e.Decision != GateDecision.Allow),
                evaluated = entries.Count,
                newlyDetectedCriticalCves = postRelease,
            },
        });
    }

    public record AppUpsert(string Key, string Name, string? Project, string? Criticality, string? Type,
        string? Team, string? Owners, string? Description, List<string>? Packages);

    [HttpPost("application")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Upsert([FromBody] AppUpsert r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Key) || string.IsNullOrWhiteSpace(r.Name))
            return BadRequest(new { error = "key and name are required" });
        var next = JsonSerializer.Deserialize<FirewallPolicy>(JsonSerializer.Serialize(_policy.Current))!;
        next.Applications.RemoveAll(a => a.Key.Equals(r.Key, StringComparison.OrdinalIgnoreCase));
        next.Applications.Add(new AppRecord
        {
            Key = r.Key, Name = r.Name, Project = r.Project ?? "", Criticality = r.Criticality ?? "Medium",
            Type = r.Type ?? "library", Team = r.Team ?? "", Owners = r.Owners ?? "",
            Description = r.Description ?? "", Packages = r.Packages ?? new(),
        });
        await _policy.UpdateAsync(next, _user.Name);
        return Ok(new { saved = true, count = next.Applications.Count });
    }

    [HttpDelete("application")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Delete([FromQuery] string key, CancellationToken ct)
    {
        var next = JsonSerializer.Deserialize<FirewallPolicy>(JsonSerializer.Serialize(_policy.Current))!;
        var n = next.Applications.RemoveAll(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (n > 0) await _policy.UpdateAsync(next, _user.Name);
        return Ok(new { removed = n });
    }
}

/// <summary>
/// AI Catalog (JFrog parity): Registry (approved models, allow-list in the signed policy),
/// Discovery (live Hugging Face Hub search with risk scoring), Detection (shadow-AI sweep of the
/// org's repositories). Approving a model is an admin action recorded in the versioned policy.
/// </summary>
[ApiController]
[Route("api/aicatalog")]
[Authorize(Policy = Policies.CanViewer)]
public class AiCatalogController : ControllerBase
{
    private readonly Advisory.Api.Catalog.AiCatalogService _svc;
    private readonly Advisory.Api.Catalog.WeightVerifier _verifier;
    private readonly Advisory.Api.Catalog.VerificationJobService _jobs;
    private readonly Advisory.Api.Catalog.ConsumedModelStore _consumed;
    private readonly IPolicyStore _policy;
    private readonly Advisory.Api.Auth.ICurrentUser _user;
    public AiCatalogController(Advisory.Api.Catalog.AiCatalogService svc, Advisory.Api.Catalog.WeightVerifier verifier,
        Advisory.Api.Catalog.VerificationJobService jobs, Advisory.Api.Catalog.ConsumedModelStore consumed,
        IPolicyStore policy, Advisory.Api.Auth.ICurrentUser user)
    { _svc = svc; _verifier = verifier; _jobs = jobs; _consumed = consumed; _policy = policy; _user = user; }

    public record ConsumeReq(string Id, string? Repo, string? Version, string? File, string? Format);

    /// <summary>Pull an APPROVED model into a repository for consumption (shows in Detection as Approved).
    /// Promotes the SAFE weight format the model ships: prefers safetensors → onnx → gguf, and only
    /// lands a pickle file when the repo has nothing safer. This is the firewall's "promote the safe
    /// equivalent, quarantine pickle" behaviour made real.</summary>
    [HttpPost("consume")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Consume([FromBody] ConsumeReq req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Id)) return BadRequest(new { error = "id required" });
        if (!_policy.Current.AllowedModels.Any(a => a.Id.Equals(req.Id, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { error = "model is not on the approved registry — approve it first" });

        // Resolve the model's actual promoted file from the Hub (so Detection reflects reality).
        string file = req.File ?? "", format = req.Format ?? "";
        var quarantinedPickle = false;
        if (string.IsNullOrEmpty(file))
        {
            var detail = await _svc.GetModelAsync(req.Id, ct);
            var weights = detail?.Files.Where(f => f.Format is "safetensors" or "onnx" or "gguf" or "pickle").ToList() ?? new();
            var safe = weights.FirstOrDefault(f => f.Format == "safetensors")
                       ?? weights.FirstOrDefault(f => f.Format == "onnx")
                       ?? weights.FirstOrDefault(f => f.Format == "gguf");
            var chosen = safe ?? weights.FirstOrDefault();   // pickle-only fallback
            file = chosen?.Name ?? "model.safetensors";
            format = chosen?.Format ?? "safetensors";
            quarantinedPickle = safe is not null && weights.Any(f => f.Format == "pickle");
        }
        var repo = string.IsNullOrWhiteSpace(req.Repo) ? "huggingface-approved" : req.Repo!;
        var m = _consumed.Add(repo, req.Id, req.Version ?? "main", file, format);
        return Ok(new { consumed = true, repo = m.Repo, model = m.ModelId, file, format, quarantinedPickle });
    }

    /// <summary>Simulate an UNapproved model landing in a repo (so Detection shows a shadow-AI row).</summary>
    [HttpPost("consume/shadow")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult ConsumeShadow([FromBody] ConsumeReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Id)) return BadRequest(new { error = "id required" });
        var repo = string.IsNullOrWhiteSpace(req.Repo) ? "huggingface-quarantine" : req.Repo!;
        var m = _consumed.Add(repo, req.Id, req.Version ?? "main", req.File ?? "pytorch_model.bin", req.Format ?? "pickle");
        return Ok(new { shadow = true, repo = m.Repo, model = m.ModelId });
    }

    /// <summary>Remove a consumed model from a repository.</summary>
    [HttpDelete("consume")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult Unconsume([FromQuery] string repo, [FromQuery] string id)
        => Ok(new { removed = _consumed.Remove(repo, id) });

    [HttpGet("discover")]
    public async Task<ActionResult> Discover([FromQuery] string? q, [FromQuery] string sort = "downloads",
        [FromQuery] int limit = 30, CancellationToken ct = default)
    {
        try { var models = await _svc.SearchAsync(q, sort, limit, ct); return Ok(new { count = models.Count, models }); }
        catch (Exception ex) { return Ok(new { count = 0, models = Array.Empty<object>(), error = ex.Message }); }
    }

    [HttpGet("model")]
    public async Task<ActionResult> Model([FromQuery] string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "id required" });
        var m = await _svc.GetModelAsync(id, ct);
        return m is null ? NotFound(new { error = $"model '{id}' not found on the Hub" }) : Ok(m);
    }

    /// <summary>
    /// Byte-level weight verification (100%-accuracy path): magic-byte signature per file via
    /// HTTP Range; when inconclusive the file is downloaded to cache and structurally scanned
    /// (zip pickle entries / pickle opcode stream / raw). Nothing is ever assumed silently.
    /// </summary>
    [HttpGet("verify")]
    public async Task<ActionResult> Verify([FromQuery] string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "id required" });
        var m = await _svc.GetModelAsync(id, ct);
        if (m is null) return NotFound(new { error = $"model '{id}' not found on the Hub" });
        var verdicts = await _verifier.VerifyModelAsync(id, m.Files, ct);
        return Ok(new
        {
            id,
            files = verdicts,
            summary = new
            {
                total = verdicts.Count,
                pickleConfirmed = verdicts.Count(v => v.Format == "pickle" && v.Confirmed),
                unconfirmed = verdicts.Count(v => !v.Confirmed),
                maliciousHits = verdicts.SelectMany(v => v.MaliciousHits.Select(h => $"{v.Name}: {h}"))
                    .Where(h => h.Contains("DANGEROUS") || h.Contains("DYNAMIC")).ToList(),
            },
        });
    }

    /// <summary>Start async background verification (returns immediately; poll /verify/status).</summary>
    [HttpPost("verify/start")]
    public async Task<ActionResult> VerifyStart([FromQuery] string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "id required" });
        var m = await _svc.GetModelAsync(id, ct);
        if (m is null) return NotFound(new { error = $"model '{id}' not found on the Hub" });
        var job = _jobs.Start(id, m.Files);
        return Accepted(job.Snapshot());
    }

    /// <summary>All verification jobs (for the global downloads panel).</summary>
    [HttpGet("verify/jobs")]
    public ActionResult VerifyJobs() => Ok(new { jobs = _jobs.All() });

    /// <summary>Poll the live state of a verification job (per-file head → download% → scan → verdict).</summary>
    [HttpGet("verify/status")]
    public ActionResult VerifyStatus([FromQuery] string id)
    {
        var job = _jobs.Get(id);
        return job is null ? Ok(new { status = "none" }) : Ok(job.Snapshot());
    }

    /// <summary>Evict this model's cached downloads from disk after a decision. Returns bytes freed.</summary>
    [HttpDelete("verify/cache")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult VerifyEvict([FromQuery] string id)
        => Ok(new { freedBytes = _jobs.Evict(id) });

    [HttpGet("registry")]
    public async Task<ActionResult> Registry(CancellationToken ct)
        => Ok(new
        {
            enforce = _policy.Current.EnforceModelAllowList,
            count = _policy.Current.AllowedModels.Count,
            models = await _svc.RegistryAsync(ct),
        });

    public record AllowReq(string Id, string? License, string? Notes);

    [HttpPost("registry/allow")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Allow([FromBody] AllowReq req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Id)) return BadRequest(new { error = "id required" });
        var next = Clone(_policy.Current);
        if (next.AllowedModels.Any(a => a.Id.Equals(req.Id, StringComparison.OrdinalIgnoreCase)))
            return Ok(new { allowed = true, already = true });
        next.AllowedModels.Add(new AllowedModel
        {
            Id = req.Id, License = req.License ?? "", Notes = req.Notes ?? "", ApprovedBy = _user.Name,
        });
        await _policy.UpdateAsync(next, _user.Name);
        return Ok(new { allowed = true, count = next.AllowedModels.Count });
    }

    [HttpDelete("registry")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Disallow([FromQuery] string id, CancellationToken ct)
    {
        var next = Clone(_policy.Current);
        var removed = next.AllowedModels.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) await _policy.UpdateAsync(next, _user.Name);
        return Ok(new { removed });
    }

    public record EnforceReq(bool Enforce);

    [HttpPut("enforce")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Enforce([FromBody] EnforceReq req, CancellationToken ct)
    {
        var next = Clone(_policy.Current);
        next.EnforceModelAllowList = req.Enforce;
        await _policy.UpdateAsync(next, _user.Name);
        return Ok(new { enforce = req.Enforce });
    }

    [HttpGet("detect")]
    public async Task<ActionResult> Detect(CancellationToken ct)
    {
        var found = await _svc.DetectAsync(ct);
        return Ok(new
        {
            configured = found.Count > 0 || true,
            count = found.Count,
            shadow = found.Count(f => f.Status == "Shadow AI"),
            artifacts = found,
        });
    }

    private static FirewallPolicy Clone(FirewallPolicy p)
        => JsonSerializer.Deserialize<FirewallPolicy>(JsonSerializer.Serialize(p))!;
}

/// <summary>
/// Self-evolution: GitHub tickets + tester comments drive automated code changes via the EVOLVE
/// engine. PR-ONLY — every change is a pull request for human review; nothing auto-merges.
/// </summary>
[ApiController]
[Route("api/evolution")]
[Authorize(Policy = Policies.CanViewer)]
public class EvolutionController : ControllerBase
{
    private readonly Advisory.Api.Evolution.EvolutionService _svc;
    private readonly Advisory.Api.Agents.GroqCycle _groq;
    private readonly IServiceScopeFactory _scopes;
    public EvolutionController(Advisory.Api.Evolution.EvolutionService svc, Advisory.Api.Agents.GroqCycle groq, IServiceScopeFactory scopes)
    { _svc = svc; _groq = groq; _scopes = scopes; }

    [HttpGet("status")]
    public ActionResult Status() => Ok(_svc.Status());

    [HttpGet("tickets")]
    public async Task<ActionResult> Tickets(CancellationToken ct)
    {
        if (!_svc.Enabled) return Ok(new { enabled = false, tickets = Array.Empty<object>() });
        var tickets = await _svc.TicketsAsync(ct);
        var active = _svc.Runs(100).Where(r => r.Status is "queued" or "running" or "tests").Select(r => r.Ticket).ToHashSet();
        return Ok(new { enabled = true, repo = _svc.Repo, tickets, activeTickets = active });
    }

    [HttpGet("runs")]
    public ActionResult Runs([FromQuery] int limit = 50) => Ok(new { runs = _svc.Runs(limit) });

    /// <summary>Real mutation history (tickets started/closed + merged PRs by day) for the Memories
    /// dashboard graphs — sourced from GitHub, so it survives clearing the in-memory run list.</summary>
    [HttpGet("history")]
    public async Task<ActionResult> History(CancellationToken ct) => Ok(await _svc.HistoryAsync(ct));

    /// <summary>Clear mutation run history (the dashboard "Clear runs" action). activeOnly=true keeps
    /// finished runs and only drops queued/running ones so a stale queued run can't re-fire.</summary>
    [HttpDelete("runs")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult ClearRuns([FromQuery] bool activeOnly = false)
        => Ok(new { cleared = _svc.ClearRuns(activeOnly) });

    [HttpGet("run/{id}")]
    public ActionResult Run(string id)
        => _svc.Run(id) is { } r ? Ok(r) : NotFound(new { error = "run not found" });

    // ---- worker plumbing (the local mutate-claude.sh loop calls these) ----
    /// <summary>Worker heartbeat — proves a loop is draining the queue (dashboard "worker online").</summary>
    [HttpPost("worker/ping")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult WorkerPing() { _svc.WorkerHeartbeat(); return Ok(new { workerAlive = _svc.WorkerAlive }); }

    /// <summary>The next queued run for the worker to pick up (null when the queue is empty).</summary>
    [HttpGet("next")]
    public ActionResult Next() => Ok(new { run = _svc.NextQueued() });

    public record ConsumeReq(string File);

    /// <summary>Worker asks the API (root) to delete a consumed queue request it can't remove itself
    /// (the bind-mounted file is root-owned; the host worker user can't rm it).</summary>
    [HttpPost("queue/consume")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult ConsumeQueue([FromBody] ConsumeReq req)
        => Ok(new { removed = _svc.ConsumeRequest(req.File) });

    public record ProgressReq(string? Stage, string? Status, string? PrUrl, string? Log);

    /// <summary>Worker reports live progress for a run (stage → % + ETA on the dashboard).</summary>
    [HttpPost("run/{id}/progress")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult Progress(string id, [FromBody] ProgressReq req)
        => _svc.UpdateProgress(id, req.Stage, req.Status, req.PrUrl, req.Log) is { } r
            ? Ok(r) : NotFound(new { error = "run not found" });

    /// <summary>Worker resets a run stopped for an external reason (e.g. Claude rate limit / out of
    /// credits) — drops it so the dashboard shows no misleading failure and the ticket can be
    /// re-queued later. Idempotent.</summary>
    [HttpPost("run/{id}/reset")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult ResetRun(string id) => Ok(new { reset = _svc.ResetRun(id) });

    // ---- Interactive run control (EPIC A) ----
    public record PlanReq(string Plan);
    public record DecisionReq(string Decision, string? SubIssue);   // decision: approve | reject | refine

    /// <summary>Worker submits its proposed plan; the run parks 'awaiting-approval'.</summary>
    [HttpPost("run/{id}/plan")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult SubmitPlan(string id, [FromBody] PlanReq req)
        => _svc.SubmitPlan(id, req.Plan ?? "") is { } r ? Ok(r) : NotFound(new { error = "run not found" });

    /// <summary>Operator approves / rejects / refines a parked plan (from the dashboard). On REJECT with
    /// a recommendation, the ticket is amended (the recommendation is posted as a comment) and the cycle
    /// RESTARTS automatically so the engine re-plans with the feedback. Approve/refine proceed in place.</summary>
    [HttpPost("run/{id}/decision")]
    [Authorize(Policy = Policies.CanApprove)]
    public async Task<ActionResult> Decide(string id, [FromBody] DecisionReq req, CancellationToken ct)
    {
        if ((req.Decision ?? "") == "reject")
        {
            // Reject → amend the ticket with the operator's recommendation → restart the cycle.
            var restarted = await _svc.RejectAndAmendAsync(id, req.SubIssue ?? "", ct);
            return restarted is { } nr
                ? Ok(new { rejected = id, restarted = nr.Id, ticket = nr.Ticket, status = nr.Status, stage = nr.Stage })
                : NotFound(new { error = "run not found" });
        }
        if ((req.Decision ?? "") == "merge")
        {
            // Merge → the SECOND operator checkpoint: squash-merge the green PR, delete the branch,
            // close the issue, mark 'released'. Only valid at 'pr-open'. Release stays operator-only.
            return await _svc.DecideMergeAsync(id, ct) is { } m ? Ok(m) : NotFound(new { error = "run not found" });
        }
        return _svc.Decide(id, req.Decision ?? "approve", req.SubIssue) is { } r ? Ok(r) : NotFound(new { error = "run not found" });
    }

    /// <summary>Worker polls the operator's decision on a parked plan.</summary>
    [HttpGet("run/{id}/decision")]
    public ActionResult Decision(string id)
        => _svc.Decision(id) is { } x ? Ok(x) : NotFound(new { error = "run not found" });

    public record EvolveReq(int Ticket);

    /// <summary>Trigger mutation for a ticket (admin): label it and QUEUE IT FOR THE LOCAL /mutate loop
    /// (scripts/mutate-claude.sh, which uses your Claude login). Opens a PR for review; never merges.</summary>
    [HttpPost("evolve")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Evolve([FromBody] EvolveReq req, CancellationToken ct)
    {
        if (!_svc.Enabled) return BadRequest(new { error = "evolution is disabled (set EVOLUTION_ENABLED=true)" });
        if (string.IsNullOrWhiteSpace(_svc.Repo)) return BadRequest(new { error = "no target repo (set EVOLUTION_REPO)" });
        var tickets = await _svc.TicketsAsync(ct);
        var t = tickets.FirstOrDefault(x => x.Number == req.Ticket);
        if (t is null) return NotFound(new { error = $"ticket #{req.Ticket} not found or not labelled '{_svc.Label}'" });

        // Create the run first so its id can travel in the queue request (the worker reports progress against it).
        var run = _svc.NewRun(t);
        run.Status = "queued"; run.Stage = "waiting for worker"; run.Pct = 0; run.EtaSeconds = Advisory.Api.Evolution.MutateStages.TotalSecs;
        var (ok, detail) = await _svc.DispatchWorkflowAsync(req.Ticket, run.Id, ct);
        if (!ok) { run.Status = "failed"; run.Stage = "queue-failed"; }
        run.Append(ok ? $"[queue] {detail} — waiting for the local worker to pick it up." : $"[error] {detail}");
        if (ok) run.PrUrl = $"https://github.com/{_svc.Repo}/pulls";
        return ok
            ? Accepted(new { runId = run.Id, ticket = t.Number, status = run.Status, detail })
            : BadRequest(new { error = detail });
    }

    /// <summary>API-NATIVE Groq cycle — runs entirely in the container, NO local worker. Plans on Groq,
    /// parks for operator approval, then (on approve) Groq writes the change and the container clones +
    /// builds + tests + opens the PR. Fast and worker-free for all-Groq routing.</summary>
    [HttpPost("groq-cycle")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> GroqCycleRun([FromBody] EvolveReq req, CancellationToken ct)
    {
        if (!_svc.Enabled) return BadRequest(new { error = "evolution is disabled" });
        if (string.IsNullOrWhiteSpace(_svc.Repo)) return BadRequest(new { error = "no target repo" });
        var agent = _groq.ExecutionAgent();
        if (agent is null) return BadRequest(new { error = "no enabled API agent (Groq/OpenAI) routed to execution — this path needs an API agent, not a CLI agent" });
        var t = await _svc.TicketAsync(req.Ticket, ct);
        if (t is null) return NotFound(new { error = $"ticket #{req.Ticket} not found" });

        var run = _svc.NewRun(t);
        run.Stage = "planning the fix (Groq, in-container)"; run.Pct = 15; run.Append($"[groq] API-native cycle on {agent.Id} ({agent.Model}).");

        // Plan now (fast), then PARK for operator approval. The implement step runs in the background
        // once the operator approves (poll), so this endpoint returns immediately.
        var plan = await _groq.PlanAsync(agent, t.Number, t.Title, t.Body, ct);
        _svc.SubmitPlan(run.Id, plan);

        var runId = run.Id; var ticket = t.Number; var title = t.Title; var body = t.Body;
        _ = Task.Run(async () =>
        {
            using var scope = _scopes.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<Advisory.Api.Evolution.EvolutionService>();
            var groq = scope.ServiceProvider.GetRequiredService<Advisory.Api.Agents.GroqCycle>();
            // Poll for the operator's decision (up to ~15 min). reject is handled by the decision endpoint.
            for (int i = 0; i < 180; i++)
            {
                await Task.Delay(5000);
                var r = svc.Run(runId);
                if (r is null || r.Approval == "rejected") return;
                if (r.Approval is "approved") break;
                if (i == 179) { svc.UpdateProgress(runId, "fix", "skipped", null, "approval timed out — re-queue to retry"); return; }
            }
            svc.UpdateProgress(runId, "fix", "running", null, "approved — Groq implementing (in-container)");
            var ag = groq.ExecutionAgent();
            if (ag is null) { svc.UpdateProgress(runId, "fix", "failed", null, "execution agent disappeared"); return; }
            // SELF-REPAIR loop: produce → apply+build+test in a clone; if it fails to build/test, feed the
            // error back to Groq and retry (up to 2 repairs). Only opens a PR when build AND tests pass —
            // a non-compiling change can never reach a PR. Context is RECALL from .said (token-efficient).
            var approved = svc.Run(runId);
            var (pok, detail) = await groq.ImplementWithRepairAsync(
                ag, ticket, title, body, approved?.Plan ?? plan,
                (stage, msg) => svc.UpdateProgress(runId, stage, stage == "build" ? "tests" : "running", null, msg),
                maxRepairs: 2, default);
            if (pok) svc.UpdateProgress(runId, "pr", "pr-open", detail, $"PR opened (Groq, in-container, built+tested): {detail}");
            else svc.UpdateProgress(runId, "fix", "failed", null, $"implement failed (no PR — change never built/tested clean): {detail}");
        });

        return Accepted(new { runId = run.Id, ticket = t.Number, status = "awaiting-approval", agent = agent.Id, model = agent.Model });
    }
}

/// <summary>
/// Evolution (research): the forward-looking loop that studies the supply-chain security landscape
/// (arXiv, NIST SSDF, SLSA, competitor controls) and files enhancement candidates by product section.
/// It NEVER edits product code — approving a finding files a `mutation` ticket the bug-fix loop
/// implements (PR-only). Runs weekly or via "Run research now".
/// </summary>
[ApiController]
[Route("api/research")]
[Authorize(Policy = Policies.CanViewer)]
public class ResearchController : ControllerBase
{
    private readonly Advisory.Api.Research.ResearchService _svc;
    public ResearchController(Advisory.Api.Research.ResearchService svc) { _svc = svc; }

    [HttpGet("status")]
    public ActionResult Status() => Ok(_svc.Status());

    /// <summary>The research backlog grouped by product section (the dashboard's Evolution tab).</summary>
    [HttpGet("findings")]
    public ActionResult Findings()
    {
        var gaps = _svc.Gaps();
        var bySection = Advisory.Api.Research.ResearchService.Sections.Select(sec => new
        {
            section = sec,
            open = gaps.Where(g => g.Section == sec && !g.Closed).ToList(),
            closed = gaps.Where(g => g.Section == sec && g.Closed).ToList(),
        }).ToList();
        return Ok(new
        {
            total = gaps.Count,
            closed = gaps.Count(g => g.Closed),
            open = gaps.Count(g => !g.Closed),
            sections = bySection,
        });
    }

    public record RunReq(string? Topic);

    /// <summary>Queue a research run now (admin). Local /evolve loop drains it; writes RESEARCH.md only.</summary>
    [HttpPost("run")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Run([FromBody] RunReq? req, CancellationToken ct)
    {
        var (ok, detail) = await _svc.RunNowAsync(req?.Topic, ct);
        return ok ? Accepted(new { status = "queued", detail }) : BadRequest(new { error = detail });
    }

    public record ApproveReq(string GapId);

    /// <summary>Approve a finding (admin) → file a `mutation` ticket so the bug-fix loop can implement it.</summary>
    [HttpPost("approve")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Approve([FromBody] ApproveReq req, CancellationToken ct)
    {
        var (ok, detail, url) = await _svc.ApproveAsync(req.GapId, ct);
        return ok ? Accepted(new { status = "filed", detail, url }) : BadRequest(new { error = detail });
    }
}

/// <summary>
/// On-Demand Scanning (JFrog parity): trigger an ad-hoc scan of any package through the real
/// gate engine and watch the row go Scanning → Done with severity / issue / violation counts.
/// </summary>
[ApiController]
[Route("api/ondemand")]
[Authorize(Policy = Policies.CanViewer)]
public class OnDemandController : ControllerBase
{
    private readonly Advisory.Api.Scan.OnDemandScanService _svc;
    public OnDemandController(Advisory.Api.Scan.OnDemandScanService svc) => _svc = svc;

    [HttpGet("list")]
    public ActionResult List() => Ok(new { count = _svc.List().Count, scans = _svc.List() });

    [HttpPost("scan")]
    public ActionResult Scan([FromBody] PackageRef pkg)
    {
        if (string.IsNullOrWhiteSpace(pkg.Name) || string.IsNullOrWhiteSpace(pkg.Version))
            return BadRequest(new { error = "name and version are required" });
        return Accepted(_svc.Start(pkg));
    }
}

/// <summary>
/// The "Ask AI" assistant + AI/Groq admin settings. Chat answers are grounded in THIS environment:
/// the active signed policy plus recent gate decisions from the audit ledger, so it answers like
/// JFrog's AI Assistant ("which CVEs are reachable here", "what was held and why"). The Groq key is
/// entered here (admin) and stored server-side in the signed policy — never returned to the client.
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize(Policy = Policies.CanViewer)]
public class AiController : ControllerBase
{
    private readonly IGroqClient _groq;
    private readonly IPolicyStore _policy;
    private readonly IAuditLog _audit;
    private readonly Advisory.Api.Auth.ICurrentUser _user;

    public AiController(IGroqClient groq, IPolicyStore policy, IAuditLog audit, Advisory.Api.Auth.ICurrentUser user)
    { _groq = groq; _policy = policy; _audit = audit; _user = user; }

    /// <summary>Assistant settings for the UI. The API key is NEVER returned — only whether one is set.</summary>
    [HttpGet("settings")]
    public ActionResult Settings()
    {
        var a = _policy.Current.Ai;
        return Ok(new
        {
            assistantEnabled = a.AssistantEnabled,
            provider = a.Provider,
            model = a.Model,
            endpoint = a.Endpoint,
            hasKey = !string.IsNullOrWhiteSpace(a.ApiKey),
            configured = _groq.IsConfigured,           // true if policy key OR env key resolves
            usingEnvKey = string.IsNullOrWhiteSpace(a.ApiKey) && _groq.IsConfigured,
        });
    }

    public record AiSettingsUpdate(bool? AssistantEnabled, string? Model, string? Endpoint, string? ApiKey, bool ClearKey);

    /// <summary>Save AI settings into the signed policy (admin). Blank ApiKey keeps the existing key.</summary>
    [HttpPut("settings")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> SaveSettings([FromBody] AiSettingsUpdate req, CancellationToken ct)
    {
        var p = _policy.Current;
        // clone the policy so we mutate a copy, then persist (keeps the signed-hash chain honest)
        var json = JsonSerializer.Serialize(p);
        var next = JsonSerializer.Deserialize<FirewallPolicy>(json)!;
        var a = next.Ai;
        if (req.AssistantEnabled is { } en) a.AssistantEnabled = en;
        if (!string.IsNullOrWhiteSpace(req.Model)) a.Model = req.Model!;
        if (!string.IsNullOrWhiteSpace(req.Endpoint)) a.Endpoint = req.Endpoint!;
        if (req.ClearKey) a.ApiKey = null;
        else if (!string.IsNullOrWhiteSpace(req.ApiKey)) a.ApiKey = req.ApiKey;
        await _policy.UpdateAsync(next, _user.Name);
        return Ok(new { saved = true, configured = _groq.IsConfigured });
    }

    /// <summary>Admin "Test connection": validate the supplied (or stored) key against the provider.</summary>
    [HttpPost("test")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Test([FromBody] AiSettingsUpdate req, CancellationToken ct)
    {
        var a = _policy.Current.Ai;
        var key = !string.IsNullOrWhiteSpace(req.ApiKey) ? req.ApiKey! : a.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) return Ok(new { ok = false, detail = "no key supplied or stored" });
        var (ok, detail) = await _groq.TestAsync(key!, req.Model ?? a.Model, req.Endpoint ?? a.Endpoint, ct);
        return Ok(new { ok, detail });
    }

    public record AiChat(string Message, List<AiTurn>? History);
    public record AiTurn(string Role, string Content);

    /// <summary>The assistant. Grounded in the live policy + recent ledger decisions for THIS environment.</summary>
    [HttpPost("chat")]
    public async Task<ActionResult> Chat([FromBody] AiChat req, CancellationToken ct)
    {
        if (!_policy.Current.Ai.AssistantEnabled)
            return Ok(new { ok = false, reply = "The AI assistant is disabled. An administrator can enable it under Administration → AI assistant." });
        if (string.IsNullOrWhiteSpace(req.Message))
            return Ok(new { ok = false, reply = "Ask a question about your packages, policy or recent decisions." });

        var prompt = new System.Text.StringBuilder();
        if (req.History is { Count: > 0 })
            foreach (var t in req.History.TakeLast(6))
                prompt.AppendLine($"{(t.Role == "user" ? "User" : "Assistant")}: {t.Content}");
        prompt.AppendLine().AppendLine("Current question: " + req.Message);
        prompt.AppendLine().AppendLine(BuildContext());

        var (ok, text) = await _groq.ChatAsync(AssistantSystem, prompt.ToString(), 900, 0.3, ct);
        return Ok(new { ok, reply = ok ? text : text, model = _groq.Model });
    }

    private const string AssistantSystem =
        "You are the Package Firewall AI assistant for a bank's software supply-chain security gate. " +
        "Answer questions about the user's packages, the firewall policy, and recent gate decisions " +
        "using ONLY the environment context provided. Be concise and concrete. When the context does not " +
        "contain the answer, say so plainly and suggest where to look (e.g. run a scan, open Reports). " +
        "Never invent CVE IDs, versions or package names. Plain prose; short bullet lists are fine.";

    /// <summary>Snapshot of the live policy + recent ledger so the model answers about THIS deployment.</summary>
    private string BuildContext()
    {
        var p = _policy.Current;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== ENVIRONMENT CONTEXT (Package Firewall) ===");
        sb.AppendLine($"Policy v{p.Version}, updated {p.UpdatedAt:yyyy-MM-dd} by {p.UpdatedBy}.");
        sb.AppendLine($"Gate: block CVSS >= {p.CvssBlockThreshold}; block KEV={p.BlockKnownExploited}; block EPSS >= {p.EpssBlockThreshold}; " +
            $"min package age {p.MinPackageAgeDays}d; license blocklist [{string.Join(", ", p.LicenseBlocklist)}]; " +
            $"reachability={p.EnableReachability} (downgrade-unreachable={p.DowngradeUnreachable}); content-scan={p.EnableContentScan}.");
        sb.AppendLine($"Enabled sources: {string.Join(", ", p.EnabledSources)} (required: {string.Join(", ", p.RequiredSources)}).");
        if (p.CustomSources.Count > 0) sb.AppendLine($"Custom OSV sources: {string.Join(", ", p.CustomSources.Select(c => c.Label))}.");
        sb.AppendLine($"Watches: {string.Join("; ", p.Watches.Where(w => w.Enabled).Select(w => w.Name))}.");
        sb.AppendLine($"Active exceptions/waivers: {p.Exceptions.Count}.");

        var recent = _audit.Query(null, 40).ToList();
        sb.AppendLine().AppendLine($"Recent gate decisions (latest {recent.Count}):");
        if (recent.Count == 0) sb.AppendLine("  (none yet — no packages have been evaluated)");
        foreach (var e in recent.Take(25))
        {
            var findings = e.Findings.Count == 0 ? "" :
                " findings=" + string.Join(",", e.Findings.Take(4).Select(f =>
                    $"{f.Id}[{f.Severity}{(f.KnownExploited ? ",KEV" : "")}{(f.CvssScore is { } cv ? $",cvss{cv}" : "")}{(f.FixedVersion is { } fv ? $",fix→{fv}" : "")}]"));
            sb.AppendLine($"  - {e.Package.Ecosystem}:{e.Package.Name}@{e.Package.Version} → {e.Decision}" +
                $"{(e.TriggeredRules.Count > 0 ? " (" + string.Join("; ", e.TriggeredRules) + ")" : "")}{findings}");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Admin Center: global platform configuration surfaced under the Administration view —
/// the AI agents the operator can use (any provider/standard), per-task agent routing for the
/// mutation/evolution loops, and memory + DB/runtime selection. Credentials are stored in the
/// signed policy (self-hosted) and NEVER returned to the client — only whether a key is set.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = Policies.CanViewer)]
public class AdminController : ControllerBase
{
    private readonly IPolicyStore _policy;
    private readonly Advisory.Api.Auth.ICurrentUser _user;
    private readonly Advisory.Api.Evolution.EvolutionService _evo;
    private readonly Advisory.Api.Agents.PhaseOrchestrator _orchestrator;
    private readonly Advisory.Api.Agents.IAgentRunner _runner;
    public AdminController(IPolicyStore policy, Advisory.Api.Auth.ICurrentUser user, Advisory.Api.Evolution.EvolutionService evo, Advisory.Api.Agents.PhaseOrchestrator orchestrator, Advisory.Api.Agents.IAgentRunner runner)
    { _policy = policy; _user = user; _evo = evo; _orchestrator = orchestrator; _runner = runner; }

    public record AgentTestReq(string? Prompt);

    /// <summary>Test ONE configured agent in isolation — each provider as its own module.
    /// API-standard agents (openai/groq/anthropic) run synchronously via MAF and return reply + tokens.
    /// CLI agents (claude-cli/cursor-cli) can't be called from the container, so we QUEUE a test the
    /// local worker drains and answers (poll GET /agent/{id}/test for the result).</summary>
    [HttpPost("agent/{id}/test")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> TestAgent(string id, [FromBody] AgentTestReq? req, CancellationToken ct)
    {
        var agent = _policy.Current.Admin.Agents.FirstOrDefault(a => a.Id == id);
        if (agent is null) return NotFound(new { error = $"agent '{id}' not found" });
        var prompt = string.IsNullOrWhiteSpace(req?.Prompt) ? "Reply in one sentence: confirm you are reachable and state your model." : req!.Prompt!;

        if (agent.Standard is "claude-cli" or "cursor-cli")
        {
            var (ok, detail) = _evo.QueueAgentTest(id, prompt);
            return Accepted(new { mode = "cli-queued", agent = id, ok, detail });
        }

        var rr = await _runner.RunAsync(agent, new Advisory.Api.Agents.AgentRunRequest("test", agent.Persona ?? "", "Answer concisely.", prompt), ct);
        return Ok(new { mode = "api", agent = id, model = rr.Model, ok = rr.Ok, error = rr.Error, reply = rr.Text, reasoning = rr.Reasoning, tokens = rr.Usage.Total });
    }

    public record AgentRunReq(string? System, string? Task, string? Prompt);
    /// <summary>Run a real PHASE prompt on a specific routed agent (used by the /mutate skill so a phase
    /// routed to Groq actually runs on Groq via MAF, not faked as a Claude sub-agent). API-standard
    /// agents (openai/groq/anthropic) run synchronously and return the reply + tokens. CLI agents
    /// (claude-cli/cursor-cli) signal the caller to run the phase inline (the skill is itself a Claude
    /// process, so a claude-cli phase just runs inline; cursor-cli likewise on the host).</summary>
    [HttpPost("agent/{id}/run")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> RunAgent(string id, [FromBody] AgentRunReq req, CancellationToken ct)
    {
        var agent = _policy.Current.Admin.Agents.FirstOrDefault(a => a.Id == id);
        if (agent is null) return NotFound(new { error = $"agent '{id}' not found" });
        // CLI agents have no in-container runner — tell the skill to run this phase inline.
        if (agent.Standard is "claude-cli" or "cursor-cli")
            return Ok(new { mode = "inline", agent = id, standard = agent.Standard, ranOnAgent = false });
        var rr = await _runner.RunAsync(agent, new Advisory.Api.Agents.AgentRunRequest(
            req.Task ?? "phase", agent.Persona ?? "", req.System ?? "You are running one phase of a PR-only mutation cycle. Be precise and minimal.",
            req.Prompt ?? ""), ct);
        return Ok(new { mode = "api", agent = id, model = rr.Model, ok = rr.Ok, error = rr.Error, ranOnAgent = rr.Ok, reply = rr.Text, tokens = rr.Usage.Total });
    }

    public record AgentTestResultReq(string Reply, bool Ok, string? Error);
    /// <summary>Worker posts a CLI agent-test result; dashboard polls GET to show it.</summary>
    [HttpPost("agent/{id}/test/result")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult PostAgentTestResult(string id, [FromBody] AgentTestResultReq r)
    { _evo.SetAgentTestResult(id, r.Reply, r.Ok, r.Error); return Ok(new { ok = true }); }

    [HttpGet("agent/{id}/test")]
    public ActionResult GetAgentTest(string id) => Ok(_evo.GetAgentTest(id));

    /// <summary>Models for an agent's provider — populates the UI dropdown instead of free-text.
    /// openai/anthropic → live GET {endpoint}/models with the key; claude-cli/cursor-cli → curated.</summary>
    [HttpGet("agent/models")]
    public async Task<ActionResult> AgentModels([FromQuery] string standard, [FromQuery] string? endpoint, [FromQuery] string? agentId, CancellationToken ct)
    {
        var ep = (endpoint ?? "").ToLowerInvariant();
        bool isGroq = ep.Contains("api.groq.com");
        bool isOpenRouter = ep.Contains("openrouter.ai");

        // Groq: ONLY the two supported gpt-oss models. OpenRouter: the curated Kimi/Opus set.
        string[]? Restricted() =>
            isGroq       ? new[] { "openai/gpt-oss-20b", "openai/gpt-oss-120b" } :
            isOpenRouter ? new[] { "moonshotai/kimi-k2.7-code", "moonshotai/kimi-k2.5", "anthropic/claude-opus-4.8" } :
            null;

        string[] Curated() => Restricted() ?? standard switch
        {
            "claude-cli" => new[] { "claude-opus-4-8", "claude-opus-4-6", "claude-sonnet-4-6", "claude-haiku-4-5-20251001" },
            "cursor-cli" => new[] { "auto", "claude-opus-4-8", "claude-sonnet-4-6", "gpt-5", "gpt-4o" },
            "anthropic"  => new[] { "claude-opus-4-8", "claude-opus-4-6", "claude-sonnet-4-6", "claude-haiku-4-5-20251001" },
            _            => new[] { "openai/gpt-oss-120b", "openai/gpt-oss-20b", "gpt-4o", "gpt-4o-mini" },
        };
        // For Groq/OpenRouter the dropdown is a FIXED allow-list — never hit the live /models list.
        if (Restricted() is { } fixedList)
            return Ok(new { live = false, models = fixedList });
        if (standard is not ("openai" or "anthropic"))
            return Ok(new { live = false, models = Curated() });

        string? key = !string.IsNullOrWhiteSpace(agentId)
            ? _policy.Current.Admin.Agents.FirstOrDefault(a => a.Id == agentId)?.ApiKey : null;
        var icfg = HttpContext.RequestServices.GetService<IConfiguration>();
        key ??= icfg?["GROQ_API_KEY"] ?? icfg?["PKGFW_GROQ_API_KEY"];
        var baseUrl = string.IsNullOrWhiteSpace(endpoint)
            ? (standard == "anthropic" ? "https://api.anthropic.com/v1" : "https://api.openai.com/v1")
            : endpoint!.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(key))
            return Ok(new { live = false, models = Curated(), note = "no key — showing common models" });
        try
        {
            using var http = HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            var reqMsg = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
            reqMsg.Headers.Add("Authorization", $"Bearer {key}");
            var resp = await http.SendAsync(reqMsg, ct);
            if (!resp.IsSuccessStatusCode) return Ok(new { live = false, models = Curated(), note = $"provider returned {(int)resp.StatusCode}" });
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var ids = doc.RootElement.TryGetProperty("data", out var data)
                ? data.EnumerateArray().Select(m => m.GetProperty("id").GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToArray()
                : Array.Empty<string?>();
            return Ok(new { live = ids.Length > 0, models = ids.Length > 0 ? ids : Curated() });
        }
        catch { return Ok(new { live = false, models = Curated(), note = "could not reach provider" }); }
    }

    public record CursorAuthReq(string? User);
    /// <summary>Begin CLI agent auth: queue a login the local worker runs. For cursor-cli the worker
    /// runs 'cursor-agent login'; for claude-cli it runs 'claude setup-token'. Both print a browser
    /// URL the user opens to authenticate; the worker relays status (and, for claude, persists the
    /// long-lived token) back here. Same endpoint for both so the dashboard flow is identical.</summary>
    [HttpPost("agent/{id}/cursor-auth")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult CursorAuth(string id, [FromBody] CursorAuthReq? req)
    {
        var agent = _policy.Current.Admin.Agents.FirstOrDefault(a => a.Id == id);
        if (agent is null) return NotFound(new { error = $"agent '{id}' not found" });
        if (agent.Standard is not ("cursor-cli" or "claude-cli"))
            return BadRequest(new { error = "browser auth only applies to a cursor-cli or claude-cli agent" });
        var (ok, detail) = _evo.QueueCursorAuth(id, agent.Standard, req?.User ?? agent.CursorUser ?? "");
        return Accepted(new { mode = "cli-queued", agent = id, standard = agent.Standard, ok, detail });
    }
    [HttpGet("agent/{id}/cursor-auth")]
    public ActionResult GetCursorAuth(string id) => Ok(_evo.GetCursorAuth(id));

    public record CursorAuthResultReq(string Status, string? Message, string? Url, bool Ok);
    /// <summary>Worker relays cursor login status (e.g. a browser URL to accept, or 'authenticated').</summary>
    [HttpPost("agent/{id}/cursor-auth/result")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult PostCursorAuthResult(string id, [FromBody] CursorAuthResultReq r)
    { _evo.SetCursorAuthResult(id, r.Status, r.Message, r.Url, r.Ok); return Ok(new { ok = true }); }

    public record OrchestrateReq(string Ticket);

    /// <summary>Run the Microsoft-Agent-Framework graph for a cycle ("mutation"|"evolution"): each phase
    /// (research/planning/execution/documentation) runs on its routed agent (persona as Instructions),
    /// sequentially or in parallel per the routing. Returns per-phase output + token usage. This is the
    /// native C# orchestration (claude-cli/cursor-cli phases are run by the local worker CLI instead).</summary>
    [HttpPost("orchestrate/{cycle}")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Orchestrate(string cycle, [FromBody] OrchestrateReq req, CancellationToken ct)
    {
        var r = await _orchestrator.RunAsync(cycle, req.Ticket ?? "", ct);
        return Ok(r);
    }

    /// <summary>Worker posts the live `said stats --json` (it can run said.exe; the container can't).</summary>
    [HttpPost("context/stats")]
    [Authorize(Policy = Policies.CanAdmin)]
    public ActionResult PostBrainStats([FromBody] System.Text.Json.JsonElement body)
    { _evo.SetBrainStats(body.GetRawText()); return Ok(new { ok = true }); }

    static object MaskAgent(Advisory.Api.Policy.AiAgent a) => new
    {
        a.Id, a.Name, a.Standard, a.Model, a.Endpoint, a.CursorUser, a.Persona, a.Enabled, a.Reasoning,
        hasKey = !string.IsNullOrWhiteSpace(a.ApiKey),   // persona is not secret; key never exposed
    };

    /// <summary>Current admin settings (keys masked). Powers the Administration view.</summary>
    [HttpGet("settings")]
    public ActionResult Get()
    {
        var ad = _policy.Current.Admin;
        return Ok(new
        {
            agents = ad.Agents.Select(MaskAgent),
            mutationRouting = ad.MutationRouting,
            evolutionRouting = ad.EvolutionRouting,
            memoryMb = ad.MemoryMb,
            runtime = ad.Runtime,
            database = ad.Database,
            contextFormat = string.IsNullOrWhiteSpace(ad.ContextFormat) ? "said" : ad.ContextFormat,
            // option catalogs so the UI can render dropdowns without hardcoding
            standards = new[] { "anthropic", "openai", "cursor-cli", "claude-cli" },
            runtimes = new[] { "docker", "podman", "none" },
            databases = new[] { "sqlserver", "postgres", "sqlite" },
            contextFormats = new[] { "said", "md" },
            taskKinds = new[] { "research", "planning", "execution", "documentation" },
        });
    }

    public record AdminUpdate(
        List<Advisory.Api.Policy.AiAgent>? Agents,
        Advisory.Api.Policy.TaskRouting? MutationRouting,
        Advisory.Api.Policy.TaskRouting? EvolutionRouting,
        int? MemoryMb, string? Runtime, string? Database, string? ContextFormat);

    /// <summary>Save admin settings into the signed policy (admin only). A blank ApiKey on an agent
    /// keeps that agent's existing key (so the masked UI never wipes a stored secret).</summary>
    [HttpPut("settings")]
    [Authorize(Policy = Policies.CanAdmin)]
    public async Task<ActionResult> Save([FromBody] AdminUpdate req, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(_policy.Current);
        var next = JsonSerializer.Deserialize<FirewallPolicy>(json)!;
        var ad = next.Admin;

        if (req.Agents is not null)
        {
            var existing = ad.Agents.ToDictionary(x => x.Id, x => x.ApiKey);
            foreach (var a in req.Agents)
            {
                // preserve a stored key when the client sends it blank (it only ever received hasKey)
                if (string.IsNullOrWhiteSpace(a.ApiKey) && existing.TryGetValue(a.Id, out var k)) a.ApiKey = k;
            }
            ad.Agents = req.Agents;
        }
        if (req.MutationRouting is not null) ad.MutationRouting = req.MutationRouting;
        if (req.EvolutionRouting is not null) ad.EvolutionRouting = req.EvolutionRouting;
        if (req.MemoryMb is { } m) ad.MemoryMb = Math.Max(0, m);
        if (!string.IsNullOrWhiteSpace(req.Runtime)) ad.Runtime = req.Runtime!;
        if (!string.IsNullOrWhiteSpace(req.Database)) ad.Database = req.Database!;
        if (!string.IsNullOrWhiteSpace(req.ContextFormat)) ad.ContextFormat = req.ContextFormat!;

        await _policy.UpdateAsync(next, _user.Name);
        return Ok(new { saved = true, agents = ad.Agents.Count });
    }

    /// <summary>Download the current project-context "memory" — the .said brain or the .md map —
    /// so it can be used as a portable artifact (said-memory-as-a-service). Served from the repo
    /// mounted read-only at /workspace (falls back to the working dir).</summary>
    [HttpGet("context/download")]
    public ActionResult DownloadContext()
    {
        var fmt = _policy.Current.Admin.ContextFormat;
        var name = string.Equals(fmt, "md", StringComparison.OrdinalIgnoreCase) ? "PROJECT_CONTEXT.md" : "Advisory.said";
        foreach (var root in new[] { "/workspace", Directory.GetCurrentDirectory(), "." })
        {
            var p = Path.Combine(root, name);
            if (System.IO.File.Exists(p))
                return PhysicalFile(Path.GetFullPath(p), "application/octet-stream", name);
        }
        return NotFound(new { error = $"{name} not built yet — run a mutation cycle (the worker builds it once) or rebuild context." });
    }

    /// <summary>Live stats of the project-memory brain (frames, symbols, compression, recalls) plus a
    /// tokens-saved estimate — so the Admin panel shows what the .said memory actually delivers.</summary>
    [HttpGet("context/stats")]
    public ActionResult ContextStats()
    {
        // Locate Advisory.said (read-only repo mount in the container, or cwd).
        string? brain = null;
        foreach (var root in new[] { "/workspace", Directory.GetCurrentDirectory(), "." })
        { var p = Path.Combine(root, "Advisory.said"); if (System.IO.File.Exists(p)) { brain = p; break; } }
        if (brain is null) return Ok(new { built = false, hint = "Brain not built yet — runs on the next cycle." });

        var fi = new FileInfo(brain);
        long frames = 0, symbols = 0, indexDocs = 0; double ratio = 0; long uncompressed = 0;
        long recalls = 0, dreamCycles = 0, boosted = 0;
        // Prefer the worker-posted `said stats --json` (accurate; the worker can run said.exe).
        if (!string.IsNullOrWhiteSpace(_evo.BrainStatsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(_evo.BrainStatsJson!);
                var r = doc.RootElement;
                frames = r.TryGetProperty("active_frames", out var f) ? f.GetInt64() : 0;
                symbols = r.TryGetProperty("symbol_count", out var s) ? s.GetInt64() : 0;
                indexDocs = r.TryGetProperty("index_docs", out var i) ? i.GetInt64() : 0;
                ratio = r.TryGetProperty("compression_ratio", out var c) ? c.GetDouble() : 0;
                uncompressed = r.TryGetProperty("uncompressed_bytes", out var u) ? u.GetInt64() : 0;
                if (r.TryGetProperty("brain", out var b))
                {
                    recalls = b.TryGetProperty("total_recalls", out var tr) ? tr.GetInt64() : 0;
                    dreamCycles = b.TryGetProperty("dream_cycles", out var dc) ? dc.GetInt64() : 0;
                    boosted = b.TryGetProperty("boosted_docs", out var bd) ? bd.GetInt64() : 0;
                }
            }
            catch { /* fall through to file-based estimate */ }
        }
        // Tokens-saved estimate: instead of stuffing the whole indexed corpus into a prompt every run,
        // the agent recalls only the relevant ~top-k frames. ~4 chars/token; recall pulls ~8 frames vs
        // the whole corpus. Conservative, clearly labelled as an estimate.
        long corpusChars = uncompressed > 0 ? uncompressed : fi.Length * 6; // ~6.4x compression typical
        long corpusTokens = corpusChars / 4;
        long recalledFrames = frames > 0 ? Math.Min(frames, 8) : 8;
        long recalledTokens = frames > 0 ? corpusTokens * recalledFrames / frames : corpusTokens / 50;
        long savedPerRun = Math.Max(0, corpusTokens - recalledTokens);

        return Ok(new
        {
            built = true,
            fileBytes = fi.Length,
            frames, symbols, indexDocs,
            recalls, dreamCycles, boosted,
            compressionRatio = Math.Round(ratio, 1),
            estCorpusTokens = corpusTokens,
            estRecalledTokens = recalledTokens,
            estTokensSavedPerRecall = savedPerRun,
            estPercentSaved = corpusTokens > 0 ? (int)(100 * savedPerRun / corpusTokens) : 0,
            updatedAt = fi.LastWriteTimeUtc,
        });
    }

    /// <summary>Resolved per-phase routing for the worker to consume: for a given cycle
    /// ("mutation"|"evolution") return each phase → the full agent spec (standard/model/endpoint/
    /// cursorUser; key still masked) plus the run mode (sequential|parallel). The local worker reads
    /// this to dispatch research/planning/execution/documentation to the right agent.</summary>
    [HttpGet("routing/{cycle}")]
    public ActionResult Routing(string cycle)
    {
        var ad = _policy.Current.Admin;
        var r = cycle?.ToLowerInvariant() == "evolution" ? ad.EvolutionRouting : ad.MutationRouting;
        object? Resolve(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var a = ad.Agents.FirstOrDefault(x => x.Id == id && x.Enabled);
            return a is null ? null : new { a.Id, a.Name, a.Standard, a.Model, a.Endpoint, a.CursorUser, a.Persona, hasKey = !string.IsNullOrWhiteSpace(a.ApiKey) };
        }
        return Ok(new
        {
            cycle = cycle?.ToLowerInvariant() ?? "mutation",
            mode = string.IsNullOrWhiteSpace(r.Mode) ? "sequential" : r.Mode,
            research = Resolve(r.Research),
            planning = Resolve(r.Planning),
            execution = Resolve(r.Execution),
            documentation = Resolve(r.Documentation),
        });
    }
}
