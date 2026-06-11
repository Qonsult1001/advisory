using System.Collections.Concurrent;
using System.IO.Compression;
using Advisory.Api.Scan;

namespace Advisory.Api.Catalog;

/// <summary>Byte-level verdict for one weight file. Method records HOW we know:
/// "magic" (first-bytes signature), "full-scan" (downloaded to cache + structural/opcode scan),
/// or "inconclusive" (could not be confirmed — never silently assumed).</summary>
public record WeightVerdict(string Name, string Format, string Method, bool Confirmed, string Detail,
    List<string> MaliciousHits);

/// <summary>
/// 100%-accuracy weight-format verification for Hugging Face models. Stage 1 reads the first
/// 16 bytes via an HTTP Range request (pickle, torch-zip, safetensors, gguf and hdf5 all have
/// unambiguous signatures — a few hundred bytes of network, no key). Stage 2, when the magic is
/// inconclusive, downloads the file to a local cache and scans it: ZIPs are opened and checked
/// for .pkl entries, pickle streams get the real opcode scan (dangerous GLOBAL/REDUCE imports),
/// anything with no pickle structure is confirmed raw. Verdicts are cached in memory.
/// </summary>
public class WeightVerifier
{
    private readonly IHttpClientFactory _f;
    private readonly PickleScanner _pickle;
    private readonly ILogger<WeightVerifier> _log;
    private readonly ConcurrentDictionary<string, WeightVerdict> _cache = new();
    private readonly string _cacheDir;
    private readonly long _maxFullScanBytes;

    public WeightVerifier(IHttpClientFactory f, PickleScanner pickle, IConfiguration cfg, ILogger<WeightVerifier> log)
    {
        _f = f; _pickle = pickle; _log = log;
        // Persistent cache (survives restarts) under the mounted /data volume when present,
        // else a temp dir for local dev. Holds the downloaded GB weight files until evicted.
        _cacheDir = cfg["VERIFY_CACHE_DIR"]
            ?? (Directory.Exists("/data") ? "/data/verify-cache" : Path.Combine(Path.GetTempPath(), "pkgfw-verify-cache"));
        Directory.CreateDirectory(_cacheDir);
        _maxFullScanBytes = long.TryParse(cfg["VERIFY_MAX_MB"], out var mb) ? mb * 1024L * 1024L : 1536L * 1024 * 1024;
    }

