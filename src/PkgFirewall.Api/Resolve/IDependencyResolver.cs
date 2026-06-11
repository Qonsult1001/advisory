using PkgFirewall.Api.Models;

namespace PkgFirewall.Api.Resolve;

/// <summary>A resolved node in the dependency tree.</summary>
public record DepNode(PackageRef Package, int Depth, string? Parent);

/// <summary>
/// Resolves the FULL transitive dependency tree for a package.
/// This is the parity-critical piece: Xray evaluates the whole tree,
/// not just the requested artifact. One resolver per ecosystem behind this.
/// </summary>
public interface IDependencyResolver
{
    Ecosystem Ecosystem { get; }
    Task<IReadOnlyList<DepNode>> ResolveAsync(PackageRef root, int maxDepth, CancellationToken ct);
}
