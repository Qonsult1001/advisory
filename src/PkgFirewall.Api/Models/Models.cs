using System.Text.Json.Serialization;

namespace PkgFirewall.Api.Models;

public enum Ecosystem { PyPI, npm, NuGet, Cargo, Go, HuggingFace }

public enum Severity { None, Low, Medium, High, Critical }

public enum GateDecision { Allow, Block, Quarantine }

/// <summary>Per-source health for one evaluation — proves coverage, exposes gaps.</summary>
public record SourceCoverage(
    string Source,
    string Status,           // Ok / Empty / Errored / Timeout / NotConfigured / Skipped
    int FindingCount,
    string? Detail,
    long ElapsedMs,
    bool Required);

/// <summary>Aggregate coverage across all sources for the evaluation.</summary>
public record CoverageReport(
    IReadOnlyList<SourceCoverage> Sources,
    bool AllRequiredConclusive,        // false => decision is Indeterminate, not clean
    IReadOnlyList<string> Gaps);       // human-readable list of what's missing & why

/// <summary>A package coordinate requested through the firewall proxy.</summary>
public record PackageRef(
    Ecosystem Ecosystem,
    string Name,
    string Version,
    string? Sha256 = null,
    string? FileName = null,
    string? LocalPath = null,
    string? ProjectPath = null);   // consuming project source root, for npm reachability/contextual analysis

/// <summary>A categorized advisory reference link (NVD, GHSA, patch commit, exploit PoC, etc.).</summary>
public record AdvisoryRef(string Type, string Url);  // Type: Advisory / Exploit / Patch / Web / Package / Report

/// <summary>A single vulnerability finding from any VulnSource plugin.</summary>
public record Finding(
    string Id,                 // CVE / GHSA / OSV id
    Severity Severity,
    double? CvssScore,         // 0-10, nullable when feed has no score
    double? EpssScore,         // 0-1 exploit probability
    bool KnownExploited,       // on a KEV list
    string Source,             // which plugin produced this (osv, kev, vulncheck...)
    string? Summary = null,
    string? FixedVersion = null, // nearest non-vulnerable version to upgrade to (OSV "fixed" event), if known
    string? Reachability = null, // Reachable / NotReachable / Unknown — contextual analysis (npm), null if not run
    string? ReachabilityDetail = null, // why: e.g. "imported and called at src/api.js" or "package never imported"
    // --- rich advisory detail (captured from OSV; same data JFrog surfaces) ---
    IReadOnlyList<string>? Aliases = null,       // CVE/GHSA/PYSEC ids that name the same vuln
    string? CvssVector = null,                   // e.g. CVSS:3.1/AV:N/AC:L/...
    IReadOnlyList<string>? Cwes = null,          // CWE ids
    string? PublishedAt = null,                  // NVD/advisory publish date
    IReadOnlyList<AdvisoryRef>? References = null); // categorized reference links

/// <summary>A finding tied to the specific tree node (direct or transitive) it came from.</summary>
public record TreeFinding(string Component, int Depth, Finding Finding);

/// <summary>
/// A structured policy violation — the projection of a non-Allow decision into the
/// "what broke policy" view (vs the raw decision ledger). Severity is the worst finding/rule
/// severity; Status is Open unless a matching, unexpired exception waives it.
/// </summary>
public record Violation(
    Guid Id,
    string Resource,           // ecosystem:name@version
    Ecosystem Ecosystem,
    GateDecision Decision,     // Block or Quarantine
    Severity Severity,
    IReadOnlyList<string> Rules,
    string Status,             // Open / Waived
    string? WaivedBy,          // exception ticket, when waived
    DateTimeOffset DetectedAt,
    string? Watch = null);     // which watch's scope this violation falls under

/// <summary>Result of evaluating the FULL dependency tree against all sources + policy.</summary>
public record GateResult(
    PackageRef Package,
    GateDecision Decision,
    IReadOnlyList<Finding> Findings,          // flattened, for quick display
    IReadOnlyList<string> TriggeredRules,
    string? ExceptionRef,
    DateTimeOffset EvaluatedAt,
    int ComponentsEvaluated = 1,              // size of resolved tree
    IReadOnlyList<TreeFinding>? TreeFindings = null,
    string? SbomPurl = null,
    CoverageReport? Coverage = null,
    string? ResearchRationale = null,         // agent-written audit explanation
    Catalog.OperationalRisk? OperationalRisk = null); // JFrog-style operational-risk analysis of the root package

/// <summary>Immutable audit entry. One per gate evaluation.</summary>
public record AuditEntry(
    Guid Id,
    PackageRef Package,
    GateDecision Decision,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> TriggeredRules,
    string? ExceptionRef,
    string PolicyVersion,
    int ComponentsEvaluated,
    DateTimeOffset Timestamp,
    CoverageReport? Coverage = null,
    string? ResearchRationale = null,
    string Actor = "system");
