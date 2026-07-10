using System.Collections.Concurrent;
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
using Advisory.Api.Auth;
using Advisory.Api.Models;
using Advisory.Api.Nexus;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins the provision API contract (#153) at the HTTP seam: GET /api/nexus/ecosystems reports the
/// gateable set + live state, POST /provision is idempotent and Admin-gated, DELETE is Admin-gated.
/// A fake INexusClient records the calls — no real Nexus.
/// </summary>
public class NexusProvisionTests
{
    private sealed class RecordingNexus : INexusClient
    {
        public ConcurrentBag<Ecosystem> Provisioned = new();
        public ConcurrentBag<Ecosystem> Deprovisioned = new();
        public HashSet<string> Existing = new(StringComparer.OrdinalIgnoreCase);

        public bool IsConfigured => true;
        public Task<ProvisionResult> ProvisionAsync(Ecosystem eco, CancellationToken ct)
        {
            var already = Existing.Contains($"{NexusEcosystems.Prefix(eco)}-quarantine");
            Provisioned.Add(eco);
            Existing.Add($"{NexusEcosystems.Prefix(eco)}-quarantine");
            Existing.Add($"{NexusEcosystems.Prefix(eco)}-approved");
            return Task.FromResult(new ProvisionResult(true, already, null));
        }
        public Task<int> DeprovisionAsync(Ecosystem eco, CancellationToken ct) { Deprovisioned.Add(eco); return Task.FromResult(2); }
        public Task<IReadOnlySet<string>> ExistingRepoNamesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlySet<string>>(Existing);
        public Task<bool> IsReachableAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RevokeApprovedAsync(Ecosystem eco, string name, string version, CancellationToken ct) => Task.FromResult(true);
        public Task<int> EmptyFirewallReposAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<bool> FetchIntoQuarantineAsync(Ecosystem eco, string name, string version, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> PromoteByNameAsync(Ecosystem eco, string name, string version, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<NexusRepo>> ListRepositoriesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NexusRepo>>(Array.Empty<NexusRepo>());
        public Task<IReadOnlyList<NexusComponent>> ListComponentsAsync(string repo, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NexusComponent>>(Array.Empty<NexusComponent>());
        public Task<IReadOnlyList<NexusComponent>> ListQuarantineAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NexusComponent>>(Array.Empty<NexusComponent>());
        public Task<byte[]> DownloadAsync(string url, CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
        public Task PromoteAsync(NexusComponent c, byte[] b, CancellationToken ct) => Task.CompletedTask;
        public Task<int> PromoteAllFilesAsync(NexusComponent c, CancellationToken ct) => Task.FromResult(0);
        public Task HoldAsync(NexusComponent c, string reason, CancellationToken ct) => Task.CompletedTask;
    }

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
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private readonly RecordingNexus _nexus = new();

    private HttpClient Client()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("AzureAd:ClientId", "");
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton<INexusClient>(_nexus);
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
    public async Task Ecosystems_endpoint_lists_gateable_set()
    {
        var c = Client();
        var resp = await c.SendAsync(As(Roles.Viewer, HttpMethod.Get, "/api/nexus/ecosystems"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Maven", json);
        Assert.Contains("RubyGems", json);
        // All 17 are reported with their honest mechanism — scanner + research-only tiers included.
        Assert.Contains("AIEditorExtensions", json);
        Assert.Contains("scanner", json);
        Assert.Contains("research-only", json);
    }

    [Fact]
    public async Task Admin_can_provision_and_it_is_idempotent()
    {
        var c = Client();
        var first = await c.SendAsync(As(Roles.Admin, HttpMethod.Post, "/api/nexus/provision", new { ecosystem = "Maven" }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await c.SendAsync(As(Roles.Admin, HttpMethod.Post, "/api/nexus/provision", new { ecosystem = "Maven" }));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode); // idempotent — no error on re-add
        Assert.Contains("already", (await second.Content.ReadAsStringAsync()).ToLowerInvariant());
    }

    [Fact]
    public async Task Viewer_cannot_provision()
    {
        var c = Client();
        var resp = await c.SendAsync(As(Roles.Viewer, HttpMethod.Post, "/api/nexus/provision", new { ecosystem = "Maven" }));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_deprovision()
    {
        var c = Client();
        var resp = await c.SendAsync(As(Roles.Viewer, HttpMethod.Delete, "/api/nexus/ecosystem/Maven"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_can_deprovision()
    {
        var c = Client();
        var resp = await c.SendAsync(As(Roles.Admin, HttpMethod.Delete, "/api/nexus/ecosystem/Maven"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(Ecosystem.Maven, _nexus.Deprovisioned);
    }

    [Fact]
    public async Task Provisioning_a_deferred_or_unknown_ecosystem_is_rejected()
    {
        var c = Client();
        // Conda is deferred (no CVE source) — must not be provisionable.
        var resp = await c.SendAsync(As(Roles.Admin, HttpMethod.Post, "/api/nexus/provision", new { ecosystem = "Conda" }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
