using System.Net.Http.Json;
using System.Text.Json;

namespace PkgFirewall.Api.Llm;

public record PfEntity(string Category, string Rule, string Sample, double Score);
public record PfResult(bool Ok, List<PfEntity> Entities, string? Redacted, string? Error);

/// <summary>
/// Client for the on-prem OpenAI Privacy Filter sidecar (openai/privacy-filter, token-classification,
/// Apache-2.0, run via transformers.js/ONNX). It detects + redacts PII without the text ever leaving
/// the network. Best-effort: when the sidecar is absent or still loading the model, callers fall back
/// to the AI (Groq) + regex DLP layers, so DLP never silently turns off.
/// </summary>
public interface IPrivacyFilter
{
    bool Configured { get; }
    Task<bool> IsReadyAsync(CancellationToken ct);
    Task<string> StateAsync(CancellationToken ct);
    Task<PfResult> RedactAsync(string text, CancellationToken ct);
}

public class PrivacyFilterClient : IPrivacyFilter
{
    private readonly HttpClient _http;
    private readonly string? _baseUrl;
    private static readonly JsonSerializerOptions J = new() { PropertyNameCaseInsensitive = true };

    public PrivacyFilterClient(IHttpClientFactory f, IConfiguration cfg)
    {
        _http = f.CreateClient("pf");
        _baseUrl = cfg["PRIVACY_FILTER_URL"];
    }

    public bool Configured => !string.IsNullOrWhiteSpace(_baseUrl);

    public async Task<bool> IsReadyAsync(CancellationToken ct) => await StateAsync(ct) == "ready";

    /// <summary>Raw sidecar state: ready | loading | unsupported | error | down.</summary>
    public async Task<string> StateAsync(CancellationToken ct)
    {
        if (!Configured) return "down";
        try
        {
            using var resp = await _http.GetAsync($"{_baseUrl}/health", ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("state", out var s) ? s.GetString() ?? "down" : "down";
        }
        catch { return "down"; }
    }

    public async Task<PfResult> RedactAsync(string text, CancellationToken ct)
    {
        if (!Configured) return new PfResult(false, new(), null, "not configured");
        try
        {
            using var resp = await _http.PostAsJsonAsync($"{_baseUrl}/redact", new { text }, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return new PfResult(false, new(), null, $"HTTP {(int)resp.StatusCode}: {raw}");
            using var doc = JsonDocument.Parse(raw);
            var ents = new List<PfEntity>();
            if (doc.RootElement.TryGetProperty("entities", out var arr))
                foreach (var e in arr.EnumerateArray())
                    ents.Add(new PfEntity(
                        e.TryGetProperty("category", out var c) ? c.GetString() ?? "PII" : "PII",
                        e.TryGetProperty("rule", out var r) ? r.GetString() ?? "PII" : "PII",
                        e.TryGetProperty("sample", out var sm) ? sm.GetString() ?? "" : "",
                        e.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 0));
            var red = doc.RootElement.TryGetProperty("redacted", out var rd) ? rd.GetString() : null;
            return new PfResult(true, ents, red, null);
        }
        catch (Exception ex) { return new PfResult(false, new(), null, ex.Message); }
    }
}
