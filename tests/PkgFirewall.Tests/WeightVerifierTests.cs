using System.Text;
using PkgFirewall.Api.Catalog;
using Xunit;

namespace PkgFirewall.Tests;

/// <summary>
/// The structural pickle walker is what lets the AI Catalog say "confirmed" instead of guessing
/// from file extensions. These tests pin both directions: real pickle streams decode, and binary
/// tensor-like data (the ONNX/OpenVINO false-positive case) does not.
/// </summary>
public class WeightVerifierTests
{
    [Fact]
    public void Protocol2_pickle_is_recognized()
    {
        // python: pickletools.dis(pickle.dumps({}, protocol=2)) → PROTO 2, EMPTY_DICT, BINPUT 0, STOP
        var p = new byte[] { 0x80, 0x02, (byte)'}', (byte)'q', 0x00, (byte)'.' };
        Assert.True(WeightVerifier.IsPickleStream(p));
    }

    [Fact]
    public void Protocol4_pickle_with_frame_is_recognized()
    {
        // PROTO 4, FRAME(len), SHORT_BINUNICODE "hi", MEMOIZE, STOP
        var p = new byte[] { 0x80, 0x04, 0x95, 7, 0, 0, 0, 0, 0, 0, 0, 0x8C, 2, (byte)'h', (byte)'i', 0x94, (byte)'.' };
        Assert.True(WeightVerifier.IsPickleStream(p));
    }

    [Fact]
    public void Protocol0_text_pickle_is_recognized()
    {
        // python: pickle.dumps({"a": 1}, protocol=0) → "(dp0\nS'a'\np1\nI1\ns."
        var p = Encoding.ASCII.GetBytes("(dp0\nS'a'\np1\nI1\ns.");
        Assert.True(WeightVerifier.IsPickleStream(p));
    }

    [Fact]
    public void Onnx_protobuf_header_is_not_pickle()
    {
        // Typical ONNX start: protobuf field tags — 0x08 is not a pickle opcode.
        var onnx = new byte[] { 0x08, 0x07, 0x12, 0x08, 0x73, 0x6B, 0x6C, 0x32, 0x6F, 0x6E, 0x6E, 0x78 };
        Assert.False(WeightVerifier.IsPickleStream(onnx));
    }

    [Fact]
    public void Random_tensor_bytes_are_not_pickle()
    {
        // Deterministic pseudo-random buffer standing in for raw float32 tensor data
        // (the OpenVINO .bin false-positive case). Opcode-byte grepping flags this; the
        // structural walk must not.
        var data = new byte[64 * 1024];
        uint seed = 0xC0FFEE42;
        for (int i = 0; i < data.Length; i++) { seed = seed * 1664525 + 1013904223; data[i] = (byte)(seed >> 24); }
        Assert.False(WeightVerifier.IsPickleStream(data));
    }

    [Fact]
    public void Truncated_pickle_without_stop_is_rejected()
    {
        var p = new byte[] { 0x80, 0x02, (byte)'}', (byte)'q', 0x00 }; // missing STOP
        Assert.False(WeightVerifier.IsPickleStream(p));
    }

    [Fact]
    public void Empty_and_tiny_buffers_are_rejected()
    {
        Assert.False(WeightVerifier.IsPickleStream(Array.Empty<byte>()));
        Assert.False(WeightVerifier.IsPickleStream(new byte[] { (byte)'.', 0, 0 }));
    }
}
