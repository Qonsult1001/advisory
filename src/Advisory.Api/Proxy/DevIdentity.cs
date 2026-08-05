using Microsoft.AspNetCore.Http;

namespace Advisory.Api.Proxy;

/// <summary>
/// Resolves the DEVELOPER behind a proxy request, for attribution on the exposure/recall ledger.
///
/// IT owns the link. IT issues each developer a unique opaque token and keeps the token→person map in
/// config (PROXY_DEV_TOKENS = "tok1=alice, tok2=bob"). The token is baked into the pip config IT pushes
/// centrally (e.g. index-url = https://&lt;token&gt;:@proxy:8090/pypi/simple/), so the developer types plain
/// `pip install` — the token rides along automatically as HTTP Basic userinfo (or a Bearer header). The
/// proxy reads the token, looks it up here, and gets a real identity. The developer's machine only ever
/// holds an opaque token; the mapping to a person never leaves IT.
///
/// No token → we fall back to the client host/IP, labelled "unattributed:&lt;ip&gt;", so a tokenless pull is
/// still visible (and IT can choose to require tokens as policy). This keeps the "developer types nothing"
/// promise while making "who has this vulnerable package" answerable with real names once tokens are rolled.
/// </summary>
public sealed class DevIdentity
{
    private readonly Dictionary<string, string> _tokenToUser;

    public DevIdentity(IConfiguration cfg)
    {
        _tokenToUser = new(StringComparer.Ordinal);
        // PROXY_DEV_TOKENS: comma/newline-separated "token=identity" pairs. Left = opaque token the dev
        // holds; right = the real person IT maps it to.
        var raw = cfg["PROXY_DEV_TOKENS"] ?? "";
        foreach (var pair in raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var token = pair[..eq].Trim();
            var user = pair[(eq + 1)..].Trim();
            if (token.Length > 0 && user.Length > 0) _tokenToUser[token] = user;
        }
    }

    /// <summary>Resolve the developer for this request. Returns the mapped identity if a known token is
    /// present, else "unattributed:&lt;host-or-ip&gt;".</summary>
    public string Resolve(HttpContext http)
    {
        var token = ExtractToken(http.Request);
        if (token is not null && _tokenToUser.TryGetValue(token, out var user)) return user;
        // Fallback: best available client identifier, clearly labelled as not tied to a person. Normalise an
        // IPv4-mapped IPv6 address to plain IPv4 so the recall list shows a resolvable v4, not "::ffff:…".
        var host = NormalizeIp(http.Connection.RemoteIpAddress);
        if (string.IsNullOrEmpty(host) || host == "::1" || host == "127.0.0.1")
            host = http.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? host ?? "local";
        return $"unattributed:{host}";
    }

    /// <summary>Whether the request carried a RECOGNISED developer token (real attribution available).</summary>
    public bool IsAttributed(HttpContext http)
    {
        var token = ExtractToken(http.Request);
        return token is not null && _tokenToUser.ContainsKey(token);
    }

    /// <summary>Capture the enterprise asset detail for this request. Network + request fields (IP, pip/
    /// python/os) are read straight off the HTTP request; the richer machine fields (hostname/MAC/OS/dept/
    /// asset-tag/os-user) come from the X-Advisory-Asset header IT injects into its pushed pip config
    /// (format: "key=value; key=value", keys: host,mac,os,dept,tag,user). Everything is best-effort — a
    /// request with no header still yields IP + parsed User-Agent, and missing fields stay null (unknown).</summary>
    public Advisory.Api.Scan.ScanStore.AssetInfo CaptureAsset(HttpContext http)
    {
        var req = http.Request;
        // Client IP: prefer the real source, then a forwarding header (proxy/LB in front). Normalise to
        // IPv4 where possible — an IPv4-mapped IPv6 address (::ffff:192.168.80.1, which is what Kestrel
        // reports on a dual-stack socket) is unmappable/unresolvable for a security team; DNS resolves the
        // v4 form. IPAddress.MapToIPv4() turns ::ffff:a.b.c.d into a.b.c.d and leaves real v6 alone.
        var ip = NormalizeIp(http.Connection.RemoteIpAddress);
        if (string.IsNullOrEmpty(ip) || ip is "::1" or "127.0.0.1")
            ip = req.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? ip;

        // pip sends a User-Agent like "pip/24.0 {"cpython":{"version":"3.12.1"},"system":{"name":"Linux"…}}".
        var ua = req.Headers.UserAgent.FirstOrDefault() ?? "";
        string? pip = null, py = null, platform = null;
        var mPip = System.Text.RegularExpressions.Regex.Match(ua, @"pip/([\d.]+)");
        if (mPip.Success) pip = mPip.Groups[1].Value;
        var mPy = System.Text.RegularExpressions.Regex.Match(ua, @"""cpython""\s*:\s*\{\s*""version""\s*:\s*""([\d.]+)""");
        if (mPy.Success) py = mPy.Groups[1].Value;
        var mSys = System.Text.RegularExpressions.Regex.Match(ua, @"""name""\s*:\s*""([^""]+)""");
        if (mSys.Success) platform = mSys.Groups[1].Value;

        // IT-injected asset header (opt-in, world-class detail): "host=…; mac=…; os=…; dept=…; tag=…; user=…; project=…".
        var kv = ParseAssetHeader(req.Headers["X-Advisory-Asset"].FirstOrDefault());
        string? Get(params string[] keys) { foreach (var k in keys) if (kv.TryGetValue(k, out var v) && v.Length > 0) return v; return null; }

        // Project/app this pull belongs to — drives the per-project SBOM (ISO 27001). Prefer a dedicated
        // X-Advisory-Project header (simplest for CI to set per repo), else "project=" in the asset header.
        var project = req.Headers["X-Advisory-Project"].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(project)) project = Get("project", "app", "application", "repo");

