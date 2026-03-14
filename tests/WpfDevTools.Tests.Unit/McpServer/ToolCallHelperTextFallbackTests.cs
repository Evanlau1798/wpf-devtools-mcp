using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class ToolCallHelperTextFallbackTests
{
    public ToolCallHelperTextFallbackTests()
    {
        ToolCallHelper.ResetCacheForTesting();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithObjectPayload_ShouldEmitCompactTextFallbackJson()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                count = 42,
                items = Enumerable.Range(0, 5).Select(i => $"item-{i}").ToArray()
            }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);

        textPayload.GetProperty("success").GetBoolean().Should().BeTrue();
        textPayload.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
        textPayload.TryGetProperty("count", out _).Should().BeFalse();
        textPayload.TryGetProperty("items", out _).Should().BeFalse();
        textBlock.Text.Length.Should().BeLessThan(result.StructuredContent!.Value.GetRawText().Length);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithErrorPayload_ShouldKeepErrorSummaryInTextFallback()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = false,
                error = "Named pipe not connected",
                errorCode = "NotConnected",
                details = new { processId = 12345 }
            }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);

        textPayload.GetProperty("success").GetBoolean().Should().BeFalse();
        textPayload.GetProperty("error").GetString().Should().Be("Named pipe not connected");
        textPayload.GetProperty("errorCode").GetString().Should().Be("NotConnected");
        textPayload.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
        textPayload.TryGetProperty("details", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithScalarPayload_ShouldKeepRawTextContent()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>("done"),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        textBlock.Text.Should().Be("\"done\"");
    }
}
