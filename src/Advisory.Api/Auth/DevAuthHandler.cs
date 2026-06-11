using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Advisory.Api.Auth;

/// <summary>
/// Dev-only auth: when Entra is not configured, authenticate every request as a local
/// "dev" principal holding all roles, so the API runs without an IdP. NEVER enabled when
/// AzureAd:ClientId is set. Production requires real Entra tokens.
/// </summary>
public class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : base(o, l, e) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("preferred_username", "dev@local"),
            new Claim("oid", "dev-local"),
            new Claim(ClaimTypes.Role, Roles.Admin),
            new Claim(ClaimTypes.Role, Roles.Approver),
            new Claim(ClaimTypes.Role, Roles.Viewer),
        };
        var identity = new ClaimsIdentity(claims, "Dev");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Dev");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