        // Hostname: prefer the IT-injected header; else best-effort reverse-DNS on the (normalised) IP so a
        // machine is still identifiable by name, not just a bare address. Reverse-DNS is skipped for
        // loopback and is bounded so it never delays a request.
        var hostname = Get("host", "hostname", "fqdn") ?? ReverseDns(ip);

        return new Advisory.Api.Scan.ScanStore.AssetInfo(
            Hostname: hostname,
            Ip: ip,
            Mac: Get("mac", "macaddr"),
            Os: Get("os", "osversion") ?? platform,
            Department: Get("dept", "department", "team"),
            AssetTag: Get("tag", "assettag", "asset", "serial"),
            OsUser: Get("user", "osuser", "loginuser"),
            PipVersion: pip, PythonVersion: py, Platform: platform,
            Project: project);
    }

    // Turn an IPv4-mapped IPv6 address (::ffff:a.b.c.d) into plain IPv4; leave real IPv4/IPv6 as-is.
    private static string? NormalizeIp(System.Net.IPAddress? addr)
    {
        if (addr is null) return null;
        if (addr.IsIPv4MappedToIPv6) return addr.MapToIPv4().ToString();
        return addr.ToString();
    }

    // Best-effort reverse-DNS with a tight timeout so it never delays a pull; returns null on any failure,
    // for loopback, or if it doesn't resolve. Not called when the IT asset header already supplies a host.
    private static string? ReverseDns(string? ip)
    {
        if (string.IsNullOrEmpty(ip) || ip is "::1" or "127.0.0.1" || ip.StartsWith("unattributed", StringComparison.Ordinal)) return null;
        if (!System.Net.IPAddress.TryParse(ip, out var addr)) return null;
        try
        {
            var task = System.Net.Dns.GetHostEntryAsync(addr);
            if (!task.Wait(TimeSpan.FromMilliseconds(200))) return null;   // bounded — don't block the pull
            var host = task.Result?.HostName;
            return string.IsNullOrWhiteSpace(host) || host == ip ? null : host;
        }
        catch { return null; }
    }

    private static Dictionary<string, string> ParseAssetHeader(string? header)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(header)) return d;
        foreach (var part in header.Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            d[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return d;
    }

    // Pull the token from either HTTP Basic userinfo (pip sends the index-url creds as Basic) or a Bearer
    // header. For Basic, pip puts the token in the username (password blank) → we accept username as token,
    // OR "token:" form; we take whichever non-empty part looks like our token.
    private string? ExtractToken(HttpRequest req)
    {
        var auth = req.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(auth)) return null;
        try
        {
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return auth["Bearer ".Length..].Trim();
            if (auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(auth["Basic ".Length..].Trim()));
                var parts = decoded.Split(':', 2);
                var user = parts[0];
                var pass = parts.Length > 1 ? parts[1] : "";
                // Prefer whichever side is a known token; else return the username (typical: token in user).
                if (_tokenToUser.ContainsKey(user)) return user;
                if (_tokenToUser.ContainsKey(pass)) return pass;
                return string.IsNullOrEmpty(user) ? (string.IsNullOrEmpty(pass) ? null : pass) : user;
            }
        }
        catch { /* malformed auth header → treat as no token */ }
        return null;
    }
}
