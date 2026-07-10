using System.Text.RegularExpressions;
using Advisory.Api.Models;

namespace Advisory.Api.Nexus;

/// <summary>
/// Parses lines of Nexus's inbound request.log (auto-gate-on-pull). A developer's pip/npm install of a
/// not-yet-approved package produces a 404 line for its <c>&lt;eco&gt;-approved</c> repo; we extract the
/// ecosystem + package name so the tailer can enqueue it for gating. Only APPROVED-repo 404s count —
/// quarantine reads and internal REST calls are the firewall's own traffic, not a developer request.
///
/// The log format is Apache-style (verified on Nexus 3.93 Community):
///   IP - user [ts] "GET /repository/&lt;repo&gt;/&lt;path&gt; HTTP/1.1" &lt;status&gt; ...
/// so we key off the request line (method + path) and the status code. Both the pip index request
/// (/simple/&lt;name&gt;/) and the wheel request (/packages/&lt;name&gt;/...) normalise to the same package
/// name so the tailer's dedup collapses them into one enqueue.
/// </summary>
public static class RequestLogParser
{
    // IP - user [ts] "GET /repository/<repo>/<rest> HTTP/1.1" <status>
    private static readonly Regex Line = new(
        @"^\S+\s+\S+\s+(?<user>\S+)\s+\[[^\]]*\]\s+""(?<method>GET|HEAD)\s+/repository/(?<repo>[^/]+)/(?<rest>\S*)\s+HTTP/[\d.]+""\s+(?<status>\d{3})",
        RegexOptions.Compiled);

    /// <summary>True when the line is a 404 for a developer-facing approved repo and we can extract the
    /// package. <paramref name="pkg"/> carries the ecosystem + name (version left empty — the tailer only
    /// needs coordinates; the fetch step resolves the concrete version, as the manual flow already does).</summary>
    public static bool TryParseMiss(string? line, out PackageRef? pkg)
        => TryParseMiss(line, out pkg, out _);

    /// <summary>As <see cref="TryParseMiss(string?, out PackageRef?)"/>, also returning the requesting
    /// user from the log line ("-" for anonymous) so requests can be attributed to a developer.</summary>
    public static bool TryParseMiss(string? line, out PackageRef? pkg, out string? user)
    {
        pkg = null; user = null;
        if (string.IsNullOrEmpty(line)) return false;

        var m = Line.Match(line);
        if (!m.Success) return false;
        if (m.Groups["status"].Value != "404") return false;

        var repo = m.Groups["repo"].Value;
        // Only gate developer misses on the APPROVED repo. Reuse the single prefix map (ADR 0001).
        if (!NexusEcosystems.TryFromRepoName(repo, out var eco)) return false;
        if (!repo.EndsWith("-approved", StringComparison.OrdinalIgnoreCase)) return false;

        var name = ExtractName(eco, m.Groups["rest"].Value);
        if (string.IsNullOrWhiteSpace(name)) return false;

        var u = m.Groups["user"].Value;
        user = (u == "-" || string.IsNullOrWhiteSpace(u)) ? null : u;
        pkg = new PackageRef(eco, name, "");
        return true;
    }

    /// <summary>Pull the package name out of the repo-relative path, per ecosystem. Mirrors the URL
    /// shapes NexusClient builds for each registry.</summary>
    private static string? ExtractName(Ecosystem eco, string rest)
    {
        // Strip any query string.
        var q = rest.IndexOf('?');
        if (q >= 0) rest = rest[..q];

        return eco switch
        {
            // pip: index "simple/<name>/" OR wheel "packages/<name>/<ver>/<file>" — both → <name>.
            Ecosystem.PyPI => PyName(rest),
            // npm: metadata "<name>" or scoped "@org%2fpkg"; tarball "<name>/-/<file>.tgz" or
            // scoped "@org/pkg/-/<file>.tgz". All → the package name (scoped incl. the @org/ part).
            Ecosystem.npm => NpmName(rest),
            _ => null, // other ecosystems added in their own slices (NuGet/Cargo/Go next).
        };
    }

    private static string? PyName(string rest)
    {
        // simple/<name>/...  or  packages/<name>/...
        var segs = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length >= 2 && (segs[0] == "simple" || segs[0] == "packages"))
            return Uri.UnescapeDataString(segs[1]);
        return null;
    }

    private static string? NpmName(string rest)
    {
        if (string.IsNullOrEmpty(rest)) return null;
        // Decode first so scoped metadata "@org%2fpkg" becomes "@org/pkg".
        var path = Uri.UnescapeDataString(rest);
        // Tarball form ".../-/<file>.tgz" — the name is everything before "/-/".
        var dash = path.IndexOf("/-/", StringComparison.Ordinal);
        if (dash >= 0) path = path[..dash];
        // Now: "name"  or  "@org/pkg". Scoped keeps two segments; unscoped is one.
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0) return null;
        if (segs[0].StartsWith('@'))
            return segs.Length >= 2 ? $"{segs[0]}/{segs[1]}" : null;   // @org/pkg
        return segs[0];
    }
}
