using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Advisory.Api.Research;

namespace Advisory.Api.Llm;

/// <summary>One DLP detection inside an outbound prompt. Method = "regex" | "luhn" | "ai".</summary>
public record DlpFinding(string Category, string Rule, string Severity, int Count, string Sample, string Method = "regex");

/// <summary>Result of inspecting an outbound LLM request: findings + original & redacted previews.</summary>
// RedactedPreview/OriginalPreview are 2000-char samples for the AUDIT LOG. RedactedBody is the FULL
// request text with every detected PII/card/secret/custom span replaced — this is what the gateway can
// forward upstream in "redact" mode so sensitive data (POPIA/PCI) is stripped BEFORE it reaches the AI
// provider, rather than blocking the whole call.
public record DlpResult(List<DlpFinding> Findings, string RedactedPreview, string OriginalPreview, bool Block, string? BlockReason, string RedactedBody);

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

    // Payment-card context: words that make nearby digits cardholder data even if Luhn fails.
    private static readonly Regex CardContext = new(
        @"(?i)\b(credit[\s\-]?card|debit[\s\-]?card|card[\s\-]?(number|no|num|#)|pan\b|cvv|cvc|cvv2|card[\s\-]?verification|expiry|exp[\s\-]?date)\b", RegexOptions.Compiled);
    private static readonly Regex Cvv = new(@"(?i)\b(cvv|cvc|cvv2|cvc2|security code)\b\s*[:#]?\s*\d{3,4}\b", RegexOptions.Compiled);

    // Proprietary source-code signals: licence/confidentiality headers, or a dense run of code tokens.
    private static readonly Regex ConfidentialHeader = new(
        @"(?i)(confidential|proprietary|all rights reserved|internal use only|copyright\s+\(c\))", RegexOptions.Compiled);
    private static readonly Regex CodeSignal = new(
        @"(?m)^\s*(import |from |package |using |public |private |func |def |class |#include|const |export |fn )", RegexOptions.Compiled);

    public async Task<DlpResult> InspectAsync(string body, DlpSettings cfg, CancellationToken ct = default)
    {
        var text = ExtractText(body);
        var findings = new List<DlpFinding>();

        // Payment cards FIRST (before the noisy PHONE pattern, so card digits aren't mis-tagged).
        // Two detection paths:
        //   • Luhn-valid 13-19 digit group → a real card number, regardless of context.
        //   • CONTEXT: the prompt mentions "credit card"/"card number"/"cvv"/"cvc" AND contains a
        //     13-19 digit group → treat as a card even if Luhn fails. People paste test/fat-fingered
        //     numbers next to the words "credit card" and "cvv"; a compliance gate must still block
        //     that. This closes the gap where a Luhn-invalid number labelled a credit card slipped through.
        var cardSet = new HashSet<string>();
        if (cfg.CategoryEnabled(CARD))
        {
            var candidates = CardCandidates(text).ToList();
            var luhnCards = candidates.Where(LuhnValid).ToList();
            var cardContext = CardContext.IsMatch(text);
            // When context says "card/cvv", every candidate digit-run is suspect; else only Luhn-valid ones.
            var cards = cardContext ? candidates : luhnCards;
            foreach (var c in cards) cardSet.Add(c);
            if (cards.Count > 0)
            {
                var method = luhnCards.Count > 0 ? "luhn" : "context";
                var detail = luhnCards.Count > 0 ? Mask(luhnCards[0])
                    : Mask(cards[0]) + " (near 'card'/'cvv' — Luhn-invalid but contextually a card)";
                findings.Add(new DlpFinding(CARD, "CREDIT_CARD", "High", cards.Count, detail, method));
            }
            // A bare CVV/CVC mention is itself cardholder data (PCI-DSS) — flag it.
            if (cardContext && Cvv.IsMatch(text))
                findings.Add(new DlpFinding(CARD, "CVV", "High", 1, "card security code present", "context"));
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
        var pfSpans = new List<(string Sample, string Rule)>();  // exact strings PF flagged, for redaction
        if (cfg.UsePrivacyFilter && cfg.ScanPii && _pf.Configured)
        {
            try
            {
                var pf = await _pf.RedactAsync(text, ct);
                if (pf.Ok)
                {
                    pfUsed = true;
                    foreach (var e in pf.Entities)
                        if (!string.IsNullOrWhiteSpace(e.Sample)) pfSpans.Add((e.Sample, e.Rule));
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

        // ---- Custom admin-defined rules (org-specific patterns) ----
        var customBlocking = new List<string>();
        foreach (var (name, pattern, ruleBlocks) in cfg.CustomRules)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            try
            {
                var re = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
                var matches = re.Matches(text);
                if (matches.Count == 0) continue;
                findings.Add(new DlpFinding("Custom", name, ruleBlocks ? "High" : "Medium", matches.Count, Mask(matches[0].Value), "custom"));
                if (ruleBlocks) customBlocking.Add(name);
            }
            catch { /* a bad/timeout regex is ignored, never crashes the gateway */ }
        }

        // Block when any ENABLED built-in category, or a blocking custom rule, has a finding.
        var blocking = findings.Where(f => f.Category != "Custom" && cfg.CategoryBlocks(f.Category)).ToList();
        var block = blocking.Count > 0 || customBlocking.Count > 0;
        var parts = blocking.GroupBy(b => b.Category).Select(g => $"{g.Key} ({string.Join(",", g.Select(x => x.Rule).Distinct())})").ToList();
        if (customBlocking.Count > 0) parts.Add($"Custom ({string.Join(",", customBlocking.Distinct())})");
        var reason = block ? string.Join("; ", parts) : null;

        // The transcript preview should show what the user ACTUALLY TYPED — the last user turn — not the
        // whole prepended context. Claude Code / Cursor front-load a large <system-reminder> / session-start
        // block as the first message, so showing all-messages made every record's "original" look identical
        // (the boilerplate) instead of the real prompt. Detection still runs over the full `text` above;
        // only the human-readable preview is scoped to the last user message.
        var previewSrc = LastUserText(body) ?? text;
        var original = previewSrc.Length > 2000 ? previewSrc[..2000] + "…" : previewSrc;
        var redactedPreview = Redact(previewSrc, findings, pfSpans, cfg.CustomRules, cap: true);
        // The FORWARDED body must stay valid JSON. Redacting the raw serialized text can splice a
        // [CAT:REDACTED] token across a structural quote/brace and corrupt the JSON (Anthropic then
        // 400s: "not valid JSON at char 0"). So when the body parses as JSON, redact only inside
        // STRING VALUES and re-serialize — structure is preserved. Non-JSON bodies fall back to the
        // flat text redaction.
        // Build the FORWARDED body from the ORIGINAL request JSON (`body`), not the extracted `text` —
        // `text` is only the message content pulled out by ExtractText for detection. Redacting `body`
        // JSON-aware keeps the {model, messages, ...} envelope intact so the upstream still gets valid
        // JSON (redacting `text` would forward the bare message string → Anthropic 400 "invalid JSON").
        var redactedBody = RedactJsonAware(body, findings, pfSpans, cfg.CustomRules);
        return new DlpResult(findings, redactedPreview, original, block, reason, redactedBody);
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
    /// <summary>The text of the LAST user message — what the human actually typed this turn — for the
    /// transcript preview. Tools like Claude Code / Cursor prepend a large session-start / system-reminder
    /// block as earlier messages; showing those made every audit row's "original" identical. Returns null
    /// if the body isn't a chat request or has no user turn, so the caller can fall back to the full text.</summary>
    private static string? LastUserText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("messages", out var msgs) || msgs.ValueKind != JsonValueKind.Array)
                return null;
            string? last = null;
            foreach (var m in msgs.EnumerateArray())
            {
                if (!m.TryGetProperty("role", out var role) || role.GetString() != "user") continue;
                if (!m.TryGetProperty("content", out var c)) continue;
                if (c.ValueKind == JsonValueKind.String) { last = c.GetString(); continue; }
                if (c.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var part in c.EnumerateArray())
                        if (part.TryGetProperty("type", out var pt) && pt.GetString() == "text"
                            && part.TryGetProperty("text", out var t)) sb.AppendLine(t.GetString());
                    if (sb.Length > 0) last = sb.ToString().TrimEnd();
                }
            }
            return string.IsNullOrWhiteSpace(last) ? null : last;
        }
        catch { return null; }
    }

    private static string ExtractText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var sb = new StringBuilder();
            var root = doc.RootElement;
            // Scan ONLY the USER turns — what the human actually sent to the model. We deliberately do NOT
            // scan the `system` field or assistant turns: coding tools (Claude Code / Cursor) load a large
            // session-start / <system-reminder> / skill block there, and its incidental digit runs and text
            // trip card/ID/phone patterns → false-positive findings on innocent prompts like "whats your
            // name". The user's own data is in the user turns; that's the exfiltration surface that matters.
            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                foreach (var m in msgs.EnumerateArray())
                {
                    if (m.TryGetProperty("role", out var role) && role.GetString() != "user") continue; // user turns only
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
        // A PAN is 13-19 digits (ISO/IEC 7812). Reject anything outside that range so the checksum
        // can never bless an over-/under-length run as a card — defense-in-depth even though the
        // candidate regex ({13,19}) is the primary length guard. (#7)
        if (digits.Length is < 13 or > 19) return false;
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

    /// <summary>Redacted preview: replace EVERY detected value so the redacted view never leaks PII —
    /// regex/Luhn patterns, the AI Privacy Filter's exact spans (names, addresses, account numbers),
    /// payment cards, and CVV. This is the text the audit log stores as "what would leave".</summary>
    private static string Redact(string text, List<DlpFinding> findings, List<(string Sample, string Rule)>? pfSpans = null,
        List<(string Name, string Pattern, bool Block)>? customRules = null, bool cap = true)
    {
        // cap=true → a 2000-char sample for the audit preview. cap=false → the WHOLE text redacted, for the
        // body we actually forward upstream (so nothing sensitive is truncated-then-forwarded).
        var preview = cap && text.Length > 2000 ? text[..2000] + "…" : text;

        // 0) Custom admin rules — mask their matches too.
        if (customRules is { Count: > 0 })
            foreach (var (name, pattern, _) in customRules)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                try { preview = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)).Replace(preview, _ => $"[{name}:REDACTED]"); }
                catch { }
            }

        // 1) AI Privacy Filter spans first — names, addresses, account numbers it flagged in free text.
        //    Replace the exact strings (longest first so substrings don't break the match).
        if (pfSpans is { Count: > 0 })
            foreach (var (sample, rule) in pfSpans.Where(s => s.Sample.Length >= 2).OrderByDescending(s => s.Sample.Length))
                preview = preview.Replace(sample, $"[{rule}:REDACTED]");

        // 2) Cards (matching raw and spaced/dashed forms). With card context, redact every 13-19 digit
        //    run (Luhn-invalid included); else only Luhn-valid — mirrors detection so cards never leak.
        if (findings.Any(f => f.Category == CARD))
        {
            var ctx = CardContext.IsMatch(preview);
            preview = Regex.Replace(preview, @"(?<!\d)(?:\d[ \-]?){13,19}(?!\d)",
                m => (ctx || LuhnValid(m.Value)) ? "[CREDIT_CARD:REDACTED]" : m.Value);
            preview = Cvv.Replace(preview, m => Regex.Replace(m.Value, @"\d", "•"));
        }

        // 3) All regex patterns INCLUDING phone — a detected phone number must be masked in the preview.
        foreach (var (cat, rule, re, _) in Patterns)
            if (findings.Any(f => f.Rule == rule))
                preview = re.Replace(preview, _ => $"[{rule}:REDACTED]");
        return preview;
    }

    /// <summary>Redact PII while keeping the body valid JSON: parse the tree, apply the same span/
    /// pattern replacements to each STRING VALUE only, and re-serialize. If the body isn't JSON
    /// (or parsing/rebuilding fails), fall back to the flat full-text redaction so we never forward
    /// unredacted content — the failure mode is "over-redact / plain text", never "leak".</summary>
    private static string RedactJsonAware(string text, List<DlpFinding> findings,
        List<(string Sample, string Rule)>? pfSpans, List<(string Name, string Pattern, bool Block)>? customRules)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(text);
            if (node is null) return Redact(text, findings, pfSpans, customRules, cap: false);
            // Redact ONLY the fields that carry user prompt text — NOT tool schemas, model, metadata, etc.
            // A chat request (Anthropic/OpenAI) puts the prompt in `messages`; some completion/embedding
            // shapes use `prompt` or `input`. Redacting the whole tree corrupted `tools[].input_schema`
            // (a redaction inside a JSON-Schema string made it fail draft-2020-12 validation → 400). System
            // instructions can also carry PII, so `system` is redacted too. Everything else is left intact.
            if (node is System.Text.Json.Nodes.JsonObject root)
            {
                foreach (var field in new[] { "messages", "prompt", "input", "system" })
                    if (root[field] is { } sub)
                        RedactNode(sub, findings, pfSpans, customRules);
                return root.ToJsonString();
            }
            // Body isn't a JSON object (unexpected) — redact the whole node rather than forward raw.
            RedactNode(node, findings, pfSpans, customRules);
            return node.ToJsonString();
        }
        catch
        {
            // Not JSON, or the tree couldn't be rebuilt — redact the raw text instead of forwarding raw.
            return Redact(text, findings, pfSpans, customRules, cap: false);
        }
    }

    private static void RedactNode(System.Text.Json.Nodes.JsonNode node, List<DlpFinding> findings,
        List<(string Sample, string Rule)>? pfSpans, List<(string Name, string Pattern, bool Block)>? customRules)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
                // Copy keys first — we replace values in-place while iterating.
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[key];
                    if (child is System.Text.Json.Nodes.JsonValue v &&
                        v.TryGetValue<string>(out var s) && s is not null)
                        obj[key] = RedactString(s, findings, pfSpans, customRules);
                    else if (child is not null)
                        RedactNode(child, findings, pfSpans, customRules);
                }
                break;
            case System.Text.Json.Nodes.JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is System.Text.Json.Nodes.JsonValue v &&
                        v.TryGetValue<string>(out var s) && s is not null)
                        arr[i] = RedactString(s, findings, pfSpans, customRules);
                    else if (child is not null)
                        RedactNode(child, findings, pfSpans, customRules);
                }
                break;
        }
    }

    /// <summary>Apply the full redaction pass to a single JSON string value. Reuses Redact() with
    /// cap:false; the returned string is stored back as a JSON value, so System.Text.Json handles
    /// all escaping — the bracketed [CAT:REDACTED] token can never break the surrounding JSON.</summary>
    private static string RedactString(string value, List<DlpFinding> findings,
        List<(string Sample, string Rule)>? pfSpans, List<(string Name, string Pattern, bool Block)>? customRules)
        => Redact(value, findings, pfSpans, customRules, cap: false);
}

/// <summary>Per-category DLP configuration read from the LLM gateway policy.</summary>
public class DlpSettings
{
    public bool ScanPii, ScanCards, ScanSecrets, ScanCode;
    public bool BlockPii, BlockCards, BlockSecrets, BlockCode;
    public bool UseAi;             // Groq classifier fallback for free-text PII/code
    public bool UsePrivacyFilter;  // on-prem OpenAI Privacy Filter as the primary PII engine
    public List<(string Name, string Pattern, bool Block)> CustomRules = new();  // admin-defined patterns

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
