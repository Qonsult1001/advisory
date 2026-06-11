using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PkgFirewall.Api.Research;

namespace PkgFirewall.Api.Llm;

/// <summary>One DLP detection inside an outbound prompt. Method = "regex" | "luhn" | "ai".</summary>
public record DlpFinding(string Category, string Rule, string Severity, int Count, string Sample, string Method = "regex");

/// <summary>Result of inspecting an outbound LLM request: findings + original & redacted previews.</summary>
public record DlpResult(List<DlpFinding> Findings, string RedactedPreview, string OriginalPreview, bool Block, string? BlockReason);

/// <summary>
/// Data-loss-prevention inspector for outbound LLM traffic (the LiteLLM "guardrails" idea, but
/// for compliance). Detects PII (POPIA/GDPR: SA ID numbers, emails, phone numbers, IBAN),
/// payment-card numbers (validated with the Luhn checksum to cut false positives), credentials,
/// and proprietary source code. Returns per-category findings plus a redacted preview so the
/// gateway can SHOW what crossed the wire without storing the raw secret/PII.
/// </summary>
public class DlpInspector
{
    public const string PII = "PII";
    public const string CARD = "PaymentCard";
    public const string SECRET = "Secret";
    public const string CODE = "SourceCode";

    private readonly IGroqClient _groq;
    private readonly IPrivacyFilter _pf;
    public DlpInspector(IGroqClient groq, IPrivacyFilter pf) { _groq = groq; _pf = pf; }

