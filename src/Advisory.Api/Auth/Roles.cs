namespace Advisory.Api.Auth;

/// <summary>
/// The three RBAC roles, mapped from Entra ID app roles (PCI 7.2 least-privilege).
///   Admin    : edit policy, manage sources, everything Approver/Viewer can do
///   Approver : grant/revoke exceptions, trigger re-evaluation; cannot change policy
///   Viewer   : read-only (policy, audit, queue depth, quarantine)
/// Policy names are used in [Authorize(Policy = ...)] on controllers.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Approver = "Approver";
    public const string Viewer = "Viewer";
}

public static class Policies
{
    public const string CanViewer   = "CanViewer";    // Admin, Approver, Viewer
    public const string CanApprove  = "CanApprove";   // Admin, Approver
    public const string CanAdmin    = "CanAdmin";     // Admin
}
