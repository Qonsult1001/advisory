using System.Text.Json.Serialization;
using Advisory.Api.Models;

namespace Advisory.Api.Policy;

/// <summary>
/// The firewall policy. Editable by SecOps via the React UI / API.
/// Every field maps to a named bank control so the policy doc IS compliance evidence.
/// Versioned + signed so each audit entry references the exact policy that decided it.
/// </summary>
public class FirewallPolicy
{
    public string Version { get; set; } = "1";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = "system";

    // --- CVE / severity gate (control: SEC-VULN-01) ---
    public double CvssBlockThreshold { get; set; } = 7.0;   // block High/Critical

    // --- Known-exploited gate (control: SEC-VULN-02) ---
    public bool BlockKnownExploited { get; set; } = true;   // anything on KEV -> hard block

    // --- Exploit-likelihood gate (control: SEC-VULN-03) ---
    public double EpssBlockThreshold { get; set; } = 0.5;   // block if >50% exploit prob

    // --- License gate (control: LEG-LIC-01) ---
    public List<string> LicenseBlocklist { get; set; } = new() { "GPL-3.0", "AGPL-3.0" };

    // --- Supply-chain freshness gate (control: SEC-SC-01) — JFrog Curation's "immature version"
    //     condition: a version younger than this is blocked (typosquat / hijacked-release window). ---
    public int MinPackageAgeDays { get; set; } = 14;

    // --- Operational risk gate (control: SEC-OPR-01) — JFrog Xray operational-risk model:
    //     EOL/deprecated, version age, # newer versions, release cadence → High/Medium/Low/None.
    //     Action on High: "Disabled" | "Notify" (record, don't block) | "Block". ---
    public string OperationalRiskAction { get; set; } = "Notify";

    // --- OpenSSF Scorecard gate (control: SEC-OSSF-01) — JFrog Curation's OpenSSF condition.
    //     0 disables; otherwise block when the project's overall scorecard score is below this. ---
    public double MinScorecardScore { get; set; } = 0;

    // --- Transitive resolution depth (control: SEC-SC-02) ---
    public int MaxTreeDepth { get; set; } = 8;

    // --- Model weights gate (control: SEC-AIML-01) ---
    public WeightsPolicy Weights { get; set; } = new();

    // --- AI Catalog registry (control: SEC-AIML-02) — JFrog AI Catalog parity. The allow-list of
    //     approved models; when enforcement is on, a HuggingFace model NOT on the list is blocked. ---
    public List<AllowedModel> AllowedModels { get; set; } = new();
    public bool EnforceModelAllowList { get; set; } = false;   // off by default: approve models first

    // --- LLM Gateway (controls SEC-LLM-01/02): route OpenAI/Anthropic API traffic through the
    //     firewall — every call recorded; outbound prompts scanned for embedded secrets (DLP). ---
    public LlmGatewayPolicy Llm { get; set; } = new();

    // --- AppTrust applications: the org's registered applications (JFrog AppTrust parity).
    //     Packages bound to an application drive its post-release CVE monitoring. ---
    public List<AppRecord> Applications { get; set; } = new()
    {
        new AppRecord {
            Key = "payments-api", Name = "Payments API", Project = "fin-core", Criticality = "High",
            Type = "service", Team = "backend", Owners = "platform@bank.local",
            Description = "Card-not-present payment processing service",
            Packages = new() { "npm:express", "npm:lodash", "PyPI:requests" } },
        new AppRecord {
            Key = "risk-engine", Name = "Risk Engine", Project = "fin-core", Criticality = "High",
            Type = "service", Team = "risk", Owners = "risk@bank.local",
            Description = "Real-time transaction risk scoring",
            Packages = new() { "PyPI:numpy", "PyPI:pandas", "HuggingFace:sentence-transformers/all-MiniLM-L6-v2" } },
    };

    // --- Time-boxed approved exceptions (replaces the ticket queue) ---
    public List<PolicyException> Exceptions { get; set; } = new();

    // --- Enabled intel plugins, in priority order (pluggable org platform) ---
    public List<string> EnabledSources { get; set; } = new() { "osv", "kev", "epss", "malware", "artifactory" };

    // --- Admin-managed source configuration (credentials/endpoints) for the built-in source types,
    //     keyed by source key. Credentials entered via the admin UI are stored here (self-hosted). ---
    public List<SourceConfig> SourceConfigs { get; set; } = new();

    // --- Custom OSV-format feed mirrors added by an admin (real add/remove). Each is queried as an
    //     extra IVulnSource. Lets a bank point at an on-prem OSV mirror with zero egress. ---
    public List<CustomSource> CustomSources { get; set; } = new();