    private static readonly (string Cat, string Rule, Regex Re, string Sev)[] Patterns =
    {
        // --- PII (POPIA / GDPR) ---
        ("PII", "EMAIL", new(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled), "Medium"),
        // South African ID: 13 digits YYMMDD SSSS C A Z — validated separately with Luhn below.
        ("PII", "SA_ID_NUMBER", new(@"\b\d{13}\b", RegexOptions.Compiled), "High"),
        ("PII", "PHONE", new(@"(?<!\d)(?:\+?\d{1,3}[ \-]?)?(?:\(?\d{2,4}\)?[ \-]?){2,4}\d{2,4}(?!\d)", RegexOptions.Compiled), "Low"),
        ("PII", "IBAN", new(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", RegexOptions.Compiled), "High"),
        ("PII", "SSN_US", new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled), "High"),
        // --- Secrets ---
        ("Secret", "AWS_ACCESS_KEY", new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled), "High"),
        ("Secret", "PRIVATE_KEY", new(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----", RegexOptions.Compiled), "High"),
        ("Secret", "GITHUB_TOKEN", new(@"gh[pousr]_[0-9A-Za-z]{36,}", RegexOptions.Compiled), "High"),
        ("Secret", "SLACK_TOKEN", new(@"xox[baprs]-[0-9A-Za-z\-]{10,}", RegexOptions.Compiled), "High"),
        ("Secret", "GENERIC_SECRET", new(@"(?i)(api[_-]?key|secret|password|token)\s*[:=]\s*['""][^'""\n]{12,}['""]", RegexOptions.Compiled), "High"),
        ("Secret", "BEARER", new(@"(?i)bearer\s+[A-Za-z0-9_\-\.=]{20,}", RegexOptions.Compiled), "Medium"),
    };

    // Proprietary source-code signals: licence/confidentiality headers, or a dense run of code tokens.
    private static readonly Regex ConfidentialHeader = new(
        @"(?i)(confidential|proprietary|all rights reserved|internal use only|copyright\s+\(c\))", RegexOptions.Compiled);
    private static readonly Regex CodeSignal = new(
        @"(?m)^\s*(import |from |package |using |public |private |func |def |class |#include|const |export |fn )", RegexOptions.Compiled);

    public async Task<DlpResult> InspectAsync(string body, DlpSettings cfg, CancellationToken ct = default)
    {
        var text = ExtractText(body);
        var findings = new List<DlpFinding>();

        // Payment cards FIRST: candidate digit groups validated by Luhn (kills false positives like
        // order ids). Done before the noisy PHONE pattern so card digits aren't mis-tagged as phones.
        var cardSet = new HashSet<string>();
        if (cfg.CategoryEnabled(CARD))
        {
            var cards = CardCandidates(text).Where(LuhnValid).ToList();
            foreach (var c in cards) cardSet.Add(c);
            if (cards.Count > 0)
                findings.Add(new DlpFinding(CARD, "CREDIT_CARD", "High", cards.Count, Mask(cards[0]), "luhn"));
        }

        foreach (var (cat, rule, re, sev) in Patterns)
        {
            if (!cfg.CategoryEnabled(cat)) continue;
            var matches = re.Matches(text);
            if (matches.Count == 0) continue;
            if (rule == "SA_ID_NUMBER" && !matches.Any(m => LuhnValid(m.Value))) continue; // 13-digit but not a valid SA ID
            // PHONE: ignore matches that are actually (part of) a detected card number.
            if (rule == "PHONE")
            {
                var real = matches.Where(m => !cardSet.Any(c => c.Contains(System.Text.RegularExpressions.Regex.Replace(m.Value, @"\D", "")))
                    && System.Text.RegularExpressions.Regex.Replace(m.Value, @"\D", "").Length is >= 7 and <= 15).ToList();
                if (real.Count == 0) continue;
                findings.Add(new DlpFinding(cat, rule, sev, real.Count, Mask(real[0].Value)));
                continue;
            }
            findings.Add(new DlpFinding(cat, rule, sev, matches.Count, Mask(matches[0].Value)));
        }

        // Proprietary source code: confidential header, or many code-structure lines.
        var codeSignalLines = 0;
        if (cfg.CategoryEnabled(CODE))
        {
            codeSignalLines = CodeSignal.Matches(text).Count;
            if (ConfidentialHeader.IsMatch(text))
                findings.Add(new DlpFinding(CODE, "CONFIDENTIAL_HEADER", "High", 1, "confidential/proprietary marker"));
            else if (codeSignalLines >= 5)
                findings.Add(new DlpFinding(CODE, "PROPRIETARY_CODE", "Medium", codeSignalLines, $"{codeSignalLines} source-code structure lines"));
        }

        // ---- PII engine selection (free-text PII regex can't see: names, addresses, context) ----
        // PRIMARY: the on-prem OpenAI Privacy Filter model (token-classification, runs locally so the
        // text never leaves the network). FALLBACK: the Groq classifier. The deterministic regex/Luhn
        // layer above always runs regardless, so DLP is never fully off.
        var pfUsed = false;
        if (cfg.UsePrivacyFilter && cfg.ScanPii && _pf.Configured)
        {
            try
            {
                var pf = await _pf.RedactAsync(text, ct);
                if (pf.Ok)
                {
                    pfUsed = true;
                    foreach (var grp in pf.Entities.GroupBy(e => (e.Category, e.Rule)))
                    {
                        var cat = grp.Key.Category == "Secret" ? SECRET : grp.Key.Rule == "URL" || grp.Key.Rule == "DATE" ? PII : grp.Key.Category;
                        if (!cfg.CategoryEnabled(cat)) continue;
                        if (findings.Any(f => f.Category == cat && f.Rule == grp.Key.Rule)) continue;
                        var sev = grp.Key.Rule is "PERSON_NAME" or "ADDRESS" or "ACCOUNT_NUMBER" or "CREDENTIAL" ? "High" : "Medium";
                        findings.Add(new DlpFinding(cat, grp.Key.Rule, sev, grp.Count(), Mask(grp.First().Sample), "openai-privacy-filter"));
                    }
                }
            }
            catch { /* sidecar down/loading — fall through to Groq */ }
        }

        // Groq is the FALLBACK, not an always-on second pass — calling it on every request adds a
        // full network round-trip (~1s). Only invoke it when it can actually add something:
        //   • PII scanning is needed but the Privacy Filter model did NOT run (sidecar down), OR
        //   • CODE scanning is on AND the text shows a code hint the deterministic pass didn't already
        //     resolve (2–4 structure lines — the ambiguous zone). Pure prose with no code signal and a
        //     working PF → skip Groq entirely.
        var needGroqPii = cfg.ScanPii && !pfUsed;
        var needGroqCode = cfg.ScanCode && codeSignalLines is >= 2 and < 5 && !ConfidentialHeader.IsMatch(text);
        if (cfg.UseAi && _groq.IsConfigured && (needGroqPii || needGroqCode))
        {
            try
            {
                var aiFindings = await AiClassifyAsync(text, cfg, ct);
                foreach (var af in aiFindings)
                {
                    if (af.Category == PII && pfUsed) continue;   // PF already covered PII
                    if (af.Category == CODE && !needGroqCode) continue;
                    if (!findings.Any(f => f.Category == af.Category && f.Rule == af.Rule))
                        findings.Add(af);
                }
            }
            catch { /* AI is best-effort; deterministic layer already ran */ }
        }

        // Block when any ENABLED category that the policy treats as blocking has a finding.
        var blocking = findings.Where(f => cfg.CategoryBlocks(f.Category)).ToList();
        var block = blocking.Count > 0;
        var reason = block
            ? string.Join("; ", blocking.GroupBy(b => b.Category).Select(g => $"{g.Key} ({string.Join(",", g.Select(x => x.Rule).Distinct())})"))
            : null;

        var original = text.Length > 2000 ? text[..2000] + "…" : text;
        return new DlpResult(findings, Redact(text, findings), original, block, reason);
    }

    // ---- AI classifier ----

    private const string AiSystem =
        "You are a data-loss-prevention classifier for a bank. Given an outbound LLM prompt, find " +
        "sensitive content that simple regex would MISS: personal names tied to identity, physical/postal " +
        "addresses, health or financial details about a person, account/policy numbers in prose, and " +
        "proprietary/internal source code or business logic. Respond ONLY with compact JSON: " +
        "{\"findings\":[{\"category\":\"PII|SourceCode\",\"label\":\"short label\",\"severity\":\"High|Medium|Low\",\"sample\":\"<=40 char snippet\"}]}. " +
        "Empty array if nothing. Do not flag generic questions, public facts, or already-redacted [TOKENS].";

    private async Task<List<DlpFinding>> AiClassifyAsync(string text, DlpSettings cfg, CancellationToken ct)
    {
        var clip = text.Length > 6000 ? text[..6000] : text;
        var (ok, json) = await _groq.ChatAsync(AiSystem, $"PROMPT TO CLASSIFY:\n{clip}", 400, 0, ct);
        var outp = new List<DlpFinding>();
        if (!ok) return outp;
        try
        {
            var start = json.IndexOf('{'); var end = json.LastIndexOf('}');
            if (start < 0 || end <= start) return outp;
            using var doc = JsonDocument.Parse(json[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("findings", out var arr)) return outp;
            foreach (var f in arr.EnumerateArray())
            {
                var cat = f.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                if (cat == PII && !cfg.ScanPii) continue;
                if (cat == CODE && !cfg.ScanCode) continue;
                if (cat != PII && cat != CODE) continue;
                outp.Add(new DlpFinding(cat,
                    (f.TryGetProperty("label", out var l) ? l.GetString() : null)?.ToUpperInvariant().Replace(' ', '_') ?? "AI_DETECTED",
                    f.TryGetProperty("severity", out var s) ? s.GetString() ?? "Medium" : "Medium",
                    1, f.TryGetProperty("sample", out var sm) ? Mask(sm.GetString() ?? "") : "context match", "ai"));
            }
        }
        catch { /* malformed AI JSON — ignore */ }
        return outp;
    }

    // ---- helpers ----

    /// <summary>Pull human text out of an OpenAI/Anthropic request body (messages + system + tools).</summary>
    private static string ExtractText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var sb = new StringBuilder();
            var root = doc.RootElement;
            if (root.TryGetProperty("system", out var sys) && sys.ValueKind == JsonValueKind.String) sb.AppendLine(sys.GetString());
            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                foreach (var m in msgs.EnumerateArray())
                {
                    if (!m.TryGetProperty("content", out var c)) continue;
                    if (c.ValueKind == JsonValueKind.String) sb.AppendLine(c.GetString());
                    else if (c.ValueKind == JsonValueKind.Array)
                        foreach (var part in c.EnumerateArray())
                            if (part.TryGetProperty("text", out var t)) sb.AppendLine(t.GetString());
                }
            return sb.Length > 0 ? sb.ToString() : body;
        }
        catch { return body; }
    }

    private static IEnumerable<string> CardCandidates(string text)
        => Regex.Matches(text, @"(?<!\d)(?:\d[ \-]?){13,19}(?!\d)").Select(m => Regex.Replace(m.Value, @"[ \-]", ""))
                .Where(d => d.Length is >= 13 and <= 19);

    private static bool LuhnValid(string digits)
    {
        digits = Regex.Replace(digits, @"\D", "");
        if (digits.Length < 13) return false;
        int sum = 0; bool alt = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (alt) { d *= 2; if (d > 9) d -= 9; }
            sum += d; alt = !alt;
        }
        return sum % 10 == 0;
    }

