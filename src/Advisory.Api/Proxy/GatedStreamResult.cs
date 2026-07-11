using Advisory.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Advisory.Api.Proxy;

/// <summary>
/// "Gate the completion, not the start." Streams a coordinate-cleared artifact from Nexus to pip WHILE
/// content-scanning the bytes in parallel — but WITHHOLDS THE FINAL CHUNK until the scan clears. This
/// defeats both hard constraints at once:
///   - No client timeout: bytes flow from the moment the download starts, so pip's read timeout (which
///     fires on silence between body bytes) never triggers, for ANY artifact size.
///   - No exposure: a package whose tail is withheld is UNUSABLE. Verified empirically — a wheel missing
///     its last bytes is not a valid zip (the End-of-Central-Directory record lives at the end), and a
///     truncated .tar.gz fails to decompress (incomplete gzip stream). So pip physically cannot install
///     a package until we release its final bytes.
/// Scan CLEAN → release the withheld tail → pip completes the install. Scan BAD → never send the tail +
/// abort the connection → pip is left with a truncated, uninstallable file it discards; NOTHING bad is
/// ever usable on the developer's machine. The coordinate gate (OSV/malware/KEV) already ran and blocked
/// known-bad with ZERO bytes sent; this covers the content dimension (secrets/IaC/pickle) as the
/// completion gate. On a clean verdict the package is also promoted so the next pull is an instant cache hit.
/// </summary>
public sealed class GatedStreamResult : IActionResult
{
    private readonly PackageProxyController _ctl;
    private readonly Ecosystem _eco;
    private readonly string _name, _version, _fileName, _quarantineUrl;

    // How much of the tail to withhold until the scan clears. Must be enough to invalidate the archive's
    // trailing structure (zip EOCD / gzip footer). 64 KB is comfortably more than any EOCD/footer and is
    // trivial to hold back.
    private const int WithholdTailBytes = 64 * 1024;

    public GatedStreamResult(PackageProxyController ctl, Ecosystem eco, string name, string version,
        string fileName, string quarantineUrl)
    { _ctl = ctl; _eco = eco; _name = name; _version = version; _fileName = fileName; _quarantineUrl = quarantineUrl; }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;

        using var upstream = await _ctl.NexusHttp().GetAsync(_quarantineUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!upstream.IsSuccessStatusCode) { http.Response.StatusCode = (int)upstream.StatusCode; return; }

        http.Response.StatusCode = 200;
        http.Response.ContentType = upstream.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        // Chunked (no Content-Length): we don't know the final length while gating, and we may abort.

        var src = await upstream.Content.ReadAsStreamAsync(ct);

        // Buffer the WHOLE artifact for the content scan (we need the complete bytes to scan the archive),
        // while streaming everything EXCEPT the trailing WithholdTailBytes to pip as it arrives. pip keeps
        // reading (no timeout); the withheld tail is what makes the file unusable until we clear it.
        using var full = new MemoryStream();
        var pending = new Queue<byte[]>();     // chunks not yet sent (the rolling tail we hold back)
        long pendingBytes = 0;
        var chunk = new byte[64 * 1024];
        int read;
        try
        {
            while ((read = await src.ReadAsync(chunk, ct)) > 0)
            {
                var slice = chunk.AsSpan(0, read).ToArray();
                full.Write(slice, 0, slice.Length);
                pending.Enqueue(slice);
                pendingBytes += slice.Length;
                // Flush everything beyond the tail budget — keep only ~WithholdTailBytes queued (unsent).
                while (pendingBytes - pending.Peek().Length >= WithholdTailBytes && pending.Count > 1)
                {
                    var send = pending.Dequeue();
                    pendingBytes -= send.Length;
                    await http.Response.Body.WriteAsync(send, ct);
                    await http.Response.Body.FlushAsync(ct);   // bytes flow → pip's read clock resets
                }
            }
        }
        catch (OperationCanceledException) { return; }   // pip went away

        // Download complete; the withheld tail (up to WithholdTailBytes) is still queued and NOT yet sent.
        // Now content-scan the COMPLETE bytes. Clean → release the tail (pip's file becomes valid). Bad →
        // do NOT send the tail and abort → pip is left with a truncated, uninstallable file.
        var clean = await _ctl.ContentScanAndPromoteAsync(_eco, _name, _version, _fileName, _quarantineUrl, full.ToArray());
        if (clean)
        {
            while (pending.Count > 0)
            {
                var tail = pending.Dequeue();
                await http.Response.Body.WriteAsync(tail, ct);
            }
            await http.Response.Body.FlushAsync(ct);   // final bytes → pip completes a VALID archive
        }
        else
        {
            // Withhold the tail and forcibly abort the connection so pip treats it as a broken download.
            http.Abort();
        }
    }
}
