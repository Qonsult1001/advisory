using System.Collections.Concurrent;
using Advisory.Api.Gate;
using Advisory.Api.Models;
using Advisory.Api.Nexus;
using Microsoft.AspNetCore.Mvc;

namespace Advisory.Api.Proxy;

/// <summary>
/// The reverse proxy that makes `pip install` work FIRST TRY, no retries. Developers (via IT-pushed
/// config) point pip at this endpoint instead of Nexus. Per request:
///   - INDEX (/pypi/simple/&lt;pkg&gt;/): proxied through from Nexus quarantine (which fetches+caches
///     pypi.org), with artifact URLs rewritten back to this proxy so pip's downloads come here.
///   - ARTIFACT (/pypi/packages/...): try the approved repo first (200 → stream, done). On a miss,
///     single-flight per file: fetch from quarantine, run the FAST gate (coordinate checks + content
///     scan of the bytes — no transitive tree), and on Allow promote-all-files + stream; on Block 403.
///     The slow transitive-tree CVE walk runs async afterwards and can revoke.
/// The proxy STREAMS bytes (never buffers whole wheels) and is the SOLE endpoint devs talk to — Nexus
/// stays internal. v1 = PyPI; the engine is ecosystem-pluggable (npm/etc. are adapters).
/// </summary>
[ApiController]
public sealed class PackageProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IServiceScopeFactory _scopes;
    private readonly INexusClient _nexus;
    private readonly IConfiguration _cfg;
    private readonly ILogger<PackageProxyController> _log;
    private readonly DevIdentity _identity;
    private readonly Advisory.Api.Scan.ScanStore _scans;
    private readonly IReadOnlyDictionary<string, IEcosystemProxyAdapter> _adapters;

    // Single-flight: one gate+promote per {repo|file} in flight; concurrent requests share the Task.
    private static readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _inflight = new(StringComparer.OrdinalIgnoreCase);

    // Concurrency gate: bound how many cold fetch+gate operations run against Nexus/upstream at once.
    // This is the "queue" — under congestion, extra requests wait IN LINE for a slot and release on the
    // real completion of the work ahead of them, with NO hard-coded timeout. Sized from PROXY_GATE_CONCURRENCY
    // (default 6). A held pip request that's waiting for a slot is kept alive by the raised client timeout
    // (IT pushes pip `timeout`); it never sits silent past pip's window because work drains in order.
    private static SemaphoreSlim? _gate;
    private static readonly object _gateInit = new();

    public PackageProxyController(IHttpClientFactory httpFactory, IServiceScopeFactory scopes,
        INexusClient nexus, IConfiguration cfg, ILogger<PackageProxyController> log,
        DevIdentity identity, Advisory.Api.Scan.ScanStore scans,
        IEnumerable<IEcosystemProxyAdapter> adapters)
    {
        _httpFactory = httpFactory; _scopes = scopes; _nexus = nexus; _cfg = cfg; _log = log;
        _identity = identity; _scans = scans;
        _adapters = adapters.ToDictionary(a => a.RoutePrefix, StringComparer.OrdinalIgnoreCase);
        if (_gate is null)
            lock (_gateInit)
                _gate ??= new SemaphoreSlim(Math.Max(1, cfg.GetValue("PROXY_GATE_CONCURRENCY", 6)));
    }

    private string NexusBase => (_cfg["NEXUS_URL"] ?? "http://nexus:8081").TrimEnd('/');
    private HttpClient Http() => _httpFactory.CreateClient("nexus");

    // ─────────────────────────── generic index (any ecosystem) ───────────────────────────

    /// <summary>Index/metadata request for ANY gated ecosystem. The first path segment selects the adapter
    /// (pypi/npm/nuget/go); the adapter maps the rest to a Nexus quarantine URL and rewrites artifact links
    /// back to this proxy so the client downloads through the gate. Binary metadata is passed through as-is.</summary>
    [HttpGet("/{prefix}/index/{**rest}")]
    public async Task<IActionResult> Index(string prefix, string rest, CancellationToken ct)
    {
        if (!_adapters.TryGetValue(prefix, out var adapter)) return NotFound();
        var mapped = adapter.MapIndexRequest(rest ?? "", NexusBase);
        if (mapped is null) return NotFound();
        using var resp = await Http().GetAsync(mapped.Value.upstreamUrl, ct);
        if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode);
        var isText = mapped.Value.contentType.StartsWith("text/") || mapped.Value.contentType.Contains("json") || mapped.Value.contentType.Contains("xml");
        if (!isText)
        {
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            return new FileStreamResult(stream, resp.Content.Headers.ContentType?.MediaType ?? mapped.Value.contentType);
        }
        var body = await resp.Content.ReadAsStringAsync(ct);
        body = adapter.RewriteIndex(body, NexusBase);
        return Content(body, mapped.Value.contentType);
    }

    // Back-compat: pip is configured with ".../pypi/simple/" as its index-url, so pip requests
    // "/pypi/simple/<name>". Route that shape to the generic handler with rest = "simple/<name>".
    [HttpGet("/pypi/simple/{name}")]
    public Task<IActionResult> PyPiSimple(string name, CancellationToken ct)
        => Index("pypi", $"simple/{name}", ct);

    // ─────────────────────────── generic artifact (any ecosystem) ───────────────────────────

    /// <summary>Artifact download for ANY gated ecosystem. Adapter maps the path to approved/quarantine
    /// Nexus URLs and parses {name,version,file}; the gate/recall/exposure/stream logic below is shared.</summary>
    [HttpGet("/{prefix}/artifact/{**rest}")]
    public async Task<IActionResult> Artifact(string prefix, string rest, CancellationToken ct)
    {
        if (!_adapters.TryGetValue(prefix, out var adapter)) return NotFound();
        return await GatedArtifact(adapter, rest ?? "", ct);
    }

    // Back-compat PyPI artifact route (pip downloads via ".../pypi/packages/...").
    [HttpGet("/pypi/packages/{**rest}")]
    public Task<IActionResult> PyPiArtifact(string rest, CancellationToken ct)
        => GatedArtifact(_adapters["pypi"], $"packages/{rest}", ct);

    // The shared gate-then-serve pipeline, ecosystem-agnostic via the adapter.
    private async Task<IActionResult> GatedArtifact(IEcosystemProxyAdapter adapter, string rest, CancellationToken ct)
    {
        var eco = adapter.Ecosystem;
        var mapped = adapter.MapArtifactRequest(rest, NexusBase);
        if (mapped is null) return NotFound();
        var (approvedUrl, quarantineUrl) = mapped.Value;

        // Harmless metadata sidecar (PEP 658 .metadata, npm doc, Go .info/.mod) — serve without gating so
        // the client can resolve; carries no executable bytes.
        if (adapter.IsUngatedMetadata(rest))
            return await ExistsAsync(quarantineUrl, ct) ? await StreamAsync(quarantineUrl, rest, ct)
                 : await ExistsAsync(approvedUrl, ct) ? await StreamAsync(approvedUrl, rest, ct) : NotFound();

        // Derive {name, version, file} up-front — needed both to re-gate an approved hit and to gate a miss.
        var (name, version, fileName) = adapter.ParseArtifactPath(rest);

        // 1) Fast path: already approved? Aligned with the JFrog Xray posture for CACHED artifacts — the
        //    bytes in <eco>-approved are IMMUTABLE, but the vulnerability knowledge (OSV/KEV/malware) is not.
        //    So we RE-RUN the fast coordinate gate against CURRENT policy on EVERY request before serving.
        //    This is metadata-only (no bytes re-downloaded, no re-unpack) → ~sub-second, and it CLOSES the
        //    approved-cache-bypass: a freshly-disclosed CVE on an already-approved package blocks on the very
        //    next pull instead of waiting for a background sweep. Content threats were already caught at
        //    promote time and can't change for immutable cached bytes, so only the coordinate dimension needs
        //    re-checking. Zero staleness window — stronger than Xray's hourly DB-sync-then-rescan.
        if (await ExistsAsync(approvedUrl, ct))
        {
            if (name is not null)
            {
                var stillOk = await CoordinateGateOnceAsync(eco, name, version!, fileName!, ct);
                if (!stillOk)
                {
                    // Fresh block on a previously-approved package → pull it from approved, flag everyone who
                    // already installed it for RECALL, and return 403 WITH remediation instructions.
                    try { await _nexus.RevokeApprovedAsync(eco, name, version!, ct); } catch { }
                    _scans.MarkRevoked(eco, name, version!);
                    return await BlockedWithRemediationAsync(eco, name, version!, "re-gate on current policy");
                }
            }
            // Cleared: record that this asset now has this exact version (exposure), then stream.
            _scans.RecordServed(eco, name ?? rest, version ?? "", _identity.Resolve(HttpContext), _identity.CaptureAsset(HttpContext));
            return await StreamAsync(approvedUrl, rest, ct);
        }

        // 2) Miss: gate-then-serve (single-flight).
        if (name is null)
        {
            // Not a gateable artifact path — best-effort passthrough from quarantine.
            return await ExistsAsync(quarantineUrl, ct) ? await StreamAsync(quarantineUrl, rest, ct) : NotFound();
        }

        // STAGE 1 — coordinate gate (OSV/malware/KEV/cooling-off) on metadata only, NO bytes fetched yet.
        // Single-flight so a burst for the same file gates once. Known-bad → 403 WITH remediation, 0 bytes.
        var coordOk = await CoordinateGateOnceAsync(eco, name, version!, fileName!, ct);
        if (!coordOk) return await BlockedWithRemediationAsync(eco, name, version!, "known-vulnerable at gate");

        // STAGE 2 — clean on coordinates: fetch into quarantine, then STREAM the bytes to the client WHILE
        // content-scanning the same bytes. The client sees data flowing (its read-timeout never fires, any
        // size); a content threat mid-stream ABORTS the connection (client discards the broken download).
        // Promotion + the full-tree deep scan run async. Capture WHO + WHICH ASSET is pulling now;
        // GatedStreamResult records the exposure only once the tail is released (scan cleared → usable file).
        var pulledBy = _identity.Resolve(HttpContext);
        var asset = _identity.CaptureAsset(HttpContext);
        await _nexus.FetchIntoQuarantineAsync(eco, name!, version!, ct);
        return new GatedStreamResult(this, eco, name!, version!, fileName!, quarantineUrl, pulledBy, asset);
    }

    // ─────────────────────────── remediation ───────────────────────────

    // Build the 403 for a blocked package, carrying REMEDIATION a developer can act on: the exact commands
    // to remove the bad version and install a gate-verified safe one. Also flags every developer who already
    // installed this exact version for RECALL (retroactive: a package approved before a CVE was disclosed).
    internal Task<IActionResult> BlockedWithRemediationAsync(Ecosystem eco, string name, string version, string why)
    {
        // Safe versions are computed + cached by the gate pipeline (SetSafeVersions) when a package is
        // blocked; read them here. If not yet present, the async deep-scan/recommend fills them shortly —
        // we never block the 403 (and the recall list) on that compute. Kick a best-effort compute so the
        // next fetch of this 403 (pip retries, or the console) has the safe version.
        var (nearest, latest) = _scans.GetSafeVersions(eco, name, version);
        if (nearest is null && latest is null)
            _ = ComputeSafeVersionsAsync(eco, name, version);
        var safeVersion = nearest ?? latest;
        var cve = FirstCve(eco, name, version);

        // Flag recall for anyone who already has this version (attribute CVE + safe version so the console
        // can tell each of them exactly what to do). Returns how many developers are affected.
        var affected = _scans.FlagRecall(eco, name, version, cve, safeVersion);
        if (affected > 0)
            _log.LogWarning("proxy RECALL — {Name}@{Ver} revoked; {N} developer(s) have it installed and must remove it", name, version, affected);
        _log.LogWarning("proxy BLOCKED {Name}@{Ver} ({Why}); remediation → {Safe}", name, version, why, safeVersion ?? "no safe version found");

        var tool = EcosystemCommands.Tool(eco);
        var uninstall = EcosystemCommands.Uninstall(eco, name);
        var install = EcosystemCommands.Install(eco, name, safeVersion);
        var detail = install is not null
            ? $"{name}=={version} is blocked ({why}). Remove it and install the gate-verified safe version:\n  {uninstall}\n  {install}"
            : $"{name}=={version} is blocked ({why}). Remove it: {uninstall}. No gate-verified safe version was found — contact your security team.";

        Response.StatusCode = 403;
        return Task.FromResult<IActionResult>(new JsonResult(new
        {
            type = "https://advisory/blocked",
            title = "Package blocked by policy",
            status = 403,
            detail,
            package = name,
            version,
            ecosystem = eco.ToString(),
            reason = why,
            cve,
            remediation = new { tool, uninstall, install, safeNearest = nearest, safeLatest = latest },
        })
        { ContentType = "application/problem+json" });
    }

    /// <summary>Called by GatedStreamResult once a developer receives a usable (clean) artifact — records
    /// the exposure so this exact version can be recalled from that asset if it is later revoked.</summary>
    internal void RecordExposure(Ecosystem eco, string name, string version, string user, Advisory.Api.Scan.ScanStore.AssetInfo asset)
        => _scans.RecordServed(eco, name, version, user, asset);

    // Best-effort background compute of gate-verified safe versions for a blocked package, cached for the
    // remediation payload + recall list. Fire-and-forget; failures are swallowed (the 403 still went out).
    private async Task ComputeSafeVersionsAsync(Ecosystem eco, string name, string version)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var gate = scope.ServiceProvider.GetRequiredService<IGateEngine>();
            var rec = scope.ServiceProvider.GetRequiredService<Advisory.Api.Nexus.SafeVersionRecommender>();
            var pkg = new PackageRef(eco, name, version);
            var blocked = await gate.EvaluateFastAsync(pkg, default);
            var safe = await rec.RecommendAsync(pkg, blocked, default);
            if (safe.Nearest is not null || safe.Latest is not null)
                _scans.SetSafeVersions(eco, name, version, safe.Nearest, safe.Latest);
        }
        catch { /* best-effort */ }
    }

    // The primary CVE id recorded for this version (for the remediation payload), if we have a stored scan.
    private string? FirstCve(Ecosystem eco, string name, string version)
    {
        foreach (var repo in new[] { "pypi-quarantine", "pypi-approved", "npm-quarantine", "npm-approved" })
        {
            var s = _scans.Get(repo, name, version);
            var v = s?.Vulnerabilities?.FirstOrDefault();
            if (v is not null) return v.Id;
        }
        return null;
    }

    // Coordinate-only gate (no artifact bytes). Single-flight per file. Content threats are caught later,
    // while streaming (Stage 2). Returns true if the coordinate checks allow it.
    private Task<bool> CoordinateGateOnceAsync(Ecosystem eco, string name, string version, string fileName, CancellationToken ct)
    {
        var key = $"coord|{eco}|{name}|{version}|{fileName}";
        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<bool>>(() => DoCoordinateGateAsync(eco, name, version, fileName, ct)));
        var task = lazy.Value;
        _ = task.ContinueWith(_ => _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<bool>>>(key, lazy)), TaskScheduler.Default);
        return task;
    }

    private async Task<bool> DoCoordinateGateAsync(Ecosystem eco, string name, string version, string fileName, CancellationToken ct)
    {
        await _gate!.WaitAsync(ct);
        try
        {
            using var scope = _scopes.CreateScope();
            var gate = scope.ServiceProvider.GetRequiredService<IGateEngine>();
            // No LocalPath ⇒ coordinate-only (content-scan recorded Skipped; it's not a required source,
            // so this doesn't force Quarantine). CVE/malware/KEV/cooling-off decide here.
            var pkg = new PackageRef(eco, name, version, Sha256: null, FileName: fileName, LocalPath: null);
            var result = await gate.EvaluateFastAsync(pkg, ct);
            if (result.Decision != GateDecision.Allow)
            {
                _log.LogWarning("proxy BLOCKED (coordinates) {Name}@{Ver}: {Rules}", name, version, string.Join("; ", result.TriggeredRules));
                return false;
            }
            return true;
        }
        catch (Exception ex) { _log.LogWarning(ex, "proxy coordinate gate failed for {Name}@{Ver}", name, version); return false; }
        finally { _gate!.Release(); }
    }

    // Content-scan the COMPLETE artifact bytes (secrets/IaC/pickle). Called by GatedStreamResult once the
    // whole artifact has been received but BEFORE its final chunk is released to pip. Returns true = clean
    // (release the tail) / false = content threat (withhold the tail → uninstallable). On clean, promote so
    // the next pull is an instant cache hit + kick the async full-tree deep scan.
    internal async Task<bool> ContentScanAndPromoteAsync(Ecosystem eco, string name, string version, string fileName, string quarantineUrl, byte[] bytes)
    {
        string? temp = null;
        try
        {
            temp = Path.Combine(Path.GetTempPath(), $"advproxy-{Guid.NewGuid():N}-{fileName}");
            await System.IO.File.WriteAllBytesAsync(temp, bytes);
            using var scope = _scopes.CreateScope();
            var gate = scope.ServiceProvider.GetRequiredService<IGateEngine>();
            // COMPLETION GATE = FULL TREE. Run the FULL transitive evaluation (not root-only) on the
            // downloaded bytes before releasing the tail. This is what makes "every dependent package the
            // tree requires falls under policy BEFORE it reaches the developer" true: if ANY node in the
            // dependency tree is malicious/known-exploited/high-CVSS, the whole package is withheld and
            // uninstallable — a risky transitive dep can no longer be detected-after-serve. We can afford
            // the full walk here because the bytes are already downloaded and the withhold-tail keeps the
            // client's connection alive (no timeout) while it runs (~2s for a typical tree). The content
            // scan (secrets/IaC/pickle on these bytes) still runs as part of the same evaluation via LocalPath.
            var pkg = new PackageRef(eco, name, version, Sha256: null, FileName: fileName, LocalPath: temp);
            var result = await gate.EvaluateAsync(pkg, default);
            var clean = result.Decision == GateDecision.Allow;
            if (clean)
            {
                var comp = new NexusComponent("", eco, name, version, fileName, null, quarantineUrl);
                _ = Task.Run(async () =>
                {
                    try { await _nexus.PromoteAsync(comp, bytes, default); } catch { }
                    try { await _nexus.PromoteAllFilesAsync(comp, default); } catch { }
                });
            }
            else
            {
                // A tree node (or the content scan) failed — record the block so the console shows WHY,
                // and never re-promote this version. The tail is withheld → the package is uninstallable.
                var scans = scope.ServiceProvider.GetRequiredService<Advisory.Api.Scan.ScanStore>();
                scans.MarkRevoked(eco, name, version);
                try { await scans.RecordDecisionAsync($"{NexusEcosystems.Prefix(eco)}-quarantine", pkg, result); } catch { }
                var badNodes = (result.TreeFindings ?? Array.Empty<TreeFinding>())
                    .Where(tf => tf.Depth > 0).Select(tf => tf.Component).Distinct().Take(5);
                _log.LogWarning("proxy TREE-BLOCK {Name}@{Ver} [{File}] — withholding tail (uninstallable): {Rules}. Bad deps: {Deps}",
                    name, version, fileName, string.Join("; ", result.TriggeredRules), string.Join(", ", badNodes));
            }
            return clean;
        }
        catch (Exception ex) { _log.LogWarning(ex, "content scan failed for {Name}@{Ver} — withholding tail (fail-closed)", name, version); return false; }
        finally { if (temp is not null) try { System.IO.File.Delete(temp); } catch { } }
    }

    internal HttpClient NexusHttp() => Http();
    internal string NexusBaseUrl => NexusBase;


    // (The former async DeepScanAsync is gone: the full transitive tree is now gated SYNCHRONOUSLY at
    // completion in ContentScanAndPromoteAsync — a bad tree node withholds the tail before serve, rather
    // than being revoked after serve. The approved-path per-request re-gate covers cache freshness.)

    // ─────────────────────────── helpers ───────────────────────────

    private async Task<bool> ExistsAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await Http().SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // Stream the upstream response body straight to the client — never buffer the whole artifact.
    private async Task<IActionResult> StreamAsync(string url, string rest, CancellationToken ct)
    {
        var resp = await Http().GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode);
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new FileStreamResult(stream, contentType);
    }
}
