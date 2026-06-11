using PkgFirewall.Api.Audit;
using PkgFirewall.Api.Gate;
using PkgFirewall.Api.Integrations;
using PkgFirewall.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using PkgFirewall.Api.Nexus;
using PkgFirewall.Api.Queue;
using PkgFirewall.Api.Policy;
using PkgFirewall.Api.Research;
using PkgFirewall.Api.Resolve;
using PkgFirewall.Api.VulnSources;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));
// --- Entra ID (Azure AD) authentication + RBAC ---
// Configure via env: AzureAd__Instance, AzureAd__TenantId, AzureAd__ClientId, AzureAd__Audience.
// Entra app roles "Admin" / "Approver" / "Viewer" arrive as role claims in the JWT.
var entraConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"]);
if (entraConfigured)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
}
else
{
    // Dev fallback: allow anonymous so the API runs locally without an IdP.
    builder.Services.AddAuthentication("Dev").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthHandler>("Dev", _ => { });
}
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Policies.CanViewer,  pol => pol.RequireRole(Roles.Admin, Roles.Approver, Roles.Viewer));
    o.AddPolicy(Policies.CanApprove, pol => pol.RequireRole(Roles.Admin, Roles.Approver));
    o.AddPolicy(Policies.CanAdmin,   pol => pol.RequireRole(Roles.Admin));
    if (!entraConfigured)
    {
        // Dev: no roles enforced.
        o.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("Dev").RequireAssertion(_ => true).Build();
        foreach (var name in new[]{Policies.CanViewer,Policies.CanApprove,Policies.CanAdmin})
            o.AddPolicy(name, p => p.RequireAssertion(_ => true));
    }
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Timeouts so a slow feed registers as Timeout, not a hang.
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("osv", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddHttpClient("kev", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("epss", c => c.Timeout = TimeSpan.FromSeconds(6));
builder.Services.AddHttpClient("resolve", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddHttpClient("groq", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("itsm", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("artifactory", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("nexus", c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient("catalog", c => { c.Timeout = TimeSpan.FromSeconds(12); c.DefaultRequestHeaders.Add("User-Agent", "PkgFirewall-Catalog"); });
builder.Services.AddHttpClient("oprisk", c => { c.Timeout = TimeSpan.FromSeconds(10); c.DefaultRequestHeaders.Add("User-Agent", "PkgFirewall-OpRisk"); });
builder.Services.AddHttpClient("hf", c => { c.Timeout = TimeSpan.FromSeconds(15); c.DefaultRequestHeaders.Add("User-Agent", "PkgFirewall-AiCatalog"); });
builder.Services.AddHttpClient("hf-dl", c => { c.Timeout = TimeSpan.FromMinutes(10); c.DefaultRequestHeaders.Add("User-Agent", "PkgFirewall-WeightVerify"); });

// Core
builder.Services.AddSingleton<IPolicyStore, PolicyStore>();
builder.Services.AddSingleton<IWormSink, FileWormSink>();
builder.Services.AddSingleton<IAuditLog, AuditLog>();
builder.Services.AddSingleton<IGroqClient, GroqClient>();
builder.Services.AddSingleton<PkgFirewall.Api.Scan.OnDemandScanService>();
builder.Services.AddSingleton<PkgFirewall.Api.Catalog.ConsumedModelStore>();
builder.Services.AddSingleton<PkgFirewall.Api.Evolution.EvolutionService>();
builder.Services.AddSingleton<PkgFirewall.Api.Catalog.AiCatalogService>();
builder.Services.AddSingleton<PkgFirewall.Api.Catalog.WeightVerifier>();
builder.Services.AddSingleton<PkgFirewall.Api.Catalog.VerificationJobService>();
builder.Services.AddSingleton<PkgFirewall.Api.Llm.LlmAuditService>();
builder.Services.AddSingleton<PkgFirewall.Api.Llm.IPrivacyFilter, PkgFirewall.Api.Llm.PrivacyFilterClient>();
builder.Services.AddHttpClient("pf", c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<PkgFirewall.Api.Llm.DlpInspector>();
builder.Services.AddHttpClient("llm-gw", c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddSingleton<IResearchAgent, ClaudeResearchAgent>();
builder.Services.AddSingleton<IItsmNotifier, ItsmWebhook>();

// Intel plugins
builder.Services.AddSingleton<KevSource>();
builder.Services.AddSingleton<EpssSource>();
builder.Services.AddSingleton<OsvSource>();
builder.Services.AddSingleton<IVulnSource>(sp => sp.GetRequiredService<OsvSource>());
builder.Services.AddSingleton<IVulnSource>(sp => sp.GetRequiredService<KevSource>());
builder.Services.AddSingleton<IVulnSource>(sp => sp.GetRequiredService<EpssSource>());
builder.Services.AddSingleton<IVulnSource, VulnCheckSource>();
builder.Services.AddSingleton<IVulnSource, MalwareSource>();
builder.Services.AddSingleton<IVulnSource, ArtifactorySource>();

// Resolvers
builder.Services.AddSingleton<IDependencyResolver, PyPiResolver>();
builder.Services.AddSingleton<IDependencyResolver, NpmResolver>();
builder.Services.AddSingleton<IDependencyResolver, NuGetResolver>();
builder.Services.AddSingleton<IDependencyResolver, CargoResolver>();
builder.Services.AddSingleton<IDependencyResolver, GoResolver>();

// Scanners
builder.Services.AddSingleton<PkgFirewall.Api.Scan.PickleScanner>();
builder.Services.AddSingleton<PkgFirewall.Api.Scan.SecretScanner>();
builder.Services.AddSingleton<PkgFirewall.Api.Scan.IacScanner>();
builder.Services.AddSingleton<PkgFirewall.Api.Scan.ReachabilityAnalyzer>();
builder.Services.AddSingleton<PkgFirewall.Api.Catalog.CatalogService>();
builder.Services.AddSingleton<PkgFirewall.Api.Catalog.OpRiskService>();
builder.Services.AddSingleton<PkgFirewall.Api.Scan.ScanStore>();

builder.Services.AddScoped<IGateEngine, GateEngine>();
builder.Services.AddHostedService<ExceptionSweepJob>();
// Intake queue: durable Redis Streams when REDIS_URL set, else in-memory fallback.
if (!string.IsNullOrWhiteSpace(builder.Configuration["SQL_CONNECTION_STRING"]))
    builder.Services.AddSingleton<IIntakeQueue, SqlServerQueue>();
else
    builder.Services.AddSingleton<IIntakeQueue, InMemoryQueue>();
builder.Services.AddHostedService<IntakeConsumer>();
builder.Services.AddSingleton<INexusClient, NexusClient>();
builder.Services.AddHostedService<PromotionBridge>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "http://localhost:8080").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