    // --- Sources that MUST return conclusively for a clean Allow (control SEC-COV-01) ---
    public List<string> RequiredSources { get; set; } = new() { "osv", "malware" };

    // --- If a required source can't be reached, quarantine instead of allow (control SEC-COV-02) ---
    public bool QuarantineOnUncertainty { get; set; } = true;

    // --- Have the research agent write an audit rationale per decision (control SEC-AUD-03) ---
    public bool EnableResearchAgent { get; set; } = true;

    // --- AI assistant / research-agent settings. The Groq API key is entered via the admin UI and
    //     stored server-side in the signed policy (self-hosted). Falls back to env GROQ_API_KEY when blank. ---
    public AiSettings Ai { get; set; } = new();

    // --- Admin Center: configured AI agents, per-task routing, memory + DB/runtime selection. ---
    public AdminSettings Admin { get; set; } = new();

    // --- Scan artifact content for embedded secrets + IaC misconfigurations when bytes are
    //     available (controls SEC-SECRET-01 / SEC-IAC-01). High-severity hits block. ---
    public bool EnableContentScan { get; set; } = true;

    // --- Contextual analysis / reachability (control SEC-REACH-01). When a consuming project is
    //     supplied (npm), annotate findings with reachability. When DowngradeUnreachable is on, a
    //     finding PROVEN not-reachable does not block on its own (Unknown/Reachable still block). ---
    public bool EnableReachability { get; set; } = true;
    public bool DowngradeUnreachable { get; set; } = false; // conservative default: off

    // --- Manually-linked git repositories (control: SEC-SRC-01). Replaces the auto-listing of
    //     all private repos so that only explicitly approved repos appear under observation.
    //     Admins add/remove via POST/DELETE /api/scans/git-repositories (Admin-only write, persisted
    //     here so every change is versioned and auditable in the signed policy). ---
    public List<LinkedGitRepo> LinkedGitRepos { get; set; } = new();

    // --- Watches: named bindings of a rule-set to a resource scope (JFrog-style organization).
    //     The gate's flat controls above are the engine; watches are the governance view that
    //     scopes/labels which rules apply where, and own the notify/block actions per scope. ---
    public List<Watch> Watches { get; set; } = new()
    {
        new Watch {
            Name = "PROD-watch", Description = "Production promotion gate — block on high vulnerability",
            Ecosystems = new(), Enabled = true,
            PolicyName = "Block-Promotion-On-High-Vulnerability", PolicyType = "Security",
            Rules = new() {
                new WatchRule { Name = "Block-high-vuln", Type = "CVEs", MinSeverity = "High", Block = true, Notify = true },
                new WatchRule { Name = "Block-known-exploited", Type = "CVEs", KnownExploitedOnly = true, Block = true, Notify = true },
                new WatchRule { Name = "Block-malicious", Type = "Malicious", Block = true, Notify = true },
            }
        },
        new Watch {
            Name = "Security-watch", Description = "All security findings, notify only (visibility)",
            Ecosystems = new(), Enabled = true,
            PolicyName = "Security_policy_1", PolicyType = "Security",
            Rules = new() {
                new WatchRule { Name = "All-CVEs", Type = "CVEs", MinSeverity = "Low", Block = false, Notify = true },
            }
        },
        new Watch {
            Name = "License-watch", Description = "Prohibited-license enforcement",
            Ecosystems = new(), Enabled = true,
            PolicyName = "license-policy", PolicyType = "License",
            Rules = new() {
                new WatchRule { Name = "Block-prohibited-licenses", Type = "License", Block = true, Notify = true },
            }
        },
    };
}

/// <summary>LLM Gateway controls. Clients point their SDK base URL at the firewall; calls are
/// forwarded to the real provider with full audit records and prompt secret-scanning (DLP).</summary>
public class LlmGatewayPolicy
{
    public bool Enabled { get; set; } = true;
    public bool AllowOpenAI { get; set; } = true;       // control SEC-LLM-01: provider allow-list
    public bool AllowAnthropic { get; set; } = true;
    public bool AllowGroq { get; set; } = true;
    public List<string> BlockedModels { get; set; } = new();  // e.g. "gpt-3.5-turbo" — deny-list

    // --- Outbound DLP (control SEC-LLM-02): inspect every prompt before it leaves the perimeter.
    //     Scan = detect & record; Block = also reject the call. POPIA/GDPR PII, payment cards,
    //     secrets, and proprietary source code each have an independent scan + block toggle. ---
    public bool CaptureTranscripts { get; set; } = true;   // store a REDACTED preview of each prompt
    public bool UseAiDlp { get; set; } = true;             // Groq fallback for free-text PII/code
    public bool UsePrivacyFilter { get; set; } = true;     // on-prem OpenAI Privacy Filter as primary PII engine

