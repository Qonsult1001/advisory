using System.Text.Json;
using Advisory.Api.Nexus;
using Advisory.Api.Policy;

namespace Advisory.Api.Catalog;

/// <summary>One model as shown in Discovery / Registry — live metadata from the Hugging Face Hub.</summary>
public record AiModel(
    string Id, string Author, string? Task, string? Library, string License,
    long Downloads, long Likes, DateTimeOffset? Updated,
    bool Gated, string WeightFormat,          // safetensors | pickle | mixed | other | unknown
    string Risk, List<string> RiskReasons,    // High | Medium | Low
    bool Allowed);

/// <summary>A model's full card: the list view fields + files.</summary>
public record AiModelDetail(AiModel Model, List<AiModelFile> Files, List<string> Tags);
public record AiModelFile(string Name, string Format);   // format: safetensors|pickle|onnx|gguf|config|other

/// <summary>A model artifact found inside the org's repositories (Detection / "shadow AI").</summary>
public record DetectedModel(string Repo, string Name, string Version, string? FileName,
    string Format, string Status);            // Status: Approved | Shadow AI

/// <summary>
/// JFrog AI Catalog parity, on free APIs. Discovery/model cards come live from the Hugging Face
/// Hub API; risk is scored from weight format (pickle vs safetensors), license, gating and
/// popularity; Detection sweeps the Nexus repositories for model artifacts and flags anything
/// not on the approved registry as shadow AI.
/// </summary>
public class AiCatalogService
{
    private readonly HttpClient _http;
    private readonly IPolicyStore _policy;
    private readonly INexusClient _nexus;
    private readonly ConsumedModelStore _consumed;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public AiCatalogService(IHttpClientFactory f, IPolicyStore policy, INexusClient nexus, ConsumedModelStore consumed)
    { _http = f.CreateClient("hf"); _policy = policy; _nexus = nexus; _consumed = consumed; }

    // ---- Discovery ----------------------------------------------------------

