using Advisory.Api.Research;
using Xunit;

namespace Advisory.Tests;

/// <summary>
/// Pins how the Groq chat-completions response is interpreted. Reasoning models (gpt-oss-120b)
/// can spend their whole completion budget on hidden reasoning and return EMPTY content with
/// finish_reason="length". That must surface as a clear failure, not a silent empty answer (#157).
/// </summary>
public class GroqResponseTests
{
    [Fact]
    public void Normal_completion_returns_the_content()
    {
        var raw = """
        {"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"[{\"name\":\"block-critical\"}]"}}]}
        """;
        var (ok, text) = GroqClient.ParseChatResponse(raw);
        Assert.True(ok);
        Assert.Contains("block-critical", text);
    }

    [Fact]
    public void Empty_content_truncated_by_length_is_a_clear_failure_not_silent_empty()
    {
        // The exact failure from #157: model used all tokens reasoning, emitted nothing.
        var raw = """
        {"choices":[{"finish_reason":"length","message":{"role":"assistant","content":""}}]}
        """;
        var (ok, text) = GroqClient.ParseChatResponse(raw);
        Assert.False(ok);
        Assert.Contains("ran out of room", text);
    }

    [Fact]
    public void Empty_content_with_normal_stop_is_just_empty_text_still_ok()
    {
        // Empty but NOT length-truncated (e.g. model legitimately had nothing) — caller decides.
        var raw = """
        {"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":""}}]}
        """;
        var (ok, text) = GroqClient.ParseChatResponse(raw);
        Assert.True(ok);
        Assert.Equal("", text);
    }
}
