using Microsoft.Extensions.Configuration;
using Advisory.Api.Models;
using Advisory.Api.Scan;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the content-scan dimension: embedded-secret detection and IaC misconfiguration
/// detection. These run on artifact text when bytes are available (e.g. promotion bridge).
/// </summary>
public class ContentScanTests
{
    [Fact]
    public void SecretScanner_flags_aws_key_and_private_key()
    {
        var s = new SecretScanner();
        var hits = s.Scan("config = { key = 'AKIAIOSFODNN7EXAMPLE' }\n-----BEGIN RSA PRIVATE KEY-----");
        Assert.Contains(hits, h => h.Rule == "AWS_ACCESS_KEY");
        Assert.Contains(hits, h => h.Rule == "PRIVATE_KEY");
    }

    [Fact]
    public void SecretScanner_clean_content_is_empty()
    {
        var s = new SecretScanner();
        Assert.Empty(s.Scan("export const greeting = 'hello world';"));
    }

    [Fact]
    public void IacScanner_flags_open_ingress_and_public_s3()
    {
        var iac = new IacScanner();
        var tf = "resource \"aws_security_group\" {\n cidr_blocks = [\"0.0.0.0/0\"]\n}\n acl = \"public-read\"";
        var hits = iac.Scan(tf);
        Assert.Contains(hits, h => h.Rule == "IAC_PUBLIC_INGRESS");
        Assert.Contains(hits, h => h.Rule == "IAC_S3_PUBLIC");
    }

    [Fact]
    public void IacScanner_flags_privileged_container()
    {
        var iac = new IacScanner();
        Assert.Contains(iac.Scan("securityContext:\n  privileged: true"), h => h.Rule == "IAC_PRIVILEGED_CONTAINER");
    }

    [Fact]
    public void IacScanner_clean_yaml_is_empty()
    {
        var iac = new IacScanner();
        Assert.Empty(iac.Scan("apiVersion: v1\nkind: ConfigMap\ndata:\n  level: info"));
    }

    // --- Contextual analysis (reachability): runs the real Node analyzer if node is on PATH. ---

    private static bool NodeAvailable()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("node", "--version")
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi);
            p!.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string AnalyzerPath()
    {
        // tools/reachability/analyze.mjs relative to repo root (tests run from bin/.../net10.0).
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "tools", "reachability", "analyze.mjs");
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return "";
    }

    private static ReachabilityAnalyzer MakeAnalyzer(string analyzerPath)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["REACHABILITY_ANALYZER"] = analyzerPath,
            ["NODE_PATH"] = "node",
        }).Build();
        return new ReachabilityAnalyzer(cfg);
    }

    [SkippableFact]
    public async Task Reachability_reachable_when_symbol_used_notreachable_when_not()
    {
        Skip.IfNot(NodeAvailable(), "node not available");
        var analyzer = AnalyzerPath();
        Skip.If(string.IsNullOrEmpty(analyzer) || !Directory.Exists(Path.Combine(Path.GetDirectoryName(analyzer)!, "node_modules", "acorn")),
            "analyzer/acorn not installed");

        var proj = Path.Combine(Path.GetTempPath(), "pkgfw-reach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(proj, "src"));
        await File.WriteAllTextAsync(Path.Combine(proj, "src", "app.js"),
            "import _ from 'lodash';\nexport const x = _.merge({}, { a: 1 });\n");

        var a = MakeAnalyzer(analyzer);
        var targets = new[]
        {
            new ReachabilityAnalyzer.Target("used", "lodash", new[] { "merge" }),
            new ReachabilityAnalyzer.Target("unused", "lodash", new[] { "template" }),
            new ReachabilityAnalyzer.Target("absent", "minimist", new[] { "parse" }),
        };
        var res = await a.AnalyzeAsync(proj, targets, default);
        Directory.Delete(proj, true);

        Assert.Equal("Reachable", res["used"].Reachability);
        Assert.Equal("NotReachable", res["unused"].Reachability);
        Assert.Equal("NotReachable", res["absent"].Reachability);
    }
}
