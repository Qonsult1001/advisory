using PkgFirewall.Api.Llm;
using PkgFirewall.Api.Research;
using Xunit;

namespace PkgFirewall.Tests;

/// <summary>Stub Groq client: not configured, so the AI DLP pass is skipped and tests exercise
/// the deterministic regex+Luhn layer in isolation.</summary>
file sealed class OfflineGroq : IGroqClient
{
    public bool IsConfigured => false;
    public string Model => "stub";
    public Task<(bool Ok, string Text)> ChatAsync(string s, string u, int m, double t, CancellationToken ct) => Task.FromResult((false, ""));
    public Task<(bool Ok, string Detail)> TestAsync(string a, string m, string e, CancellationToken ct) => Task.FromResult((false, ""));
}

/// <summary>Stub Privacy Filter: not configured, so the deterministic regex/Luhn layer is tested alone.</summary>
file sealed class OfflinePf : IPrivacyFilter
{
    public bool Configured => false;
    public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(false);
    public Task<string> StateAsync(CancellationToken ct) => Task.FromResult("down");
    public Task<PfResult> RedactAsync(string text, CancellationToken ct) => Task.FromResult(new PfResult(false, new(), null, "stub"));
}

/// <summary>
/// The DLP inspector is what stops PII/cards/secrets/code leaving via an LLM prompt. These tests
/// pin detection (true positives), Luhn-gating (no false positives on random digit runs), and the
/// scan-vs-block distinction.
/// </summary>
public class DlpInspectorTests
{
    private static DlpSettings All(bool block) => new()
    {
        ScanPii = true, ScanCards = true, ScanSecrets = true, ScanCode = true,
        BlockPii = block, BlockCards = block, BlockSecrets = block, BlockCode = block,
    };

    private static string Body(string content)
        => $$"""{"model":"gpt-4o","messages":[{"role":"user","content":{{System.Text.Json.JsonSerializer.Serialize(content)}}}]}""";

    [Fact]
    public async System.Threading.Tasks.Task Detects_valid_credit_card_and_redacts_it()
    {
        var r = await new DlpInspector(new OfflineGroq(), new OfflinePf()).InspectAsync(Body("charge card 4111 1111 1111 1111 now"), All(true));
        Assert.Contains(r.Findings, f => f.Category == DlpInspector.CARD);
        Assert.True(r.Block);
        Assert.DoesNotContain("4111111111111111", r.RedactedPreview);
        Assert.Contains("CREDIT_CARD:REDACTED", r.RedactedPreview);
    }

    [Fact]
    public async System.Threading.Tasks.Task Random_16_digit_order_id_is_not_flagged_as_card()
    {
        // 1234567890123456 fails Luhn → must not be a card finding.
        var r = await new DlpInspector(new OfflineGroq(), new OfflinePf()).InspectAsync(Body("order ref 1234567890123456 shipped"), All(true));
        Assert.DoesNotContain(r.Findings, f => f.Category == DlpInspector.CARD);
    }

    [Fact]
    public async System.Threading.Tasks.Task Detects_email_and_aws_secret()
    {
        var r = await new DlpInspector(new OfflineGroq(), new OfflinePf()).InspectAsync(Body("email jane@bank.co.za key AKIAIOSFODNN7EXAMPLE"), All(true));
        Assert.Contains(r.Findings, f => f.Rule == "EMAIL");
        Assert.Contains(r.Findings, f => f.Rule == "AWS_ACCESS_KEY");
        Assert.True(r.Block);
    }

    [Fact]
    public async System.Threading.Tasks.Task Scan_without_block_records_but_allows()
    {
        var r = await new DlpInspector(new OfflineGroq(), new OfflinePf()).InspectAsync(Body("contact jane@bank.co.za"), All(false));
        Assert.Contains(r.Findings, f => f.Category == DlpInspector.PII);
        Assert.False(r.Block);            // scan-only: detected, not blocked
    }

    [Fact]
    public async System.Threading.Tasks.Task Detects_proprietary_code_block()
    {
        var code = "import os\nfrom risk import model\ndef score(tx):\n    return model.predict(tx)\nclass Engine:\n    def run(self):\n        pass\nimport sys\nexport const X = 1";
        var r = await new DlpInspector(new OfflineGroq(), new OfflinePf()).InspectAsync(Body(code), All(false));
        Assert.Contains(r.Findings, f => f.Category == DlpInspector.CODE);
    }

    [Fact]
    public async System.Threading.Tasks.Task Clean_prompt_has_no_findings()
    {
        var r = await new DlpInspector(new OfflineGroq(), new OfflinePf()).InspectAsync(Body("Summarize the quarterly earnings in two sentences."), All(true));
        Assert.Empty(r.Findings);
        Assert.False(r.Block);
    }

    [Fact]
    public async System.Threading.Tasks.Task Disabled_category_is_not_scanned()
    {
        var cfg = new DlpSettings { ScanPii = false, ScanCards = true, ScanSecrets = true, ScanCode = true };
        var r = await new DlpInspector(new OfflineGroq(), new OfflinePf()).InspectAsync(Body("email jane@bank.co.za"), cfg);
        Assert.DoesNotContain(r.Findings, f => f.Category == DlpInspector.PII);
    }
}
