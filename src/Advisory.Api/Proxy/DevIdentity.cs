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
        // Fallback: best available client identifier, clearly labelled as not tied to a person.
        var host = http.Connection.RemoteIpAddress?.ToString();
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