    public bool ScanPii { get; set; } = true;
    public bool BlockPii { get; set; } = false;            // notify by default; flip to enforce
    public bool ScanCards { get; set; } = true;
    public bool BlockCards { get; set; } = true;           // card numbers leaving = hard block
    public bool ScanSecrets { get; set; } = true;
    public bool BlockSecrets { get; set; } = true;
    public bool ScanCode { get; set; } = true;
    public bool BlockCode { get; set; } = false;           // notify by default (dev help is legitimate)

    // --- Custom DLP rules added by an admin via the UI. Each is a named regex; matches are recorded
    //     and (if Block) reject the call. Lets compliance add org-specific patterns (employee IDs,
    //     project codenames, internal hostnames) without a code change. ---
    public List<CustomDlpRule> CustomDlpRules { get; set; } = new();
}

/// <summary>An admin-defined DLP pattern (control SEC-LLM-02).</summary>
public class CustomDlpRule
{
    public string Name { get; set; } = "";        // e.g. "EMPLOYEE_ID"
    public string Pattern { get; set; } = "";      // a .NET regex
    public bool Block { get; set; } = false;       // block the call, or just record
    public bool Enabled { get; set; } = true;
}

/// <summary>An AppTrust application: a named deliverable whose supply chain we monitor.</summary>
public class AppRecord
{
    public string Key { get; set; } = "";               // e.g. "pizza-checkout"
    public string Name { get; set; } = "";              // e.g. "Pizza Checkout"
    public string Project { get; set; } = "";
    public string Criticality { get; set; } = "Medium"; // High | Medium | Low
    public string Type { get; set; } = "library";
    public string Team { get; set; } = "";
    public string Owners { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Packages { get; set; } = new(); // bound packages "npm:express" → CVE monitoring
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One approved model on the AI Catalog registry (control SEC-AIML-02).</summary>
public class AllowedModel
{
    public string Id { get; set; } = "";            // HuggingFace model id, e.g. "meta-llama/Llama-3.1-8B"
    public string License { get; set; } = "";       // license at approval time (drift is re-checked live)
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Notes { get; set; } = "";
}

/// <summary>AI assistant + research-agent config (Groq, OpenAI-compatible). Key stored server-side.</summary>
public class AiSettings
{
    public bool AssistantEnabled { get; set; } = true;             // show + serve the "Ask AI" assistant
    public string Provider { get; set; } = "groq";                  // groq (OpenAI-compatible)
    public string Model { get; set; } = "openai/gpt-oss-120b";
    public string? ApiKey { get; set; }                              // entered via admin UI; blank => use env GROQ_API_KEY
    public string Endpoint { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
}

// ─────────────────────────── Admin Center (global platform config) ───────────────────────────

/// <summary>One named AI agent the operator configures in the Admin Center. Multiple can exist;
/// mutation/evolution tasks are routed to them by name (see TaskRouting). Credentials are entered
/// in the admin UI and stored in the signed policy (self-hosted) — never logged, masked on read.</summary>
public class AiAgent
{
    public string Id { get; set; } = "";                  // stable key, e.g. "claude-cursor", "groq-exec"
    public string Name { get; set; } = "";                // display name
    // Standard the agent speaks to. Lets the operator "choose any AI":
    //   anthropic     — Anthropic Messages API
    //   openai        — OpenAI-compatible /v1 (covers Groq, OpenAI, on-prem gpt-oss, vLLM, etc.)
    //   cursor-cli    — drive Claude inside the Cursor CLI (uses Cursor user creds, not an API key)
    //   claude-cli    — the local Claude Code CLI (the mutation worker's default)
    public string Standard { get; set; } = "openai";
    public string Model { get; set; } = "";               // e.g. claude-opus-4-6, openai/gpt-oss-120b, gpt-oss-20b
    public string? Endpoint { get; set; }                 // base URL for openai-standard providers
    public string? ApiKey { get; set; }                   // masked on read; blank => use env
    public string? CursorUser { get; set; }               // cursor-cli: the Cursor account/user detail
    // A persona / system prompt injected whenever this agent runs — its personality + strict
    // instructions (e.g. "You are a meticulous .NET security reviewer; never weaken a control…").
    public string? Persona { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>Which configured agent handles each phase of a cycle. Empty => fall back to the default
/// worker engine. This is the "different tasks to different AI agents" routing.</summary>
public class TaskRouting
{
    public string? Research { get; set; }     // investigate existing code (e.g. claude-cursor / Opus)
    public string? Planning { get; set; }     // plan the fix
    public string? Execution { get; set; }    // implement (e.g. groq gpt-oss-120b)
    public string? Documentation { get; set; }// PR text, journal, close-out (e.g. gpt-oss-20b)
    // How the phases run: agents can work one-after-another or fan out. "sequential" runs each phase
    // in order; "parallel" lets independent phases (e.g. research + planning) run concurrently, then
    // converge before execution. The worker honours this when dispatching to the routed agents.
    public string Mode { get; set; } = "sequential";   // sequential | parallel
}

/// <summary>Global platform settings surfaced in the Administration view.</summary>
public class AdminSettings
{
    public List<AiAgent> Agents { get; set; } = new();    // the AI agents the operator can pick from
    public TaskRouting MutationRouting { get; set; } = new();
    public TaskRouting EvolutionRouting { get; set; } = new();
    // Memory budget the agents may use (MB); 0 => engine default.
    public int MemoryMb { get; set; } = 0;
    // Container/runtime the platform deploys + creates temp test environments on.
    public string Runtime { get; set; } = "docker";       // docker | podman | none
    // Database backing the platform (matches docker-compose default).
    public string Database { get; set; } = "sqlserver";   // sqlserver | postgres | sqlite
}

/// <summary>Admin config for a built-in source type: its credential/endpoint and enabled state.</summary>
public class SourceConfig
{
    public string Key { get; set; } = "";        // osv / kev / epss / vulncheck / socket / artifactory
    public string? Endpoint { get; set; }          // override base URL (e.g. on-prem OSV mirror)
    public string? Credential { get; set; }        // API key / token, entered via admin UI (self-hosted)
    public bool Enabled { get; set; } = true;
}

/// <summary>An admin-added custom OSV-format feed (real add/remove). Queried as an extra source.</summary>
public class CustomSource
{
    public string Id { get; set; } = "";           // stable id (slug)
    public string Label { get; set; } = "";
    public string OsvQueryUrl { get; set; } = "";   // POST {package,version} → OSV query response, e.g. https://mirror/v1/query
    public string? Credential { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Required { get; set; } = false;
}

/// <summary>A named binding of a rule-set to a resource scope. Mirrors JFrog's Watch concept.
/// The rule-set itself is the named "policy" (PolicyName/PolicyType), matching JFrog's
/// watch→policy organization where a watch applies one or more policies to a scope.</summary>
public class Watch
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<Ecosystem> Ecosystems { get; set; } = new(); // empty = all ecosystems
    public bool Enabled { get; set; } = true;
    public string PolicyName { get; set; } = "";              // named rule-set (the "policy")
    public string PolicyType { get; set; } = "Security";      // Security | License | Operational Risk
    public List<WatchRule> Rules { get; set; } = new();
}

/// <summary>One rule inside a watch: a condition (type + threshold) plus actions (block/notify).</summary>
public class WatchRule
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "CVEs";        // CVEs / Malicious / License
    public string MinSeverity { get; set; } = "High"; // None/Low/Medium/High/Critical (for CVEs)
    public bool KnownExploitedOnly { get; set; }       // CVEs: only act on KEV-listed
    public bool Block { get; set; } = true;            // action: block the artifact
    public bool Notify { get; set; } = true;           // action: emit notification/violation
}

/// <summary>A git repository explicitly linked for observation (control: SEC-SRC-01).
/// Stored in the signed policy so every add/remove is versioned and auditable.
/// Replaces the previous auto-listing of all repos for a GitHub owner.</summary>
public class LinkedGitRepo
{
    public string FullName { get; set; } = "";          // e.g. "myorg/payments-api"
    public string Url { get; set; } = "";               // HTTPS URL, e.g. "https://github.com/myorg/payments-api"
    public string DefaultBranch { get; set; } = "main";
    public string Visibility { get; set; } = "private";
    public string? Language { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class WeightsPolicy
{
    public bool SafetensorsOnly { get; set; } = true;   // block .bin / pickle
    public bool BlockPickle { get; set; } = true;
    public bool RequireHashPin { get; set; } = true;
}

public class PolicyException
{
    public string Package { get; set; } = "";           // "torch", "torch==2.4.0", or "*"
    public Ecosystem? Ecosystem { get; set; }
    public string Reason { get; set; } = "";
    public string ApprovedBy { get; set; } = "";
    public string Ticket { get; set; } = "";
    public DateTimeOffset Expires { get; set; }

    public bool Matches(PackageRef pkg)
    {
        if (Ecosystem is not null && Ecosystem != pkg.Ecosystem) return false;
        if (Expires < DateTimeOffset.UtcNow) return false;
        if (Package == "*") return true;
        var spec = Package.Split("==", 2);
        if (!spec[0].Equals(pkg.Name, StringComparison.OrdinalIgnoreCase)) return false;
        return spec.Length == 1 || spec[1] == pkg.Version;
    }
}
