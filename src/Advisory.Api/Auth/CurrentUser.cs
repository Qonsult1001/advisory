using System.Security.Claims;

namespace Advisory.Api.Auth;

/// <summary>Resolves the authenticated identity for audit attribution (PCI 10.2).</summary>
public interface ICurrentUser
{
    string Name { get; }     // UPN / preferred_username, or "system" for background jobs
    string ObjectId { get; } // Entra oid (stable per-user id)
    IReadOnlyList<string> Roles { get; }
}

public class CurrentUser : ICurrentUser
{
    private readonly ClaimsPrincipal? _p;
    public CurrentUser(IHttpContextAccessor http) => _p = http.HttpContext?.User;

    public string Name =>
        _p?.FindFirst("preferred_username")?.Value
        ?? _p?.FindFirst(ClaimTypes.Upn)?.Value
        ?? _p?.FindFirst(ClaimTypes.Name)?.Value
        ?? "system";

    public string ObjectId =>
        _p?.FindFirst("oid")?.Value
        ?? _p?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? "system";

    public IReadOnlyList<string> Roles =>
        _p?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? new List<string>();
}
