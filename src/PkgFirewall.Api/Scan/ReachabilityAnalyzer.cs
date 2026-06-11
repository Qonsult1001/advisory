using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PkgFirewall.Api.Models;

namespace PkgFirewall.Api.Scan;

/// <summary>
/// Contextual analysis (reachability) for npm: invokes the bundled Node analyzer
/// (tools/reachability/analyze.mjs, acorn-based) to determine whether a vulnerable package —
/// and where known, its vulnerable symbol — is actually imported and called by the consuming
/// project's first-party code.
///
/// This is single-hop first-party reachability, NOT a full transitive call graph through
/// dependency internals. It removes the largest false-positive class ("CVE in a package you
/// never call") and is explicit (Unknown) where it cannot prove the specific symbol is used.
/// Only runs when a ProjectPath is supplied and Node + the analyzer are available; otherwise
/// every finding is left Unknown (never silently "not reachable").
/// </summary>
public class ReachabilityAnalyzer
{
    private readonly string _analyzerPath;
    private readonly string _nodeExe;
    private readonly int _timeoutMs;

    public ReachabilityAnalyzer(IConfiguration cfg)
    {
        _analyzerPath = cfg["REACHABILITY_ANALYZER"] ?? "/app/reachability/analyze.mjs";
        _nodeExe = cfg["NODE_PATH"] ?? "node";
        _timeoutMs = int.TryParse(cfg["REACHABILITY_TIMEOUT_MS"], out var t) ? t : 30_000;
    }

    public bool IsAvailable(PackageRef root)
        => root.Ecosystem == Ecosystem.npm
           && !string.IsNullOrEmpty(root.ProjectPath)
           && Directory.Exists(root.ProjectPath)
           && File.Exists(_analyzerPath);

    public record Target(string Id, string Package, string[] Symbols);
    private record Verdict(string Id, string Reachability, string Detail);
    private record AnalyzerOutput(List<Verdict>? Results, int FilesScanned, bool Parsed);

    /// <summary>
    /// Returns a map of target id -> (reachability, detail). On any failure returns an empty map
    /// (caller leaves those findings Unknown).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, (string Reachability, string Detail)>> AnalyzeAsync(
        string projectPath, IReadOnlyList<Target> targets, CancellationToken ct)
    {
        var empty = new Dictionary<string, (string, string)>();
        if (targets.Count == 0) return empty;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _nodeExe,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(_analyzerPath);
            psi.ArgumentList.Add(projectPath);

            using var proc = Process.Start(psi);
            if (proc is null) return empty;

            // camelCase to match the Node analyzer's expected shape (input.targets[].package/.symbols).
            var payload = JsonSerializer.Serialize(new { targets },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            // Read both streams concurrently BEFORE waiting for exit, to avoid pipe-buffer deadlock.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.StandardInput.WriteAsync(payload.AsMemory(), ct);
            proc.StandardInput.Close();

            using var reg = new CancellationTokenSource(_timeoutMs);
            await proc.WaitForExitAsync(reg.Token);
            var stdout = await stdoutTask;
            await stderrTask; // drain
            if (string.IsNullOrWhiteSpace(stdout)) return empty;

            var output = JsonSerializer.Deserialize<AnalyzerOutput>(stdout,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (output?.Results is null) return empty;

            return output.Results.ToDictionary(r => r.Id, r => (r.Reachability, r.Detail));
        }
        catch
        {
            return empty; // analyzer missing/crashed/timeout -> findings stay Unknown
        }
    }
}