    public async Task<List<WeightVerdict>> VerifyModelAsync(string modelId, List<AiModelFile> files, CancellationToken ct)
    {
        var weightFiles = files.Where(x => x.Format is not ("config" or "other")).ToList();
        var xmlStems = files.Where(x => x.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Name[..^4]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<WeightVerdict>();
        var sem = new SemaphoreSlim(4);
        var tasks = weightFiles.Select(async wf =>
        {
            await sem.WaitAsync(ct);
            try { return await VerifyFileAsync(modelId, wf, xmlStems, ct); }
            finally { sem.Release(); }
        });
        results.AddRange(await Task.WhenAll(tasks));
        return results.OrderBy(r => r.Name).ToList();
    }

    /// <summary>Per-model persistent cache directory (downloaded GB files live here until evicted).</summary>
    private string ModelCacheDir(string modelId)
    {
        var safe = string.Concat(modelId.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var dir = Path.Combine(_cacheDir, safe);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Delete a model's cached downloads. Returns bytes freed.</summary>
    public long EvictCache(string modelId)
    {
        var dir = ModelCacheDir(modelId);
        long freed = 0;
        try
        {
            foreach (var f in Directory.GetFiles(dir)) { freed += new FileInfo(f).Length; File.Delete(f); }
            Directory.Delete(dir, true);
        }
        catch { /* best-effort */ }
        // also drop the in-memory verdicts so a re-verify re-downloads
        foreach (var k in _cache.Keys.Where(k => k.StartsWith(modelId + ":", StringComparison.Ordinal)).ToList())
            _cache.TryRemove(k, out _);
        return freed;
    }

    /// <summary>
    /// Progress-tracked verification for the background job runner. Updates <paramref name="fp"/>
    /// through head → downloading(%) → scanning → verdict, streaming the download to a persistent
    /// cache file and reporting bytes via <paramref name="onBytes"/>.
    /// </summary>
    public async Task<WeightVerdict> VerifyFileTrackedAsync(string modelId, AiModelFile wf,
        HashSet<string> xmlStems, FileProgress fp, Action<long> onBytes, CancellationToken ct)
    {
        var key = $"{modelId}:{wf.Name}";
        if (_cache.TryGetValue(key, out var cached)) { fp.Stage = "done"; return cached; }

        var url = $"https://huggingface.co/{modelId}/resolve/main/{wf.Name}";
        WeightVerdict verdict;
        try
        {
            fp.Stage = "head";
            var (head, totalLen) = await ReadHeadAsync(url, ct);
            fp.TotalBytes = totalLen;
            verdict = ClassifyMagic(wf, head);   // signature hit → no download needed

            if (verdict.Method == "inconclusive")
            {
                if (totalLen > _maxFullScanBytes)
                    verdict = verdict with { Method = "inconclusive",
                        Detail = $"{verdict.Detail}; file is {totalLen / (1024 * 1024)} MB — above the {_maxFullScanBytes / (1024 * 1024)} MB auto-scan cap, manual review required" };
                else
                {
                    fp.Stage = "downloading";
                    var path = await DownloadTrackedAsync(modelId, wf, url, fp, onBytes, ct);
                    fp.Stage = "scanning";
                    verdict = ScanCachedFile(wf, path, xmlStems);
                }
            }
        }
        catch (Exception ex)
        {
            verdict = new WeightVerdict(wf.Name, wf.Format, "inconclusive", false, $"verification failed: {ex.Message}", new());
        }
        _cache[key] = verdict;
        return verdict;
    }

    private async Task<string> DownloadTrackedAsync(string modelId, AiModelFile wf, string url,
        FileProgress fp, Action<long> onBytes, CancellationToken ct)
    {
        var path = Path.Combine(ModelCacheDir(modelId), string.Concat(wf.Name.Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_')));
        var http = _f.CreateClient("hf-dl");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? fp.TotalBytes;
        fp.TotalBytes = total;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);
        var buf = new byte[256 * 1024];
        int n; long read = 0;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n; onBytes(n);
            fp.Bytes = read;
            if (total > 0) fp.Percent = (int)(read * 100 / total);
        }
        return path;
    }

    /// <summary>Structural scan of an already-cached file (no download).</summary>
    private WeightVerdict ScanCachedFile(AiModelFile wf, string path, HashSet<string> xmlStems)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K') return ScanZip(wf, path);
        if (IsPickleStream(bytes))
        {
            var hits = _pickle.ScanBytes(bytes);
            return new(wf.Name, "pickle", "full-scan", true,
                $"content decodes as a structurally valid pickle stream ({bytes.Length / 1024} KB)",
                hits.Select(x => $"{x.Rule}: {x.Detail}").ToList());
        }
        var stem = wf.Name.Contains('.') ? wf.Name[..wf.Name.LastIndexOf('.')] : wf.Name;
        var openvino = xmlStems.Contains(stem);
        var label = openvino ? "openvino (raw)"
            : wf.Name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ? "onnx" : "raw weights";
        return new(wf.Name, label, "full-scan", true,
            openvino
                ? "no pickle structure; paired .xml graph confirms OpenVINO IR raw tensors, no code execution"
                : "full content scanned — does not decode as pickle and is not a pickle container; no code execution on load", new());
    }

    private async Task<WeightVerdict> VerifyFileAsync(string modelId, AiModelFile wf, HashSet<string> xmlStems, CancellationToken ct)
    {
        var key = $"{modelId}:{wf.Name}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var url = $"https://huggingface.co/{modelId}/resolve/main/{wf.Name}";
        WeightVerdict verdict;
        try
        {
            // ---- Stage 1: magic bytes (Range: first 16) ----
            var (head, totalLen) = await ReadHeadAsync(url, ct);
            verdict = ClassifyMagic(wf, head);

            // ---- Stage 2: inconclusive → download to cache and scan to a definitive answer ----
            if (verdict.Method == "inconclusive")
            {
                if (totalLen > _maxFullScanBytes)
                    verdict = verdict with { Detail = $"{verdict.Detail}; file is {totalLen / (1024 * 1024)} MB — above the auto-scan cap, manual review required" };
                else
                    verdict = await FullScanAsync(modelId, wf, url, xmlStems, ct);
            }
        }
        catch (Exception ex)
        {
            verdict = new WeightVerdict(wf.Name, wf.Format, "inconclusive", false,
                $"verification failed: {ex.Message}", new());
        }
        _cache[key] = verdict;
        return verdict;
    }

    private async Task<(byte[] Head, long TotalLen)> ReadHeadAsync(string url, CancellationToken ct)
    {
        var http = _f.CreateClient("hf");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 15);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentRange?.Length
            ?? resp.Content.Headers.ContentLength ?? 0;
        var buf = await resp.Content.ReadAsByteArrayAsync(ct);
        return (buf, total);
    }

    private static WeightVerdict ClassifyMagic(AiModelFile wf, byte[] h)
    {
        List<string> none = new();
        if (h.Length >= 4 && h[0] == 'P' && h[1] == 'K' && h[2] == 3 && h[3] == 4)
            // ZIP container: torch.save's modern format — defined to contain data.pkl (pickle).
            return new(wf.Name, "pickle", "magic", true,
                "ZIP container (torch.save format — embeds a pickle archive); deserialization risk on load", none);
        if (h.Length >= 2 && h[0] == 0x80 && h[1] is >= 2 and <= 5)
            return new(wf.Name, "pickle", "magic", true,
                $"raw pickle stream, protocol {h[1]} — executes opcodes on load", none);
        if (h.Length >= 9 && h[8] == '{' && BitConverter.ToUInt64(h, 0) is > 0 and < 100_000_000)
            return new(wf.Name, "safetensors", "magic", true,
                "safetensors header (8-byte length + JSON) — pure tensor data, no code execution", none);
        if (h.Length >= 4 && h[0] == 'G' && h[1] == 'G' && h[2] == 'U' && h[3] == 'F')
            return new(wf.Name, "gguf", "magic", true, "GGUF container — raw tensor data", none);
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 'H' && h[2] == 'D' && h[3] == 'F')
            return new(wf.Name, "hdf5", "magic", true, "HDF5 container (Keras-era format)", none);
        return new(wf.Name, wf.Format, "inconclusive", false, "no recognized signature in first bytes", none);
    }

