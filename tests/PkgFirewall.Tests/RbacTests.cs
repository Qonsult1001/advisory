using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using PkgFirewall.Api.Auth;
using PkgFirewall.Api.Models;
using Xunit;

namespace PkgFirewall.Tests;

/// <summary>
/// Proves RBAC is actually ENFORCED, not just configured: a Viewer token is rejected from a
/// policy write (Admin-only) and an exception grant (Approver-only), while an Admin token
/// succeeds. Identity is injected via a test auth scheme that mints whichever role the test asks for.
/// </summary>
public class RbacTests
{
    // Test auth scheme: reads desired role from the "X-Test-Role" header and authenticates as it.
    private class RoleHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public RoleHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : base(o, l, e) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault() ?? Roles.Viewer;
            var claims = new[]
            {
                new Claim("preferred_username", $"{role.ToLower()}@test"),
                new Claim("oid", $"oid-{role}"),
                new Claim(ClaimTypes.Role, role),
            };
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private HttpClient ClientWithRoles()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("AzureAd:ClientId", ""); // force non-Entra path so we control auth
            b.ConfigureTestServices(services =>
            {
                // Replace whatever auth the app wired with our role-minting scheme,
                // and enforce the real role policies.
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, RoleHandler>("Test", _ => { });
                services.AddAuthorizationBuilder()
                    .AddPolicy(Policies.CanViewer, p => p.AddAuthenticationSchemes("Test").RequireRole(Roles.Admin, Roles.Approver, Roles.Viewer))
                    .AddPolicy(Policies.CanApprove, p => p.AddAuthenticationSchemes("Test").RequireRole(Roles.Admin, Roles.Approver))
                    .AddPolicy(Policies.CanAdmin, p => p.AddAuthenticationSchemes("Test").RequireRole(Roles.Admin));
            });
        });
        return factory.CreateClient();
    }

    private static HttpRequestMessage As(string role, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-Test-Role", role);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task Viewer_can_read_policy()
    {
        var c = ClientWithRoles();
        var resp = await c.SendAsync(As(Roles.Viewer, HttpMethod.Get, "/api/policy"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_write_policy()
    {
        var c = ClientWithRoles();
        var body = new { version = "x", cvssBlockThreshold = 7.0, blockKnownExploited = true,
            epssBlockThreshold = 0.5, licenseBlocklist = new[]{"GPL-3.0"}, minPackageAgeDays = 14,
            maxTreeDepth = 8, weights = new { safetensorsOnly = true, blockPickle = true, requireHashPin = true },
            enabledSources = new[]{"osv"}, requiredSources = new[]{"osv"}, quarantineOnUncertainty = true,
            enableResearchAgent = false, exceptions = System.Array.Empty<object>() };
        var resp = await c.SendAsync(As(Roles.Viewer, HttpMethod.Put, "/api/policy", body));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_grant_exception()
    {
        var c = ClientWithRoles();
        var body = new { package = "x==1", reason = "r", ticket = "T1", expires = DateTimeOffset.UtcNow.AddDays(1) };
        var resp = await c.SendAsync(As(Roles.Viewer, HttpMethod.Post, "/api/exceptions", body));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Approver_can_grant_exception_but_not_write_policy()
    {
        var c = ClientWithRoles();
        var grant = new { package = "x==1", reason = "approved", ticket = "T2", expires = DateTimeOffset.UtcNow.AddDays(1) };
        var ok = await c.SendAsync(As(Roles.Approver, HttpMethod.Post, "/api/exceptions", grant));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var body = new { version = "x", cvssBlockThreshold = 7.0, blockKnownExploited = true,
            epssBlockThreshold = 0.5, licenseBlocklist = new[]{"GPL-3.0"}, minPackageAgeDays = 14,
            maxTreeDepth = 8, weights = new { safetensorsOnly = true, blockPickle = true, requireHashPin = true },
            enabledSources = new[]{"osv"}, requiredSources = new[]{"osv"}, quarantineOnUncertainty = true,
            enableResearchAgent = false, exceptions = System.Array.Empty<object>() };
        var denied = await c.SendAsync(As(Roles.Approver, HttpMethod.Put, "/api/policy", body));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Admin_can_write_policy()
    {
        var c = ClientWithRoles();
        var body = new { version = "x", cvssBlockThreshold = 8.0, blockKnownExploited = true,
            epssBlockThreshold = 0.5, licenseBlocklist = new[]{"GPL-3.0"}, minPackageAgeDays = 14,
            maxTreeDepth = 8, weights = new { safetensorsOnly = true, blockPickle = true, requireHashPin = true },
            enabledSources = new[]{"osv"}, requiredSources = new[]{"osv"}, quarantineOnUncertainty = true,
            enableResearchAgent = false, exceptions = System.Array.Empty<object>() };
        var resp = await c.SendAsync(As(Roles.Admin, HttpMethod.Put, "/api/policy", body));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