    public async Task<List<AiModel>> SearchAsync(string? q, string sort, int limit, CancellationToken ct)
    {
        var sortField = sort switch { "likes" => "likes", "updated" => "lastModified", _ => "downloads" };
        var url = $"https://huggingface.co/api/models?limit={Math.Clamp(limit, 1, 50)}" +
                  $"&sort={sortField}&direction=-1&full=true" +
                  (string.IsNullOrWhiteSpace(q) ? "" : $"&search={Uri.EscapeDataString(q)}");
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, ct));
        var allowed = AllowedIds();
        var list = new List<AiModel>();
        foreach (var m in doc.RootElement.EnumerateArray())
            list.Add(ToModel(m, allowed));
        return list;
    }

    public async Task<AiModelDetail?> GetModelAsync(string id, CancellationToken ct)
    {
        var url = $"https://huggingface.co/api/models/{Uri.EscapeDataString(id).Replace("%2F", "/")}";
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var model = ToModel(root, AllowedIds());
        var files = new List<AiModelFile>();
        if (root.TryGetProperty("siblings", out var sib))
            foreach (var f in sib.EnumerateArray())
            {
                var name = f.GetProperty("rfilename").GetString() ?? "";
                files.Add(new AiModelFile(name, FileFormat(name)));
            }
        var tags = new List<string>();
        if (root.TryGetProperty("tags", out var tg))
            tags = tg.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => t.Length > 0).ToList();
        return new AiModelDetail(model, files, tags);
    }

    /// <summary>Registry view: each approved model re-joined with live Hub metadata (license drift shows).</summary>
    public async Task<List<object>> RegistryAsync(CancellationToken ct)
    {
        var rows = new List<object>();
        foreach (var a in _policy.Current.AllowedModels)
        {
            AiModelDetail? live = null;
            try { live = await GetModelAsync(a.Id, ct); } catch { /* offline-tolerant */ }
            rows.Add(new
            {
                a.Id, a.ApprovedBy, a.ApprovedAt, a.Notes,
                approvedLicense = a.License,
                live = live?.Model,
                licenseDrift = live is not null && !string.IsNullOrWhiteSpace(a.License)
                    && !string.Equals(live.Model.License, a.License, StringComparison.OrdinalIgnoreCase),
            });
        }
        return rows;
    }

    // ---- Detection (shadow AI) ----------------------------------------------

    private static readonly string[] ModelExtensions =
        { ".safetensors", ".bin", ".pt", ".pth", ".ckpt", ".pkl", ".pickle", ".onnx", ".gguf", ".h5", ".tflite", ".msgpack" };

    public async Task<List<DetectedModel>> DetectAsync(CancellationToken ct)
    {
        var allowed = AllowedIds();
        var found = new List<DetectedModel>();

        // Models pulled into repositories through the firewall (the consume flow).
        foreach (var c in _consumed.List())
            found.Add(new DetectedModel(c.Repo, c.ModelId, c.Version, c.File, c.Format,
                allowed.Contains(c.ModelId) ? "Approved" : "Shadow AI"));

        // Live Nexus sweep for any model files that landed in repositories directly.
        if (_nexus.IsConfigured)
            foreach (var repo in await _nexus.ListRepositoriesAsync(ct))
            {
                IReadOnlyList<NexusComponent> comps;
                try { comps = await _nexus.ListComponentsAsync(repo.Name, ct); } catch { continue; }
                foreach (var c in comps)
                {
                    var fn = c.FileName ?? "";
                    var ext = ModelExtensions.FirstOrDefault(e => fn.EndsWith(e, StringComparison.OrdinalIgnoreCase));
                    var isHf = c.Ecosystem == Models.Ecosystem.HuggingFace;
                    if (ext is null && !isHf) continue;
                    if (found.Any(f => f.Repo == repo.Name && f.Name == c.Name && f.FileName == c.FileName)) continue;
                    found.Add(new DetectedModel(repo.Name, c.Name, c.Version, c.FileName,
                        ext is null ? "model" : FileFormat(fn),
                        allowed.Contains(c.Name) ? "Approved" : "Shadow AI"));
                }
            }
        return found;
    }

    // ---- helpers -------------------------------------------------------------

    private HashSet<string> AllowedIds() =>
        _policy.Current.AllowedModels.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string FileFormat(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.EndsWith(".safetensors")) return "safetensors";
        if (n.EndsWith(".bin") || n.EndsWith(".pt") || n.EndsWith(".pth") || n.EndsWith(".ckpt")
            || n.EndsWith(".pkl") || n.EndsWith(".pickle")) return "pickle";
        if (n.EndsWith(".onnx")) return "onnx";
        if (n.EndsWith(".gguf")) return "gguf";
        if (n.EndsWith(".json") || n.EndsWith(".txt") || n.EndsWith(".md") || n.EndsWith(".model")) return "config";
        return "other";
    }

    private AiModel ToModel(JsonElement m, HashSet<string> allowed)
    {
        var id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var author = id.Contains('/') ? id.Split('/')[0] : "";
        long downloads = m.TryGetProperty("downloads", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0;
        long likes = m.TryGetProperty("likes", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt64() : 0;
        var task = m.TryGetProperty("pipeline_tag", out var pt) ? pt.GetString() : null;
        var lib = m.TryGetProperty("library_name", out var lb) ? lb.GetString() : null;
        var gated = m.TryGetProperty("gated", out var g) && g.ValueKind != JsonValueKind.False && g.ValueKind != JsonValueKind.Null;
        DateTimeOffset? updated = m.TryGetProperty("lastModified", out var lm) && lm.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(lm.GetString()!) : null;

        var license = "";
        var tags = new List<string>();
        if (m.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
            foreach (var t in tg.EnumerateArray())
            {
                var s = t.GetString() ?? "";
                tags.Add(s);
                if (s.StartsWith("license:", StringComparison.OrdinalIgnoreCase)) license = s[8..];
            }

        // Weight format from the file list when present (full=true includes siblings).
        // A .bin paired with a same-stem .xml is OpenVINO IR (raw tensors, not pickle) — don't
        // count it as pickle. Byte-level confirmation happens via /aicatalog/verify.
        var hasSafetensors = false; var hasPickle = false; var anyWeights = false;
        if (m.TryGetProperty("siblings", out var sib) && sib.ValueKind == JsonValueKind.Array)
        {
            var names = sib.EnumerateArray()
                .Select(f => f.GetProperty("rfilename").GetString() ?? "").ToList();
            var xmlStems = names.Where(n => n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(n => n[..^4]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var fmt = FileFormat(name);
                if (fmt == "pickle" && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                    && name.Contains('.') && xmlStems.Contains(name[..name.LastIndexOf('.')]))
                    fmt = "other"; // OpenVINO IR raw weights
                if (fmt == "safetensors") { hasSafetensors = true; anyWeights = true; }
                else if (fmt == "pickle") { hasPickle = true; anyWeights = true; }
                else if (fmt is "onnx" or "gguf") anyWeights = true;
            }
        }
        var format = !anyWeights ? "unknown"
            : hasSafetensors && hasPickle ? "mixed"
            : hasSafetensors ? "safetensors"
            : hasPickle ? "pickle" : "other";

        var (risk, reasons) = ScoreRisk(format, license, gated, downloads, updated);
        return new AiModel(id, author, task, lib, license, downloads, likes, updated, gated, format, risk, reasons,
            allowed.Contains(id));
    }

    private static readonly string[] PermissiveLicenses =
        { "apache-2.0", "mit", "bsd-3-clause", "bsd-2-clause", "openrail", "cc-by-4.0", "gemma", "llama2", "llama3", "llama3.1", "llama3.2" };

    private static (string, List<string>) ScoreRisk(string format, string license, bool gated, long downloads, DateTimeOffset? updated)
    {
        var reasons = new List<string>();
        var score = 0;
        if (format == "pickle") { score += 3; reasons.Add("Weights only available in pickle-based format (arbitrary code execution on load)"); }
        else if (format == "mixed") { score += 1; reasons.Add("Repository carries pickle-based weights alongside safetensors"); }
        else if (format == "unknown") { score += 1; reasons.Add("No recognizable weight files — verify contents before use"); }
        if (string.IsNullOrWhiteSpace(license)) { score += 2; reasons.Add("No license declared — legal review required"); }
        else if (license.Contains("nc", StringComparison.OrdinalIgnoreCase)) { score += 2; reasons.Add($"Non-commercial license ({license})"); }
        else if (!PermissiveLicenses.Any(p => license.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        { score += 1; reasons.Add($"Non-standard license ({license}) — review terms"); }
        if (gated) { score += 1; reasons.Add("Gated model — access terms apply"); }
        if (downloads < 1000) { score += 1; reasons.Add("Low adoption (under 1k downloads)"); }
        if (updated is { } u && u < DateTimeOffset.UtcNow.AddYears(-2)) { score += 1; reasons.Add("Not updated in over 2 years"); }
        if (reasons.Count == 0) reasons.Add("Safetensors weights, permissive license, healthy adoption");
        return (score >= 3 ? "High" : score >= 1 ? "Medium" : "Low", reasons);
    }
}
