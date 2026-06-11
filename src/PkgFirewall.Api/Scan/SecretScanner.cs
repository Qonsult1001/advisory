using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PkgFirewall.Api.Models;
using PkgFirewall.Api.Resolve;

namespace PkgFirewall.Api.Scan;

/// <summary>Regex-based secret detection inside artifact text content.</summary>
public class SecretScanner
{
    private static readonly (string Rule, Regex Re)[] Patterns =
    {
        ("AWS_ACCESS_KEY", new(@"AKIA[0-9A-Z]{16}")),
        ("PRIVATE_KEY",    new(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----")),
        ("GENERIC_TOKEN",  new(@"(?i)(api[_-]?key|secret|token)\s*[:=]\s*['""][0-9a-zA-Z\-_]{16,}['""]")),
        ("SLACK_TOKEN",    new(@"xox[baprs]-[0-9A-Za-z\-]{10,}")),
        ("JWT",            new(@"eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}")),
        ("GITHUB_TOKEN",   new(@"gh[pousr]_[0-9A-Za-z]{36,}")),
        ("GCP_KEY",        new(@"AIza[0-9A-Za-z\-_]{35}")),
    };

    public IReadOnlyList<ScanFinding> Scan(string content)
    {
        var f = new List<ScanFinding>();
        foreach (var (rule, re) in Patterns)
            if (re.IsMatch(content))
                f.Add(new ScanFinding(rule, Severity.High, "embedded secret detected in artifact"));
        return f;
    }
}

/// <summary>
/// Scans Infrastructure-as-Code text (Terraform, Kubernetes/YAML, Dockerfile) shipped inside a
/// package for common high-risk misconfigurations. Regex/heuristic — a free, no-dependency
/// equivalent of JFrog Advanced Security's IaC scan. Conservative: only flags strong signals to
/// keep false positives low. Returns an empty list when the content carries no IaC.
/// </summary>
public class IacScanner
{
    private static readonly (string Rule, Severity Sev, Regex Re, string Detail)[] Rules =
    {
        ("IAC_PUBLIC_INGRESS", Severity.High,
            new(@"(?i)cidr_blocks?\s*=\s*\[?\s*['""]0\.0\.0\.0/0['""]"),
            "security group / ingress open to 0.0.0.0/0 (the whole internet)"),
        ("IAC_S3_PUBLIC", Severity.High,
            new(@"(?i)acl\s*=\s*['""]public-read(-write)?['""]"),
            "S3 bucket ACL set to public-read/-write"),
        ("IAC_PLAINTEXT_SECRET", Severity.High,
            new(@"(?i)(password|secret|access[_-]?key)\s*[:=]\s*['""][^'""\n]{6,}['""]"),
            "hard-coded credential in IaC definition"),
        ("IAC_PRIVILEGED_CONTAINER", Severity.High,
            new(@"(?i)privileged:\s*true"),
            "Kubernetes/container running in privileged mode"),
        ("IAC_HOST_NETWORK", Severity.Medium,
            new(@"(?i)hostNetwork:\s*true"),
            "pod uses host network namespace"),
        ("IAC_DOCKER_ROOT", Severity.Low,
            new(@"(?im)^\s*USER\s+root\s*$"),
            "Dockerfile sets USER root"),
        ("IAC_NO_TLS", Severity.Medium,
            new(@"(?i)(disable[_-]?ssl|insecure[_-]?skip[_-]?verify)\s*[:=]\s*true"),
            "TLS verification disabled"),
    };

    public IReadOnlyList<ScanFinding> Scan(string content)
    {
        var f = new List<ScanFinding>();
        foreach (var (rule, sev, re, detail) in Rules)
            if (re.IsMatch(content))
                f.Add(new ScanFinding(rule, sev, detail));
        return f;
    }
}

/// <summary>Generates a CycloneDX SBOM from the resolved dependency tree.</summary>
public static class SbomGenerator
{
    public static string CycloneDx(PackageRef root, IReadOnlyList<DepNode> tree)
    {
        var components = tree.Select(n => new
        {
            type = "library",
            name = n.Package.Name,
            version = n.Package.Version,
            purl = Purl(n.Package),
            properties = new[] { new { name = "depth", value = n.Depth.ToString() } }
        });

        var doc = new
        {
            bomFormat = "CycloneDX",
            specVersion = "1.5",
            version = 1,
            metadata = new
            {
                timestamp = DateTimeOffset.UtcNow.ToString("o"),
                component = new { type = "application", name = root.Name, version = root.Version }
            },
            components
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Purl(PackageRef p)
    {
        var type = p.Ecosystem switch
        {
            Ecosystem.PyPI => "pypi", Ecosystem.npm => "npm", Ecosystem.NuGet => "nuget",
            Ecosystem.Cargo => "cargo", Ecosystem.Go => "golang", _ => "generic"
        };
        return $"pkg:{type}/{p.Name}@{p.Version}";
    }
}