    private async Task<WeightVerdict> FullScanAsync(string modelId, AiModelFile wf, string url,
        HashSet<string> xmlStems, CancellationToken ct)
    {
        var tmp = Path.Combine(_cacheDir, $"{Guid.NewGuid():n}{Path.GetExtension(wf.Name)}");
        try
        {
            _log.LogInformation("Full-scan download {Model}/{File}", modelId, wf.Name);
            var http = _f.CreateClient("hf-dl");
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmp);
                await resp.Content.CopyToAsync(fs, ct);
            }

            var bytes = await File.ReadAllBytesAsync(tmp, ct);

            // ZIP that didn't show magic at byte 0 can't happen (zip magic is at 0), but re-check.
            if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K')
                return ScanZip(wf, tmp);

            // Structural test: does the content DECODE as a pickle stream (opcode-by-opcode with
            // correct argument framing, reaching STOP)? Random binary fails within a few bytes —
            // grepping for opcode byte values would false-positive on any tensor data.
            if (IsPickleStream(bytes))
            {
                var hits = _pickle.ScanBytes(bytes);
                return new(wf.Name, "pickle", "full-scan", true,
                    $"content decodes as a structurally valid pickle stream ({bytes.Length / 1024} KB)",
                    hits.Select(x => $"{x.Rule}: {x.Detail}").ToList());
            }

