using System.Collections.Concurrent;
using Advisory.Api.Gate;
using Advisory.Api.Models;

namespace Advisory.Api.Scan;

/// <summary>One ad-hoc scan triggered from the On-Demand Scanning screen (JFrog parity).</summary>
public class OnDemandScan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..12];
    public string FileName { get; set; } = "";          // "lodash@4.17.21" (npm)
    public Ecosystem Ecosystem { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Status { get; set; } = "Scanning";    // Scanning | Done | Failed
    public string TopSeverity { get; set; } = "None";   // worst finding severity
    public int SecurityIssues { get; set; }              // total findings across tree
    public int Violations { get; set; }                  // triggered policy controls
    public string Decision { get; set; } = "";          // Allow | Block | Quarantine
    public DateTimeOffset ScanDate { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// In-memory register of on-demand scans. Each scan runs the real gate engine in the background
/// (same engine as the promotion gate) and the row updates from Scanning → Done with the results.
/// </summary>
public class OnDemandScanService
{
    private readonly ConcurrentDictionary<string, OnDemandScan> _scans = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OnDemandScanService> _log;

    public OnDemandScanService(IServiceScopeFactory scopes, ILogger<OnDemandScanService> log)
    { _scopes = scopes; _log = log; }

    public IReadOnlyList<OnDemandScan> List() =>
        _scans.Values.OrderByDescending(s => s.ScanDate).ToList();

    public OnDemandScan Start(PackageRef pkg)
    {
        var scan = new OnDemandScan
        {
            FileName = $"{pkg.Name}@{pkg.Version}",
            Ecosystem = pkg.Ecosystem, Name = pkg.Name, Version = pkg.Version,
        };
        _scans[scan.Id] = scan;
        _ = Task.Run(() => RunAsync(scan, pkg));
        return scan;
    }

    /// <summary>Record an on-demand scan only if this name@version isn't already in the history —
    /// avoids duplicate rows when Catalog search/CVEs re-scans the same package.</summary>
    public OnDemandScan? StartIfAbsent(PackageRef pkg)
    {
        if (_scans.Values.Any(s => string.Equals(s.Name, pkg.Name, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(s.Version, pkg.Version, StringComparison.OrdinalIgnoreCase)))
            return null;
        return Start(pkg);
    }

    private async Task RunAsync(OnDemandScan scan, PackageRef pkg)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var gate = scope.ServiceProvider.GetRequiredService<IGateEngine>();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var r = await gate.EvaluateAsync(pkg, cts.Token);
            var findings = r.TreeFindings?.Select(t => t.Finding).ToList() ?? new List<Finding>();
            scan.SecurityIssues = findings.Count;
            scan.TopSeverity = findings.Count > 0 ? findings.Max(f => f.Severity).ToString() : "None";
            scan.Violations = r.TriggeredRules.Count;
            scan.Decision = r.Decision.ToString();
            scan.Status = "Done";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "On-demand scan failed for {Pkg}", scan.FileName);
            scan.Status = "Failed";
        }
    }
}