    private static string Mask(string s)
    {
        if (s.Length <= 4) return new string('•', s.Length);
        return s[..2] + new string('•', Math.Min(8, s.Length - 4)) + s[^2..];
    }

    /// <summary>Redacted preview: replace every detected value so the UI shows context, never the secret.</summary>
    private static string Redact(string text, List<DlpFinding> findings)
    {
        var preview = text.Length > 2000 ? text[..2000] + "…" : text;
        // Cards first (before PHONE eats the digits), matching raw and spaced/dashed forms.
        if (findings.Any(f => f.Category == CARD))
            preview = Regex.Replace(preview, @"(?<!\d)(?:\d[ \-]?){13,19}(?!\d)",
                m => LuhnValid(m.Value) ? "[CREDIT_CARD:REDACTED]" : m.Value);
        foreach (var (cat, rule, re, _) in Patterns)
            if (findings.Any(f => f.Rule == rule) && rule != "PHONE")
                preview = re.Replace(preview, _ => $"[{rule}:REDACTED]");
        return preview;
    }
}

/// <summary>Per-category DLP configuration read from the LLM gateway policy.</summary>
public class DlpSettings
{
    public bool ScanPii, ScanCards, ScanSecrets, ScanCode;
    public bool BlockPii, BlockCards, BlockSecrets, BlockCode;
    public bool UseAi;             // Groq classifier fallback for free-text PII/code
    public bool UsePrivacyFilter;  // on-prem OpenAI Privacy Filter as the primary PII engine

    public bool CategoryEnabled(string cat) => cat switch
    {
        DlpInspector.PII => ScanPii, DlpInspector.CARD => ScanCards,
        DlpInspector.SECRET => ScanSecrets, DlpInspector.CODE => ScanCode, _ => false,
    };
    public bool CategoryBlocks(string cat) => cat switch
    {
        DlpInspector.PII => BlockPii, DlpInspector.CARD => BlockCards,
        DlpInspector.SECRET => BlockSecrets, DlpInspector.CODE => BlockCode, _ => false,
    };
}