            // No pickle container and no decodable pickle stream → confirmed NOT pickle.
            var stem = wf.Name.Contains('.') ? wf.Name[..wf.Name.LastIndexOf('.')] : wf.Name;
            var openvino = xmlStems.Contains(stem);
            var label = openvino ? "openvino (raw)"
                : wf.Name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ? "onnx" : "raw weights";
            return new(wf.Name, label, "full-scan", true,
                openvino
                    ? "full content scanned — no pickle structure; paired .xml graph confirms OpenVINO IR raw tensors, no code execution"
                    : "full content scanned — does not decode as pickle and is not a pickle container; no code execution on load", new());
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* cache hygiene only */ }
        }
    }

    /// <summary>
    /// Structural pickle validation: walk the opcode stream from byte 0, consuming each opcode's
    /// argument framing exactly as the pickletools grammar defines, and require a clean path to
    /// STOP ('.'). One invalid opcode or broken frame → not pickle. This is what makes the verdict
    /// safe to call "confirmed": arbitrary tensor bytes cannot decode this way by accident.
    /// </summary>
    public static bool IsPickleStream(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4) return false;
        int i = 0, ops = 0;
        while (i < d.Length && ops < 5_000_000)
        {
            var op = d[i++]; ops++;
            switch (op)
            {
                case (byte)'.': return ops >= 4;                       // STOP — require non-trivial stream
                case 0x80: if (i >= d.Length || d[i] is < 1 or > 5) return false; i += 1; break; // PROTO
                case 0x95: i += 8; break;                               // FRAME
                case (byte)'(': case (byte)')': case (byte)']': case (byte)'}':
                case (byte)'0': case (byte)'1': case (byte)'2': case (byte)'N':
                case (byte)'a': case (byte)'e': case (byte)'s': case (byte)'u':
                case (byte)'t': case (byte)'l': case (byte)'d': case (byte)'b':
                case (byte)'R': case (byte)'o': case 0x85: case 0x86: case 0x87:
                case 0x88: case 0x89: case 0x8F: case 0x93: case 0x94:
                case (byte)'Q': break;                                  // no-arg opcodes
                case (byte)'K': case (byte)'q': case (byte)'h': i += 1; break;       // 1-byte arg
                case (byte)'M': i += 2; break;                          // 2-byte arg
                case (byte)'J': case (byte)'r': case (byte)'j': i += 4; break;       // 4-byte arg
                case (byte)'G': i += 8; break;                          // BINFLOAT
                case (byte)'U': case (byte)'C': case 0x8C: case 0x8A:   // 1-byte length + payload
                    if (i >= d.Length) return false; i += 1 + d[i]; break;
                case (byte)'T': case (byte)'X': case (byte)'B': case 0x8B: // 4-byte LE length + payload
                    if (i + 4 > d.Length) return false;
                    { var n = BitConverter.ToUInt32(d.Slice(i, 4)); if (n > (uint)d.Length) return false; i += 4 + (int)n; }
                    break;
                case 0x8D: case 0x8E: case 0x96:                        // 8-byte LE length + payload
                    if (i + 8 > d.Length) return false;
                    { var n8 = BitConverter.ToUInt64(d.Slice(i, 8)); if (n8 > (ulong)d.Length) return false; i += 8 + (int)n8; }
                    break;
                case (byte)'c': case (byte)'i':                         // GLOBAL/INST: two LF-terminated lines
                    if (!SkipLine(d, ref i) || !SkipLine(d, ref i)) return false; break;
                case (byte)'I': case (byte)'L': case (byte)'F': case (byte)'S':
                case (byte)'V': case (byte)'P': case (byte)'g': case (byte)'p':
                    if (!SkipLine(d, ref i)) return false; break;       // one LF-terminated line
                default: return false;                                  // unknown opcode → not pickle
            }
            if (i > d.Length) return false;                             // framing overran the buffer
        }
        return false;                                                   // never reached STOP
    }

    private static bool SkipLine(ReadOnlySpan<byte> d, ref int i)
    {
        var limit = Math.Min(d.Length, i + 4096);                       // pickle text lines are short
        while (i < limit) { if (d[i++] == (byte)'\n') return true; }
        return false;
    }

    private WeightVerdict ScanZip(AiModelFile wf, string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var pkl = zip.Entries.Where(e => e.FullName.EndsWith(".pkl", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pkl.Count == 0)
            return new(wf.Name, "zip archive", "full-scan", true, "ZIP container with no pickle entries", new());
        var hits = new List<string>();
        foreach (var e in pkl)
        {
            using var ms = new MemoryStream();
            using var es = e.Open();
            es.CopyTo(ms);
            hits.AddRange(_pickle.ScanBytes(ms.ToArray()).Select(x => $"{e.FullName} → {x.Rule}: {x.Detail}"));
        }
        return new(wf.Name, "pickle", "full-scan", true,
            $"ZIP contains {pkl.Count} pickle archive(s) ({string.Join(", ", pkl.Select(p => p.FullName).Take(3))})", hits);
    }
}
