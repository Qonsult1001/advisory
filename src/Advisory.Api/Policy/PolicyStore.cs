using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Advisory.Api.Policy;

public interface IPolicyStore
{
    FirewallPolicy Current { get; }
    string CurrentSignature { get; }
    Task<FirewallPolicy> UpdateAsync(FirewallPolicy updated, string actor);
}

/// <summary>
/// Holds the active policy. Bumps version + re-signs (SHA256 over canonical JSON)
/// on every change, and persists to disk. Swap for a DB-backed store in prod.
/// </summary>
public class PolicyStore : IPolicyStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private FirewallPolicy _current;
    private string _signature = "";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PolicyStore(IConfiguration config)
    {
        _path = config["PolicyPath"] ?? "policy.json";
        _current = LoadOrDefault(_path);
        _signature = Sign(_current);
    }

    private static FirewallPolicy LoadOrDefault(string path)
    {
        try
        {
            if (!File.Exists(path)) return new FirewallPolicy();
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return new FirewallPolicy();
            return JsonSerializer.Deserialize<FirewallPolicy>(text, Json) ?? new FirewallPolicy();
        }
        catch (JsonException)
        {
            // Corrupt/empty policy on disk must not crash startup; fall back to safe defaults.
            return new FirewallPolicy();
        }
    }

    public FirewallPolicy Current { get { lock (_lock) return _current; } }
    public string CurrentSignature { get { lock (_lock) return _signature; } }

    public async Task<FirewallPolicy> UpdateAsync(FirewallPolicy updated, string actor)
    {
        lock (_lock)
        {
            var next = int.TryParse(_current.Version, out var v) ? v + 1 : 1;
            updated.Version = next.ToString();
            updated.UpdatedAt = DateTimeOffset.UtcNow;
            updated.UpdatedBy = actor;
            _current = updated;
            _signature = Sign(updated);
        }
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(_current, Json));
        return _current;
    }

    private static string Sign(FirewallPolicy p)
    {
        var canonical = JsonSerializer.Serialize(p, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
