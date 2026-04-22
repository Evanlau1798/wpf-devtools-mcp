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
                status = "Connected",
                processId = 12345,
                count = 42,
                items = Enumerable.Range(0, 5).Select(i => $"item-{i}").ToArray()
            }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);

        textPayload.GetProperty("success").GetBoolean().Should().BeTrue();
        textPayload.GetProperty("status").GetString().Should().Be("Connected");
        textPayload.GetProperty("processId").GetInt64().Should().Be(12345);
        textPayload.GetProperty("count").GetInt64().Should().Be(42);
        textPayload.GetProperty("itemsCount").GetInt64().Should().Be(5);
        textPayload.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
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
                retryAfterSeconds = 3,
                requiresReconnect = true,
                details = new { processId = 12345 }
            }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);

        textPayload.GetProperty("success").GetBoolean().Should().BeFalse();
        textPayload.GetProperty("error").GetString().Should().Be("Named pipe not connected");
        textPayload.GetProperty("errorCode").GetString().Should().Be("NotConnected");
        textPayload.GetProperty("retryAfterSeconds").GetInt64().Should().Be(3);
        textPayload.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
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

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldPreferExistingScalarCountsBeforeSyntheticCollectionCounts()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                eventCount = 2,
                handlerInvocationCount = 5,
                status = "Captured",
                processId = 12345,
                traceId = "trace-1",
                events = new[] { "Click", "Loaded" }
            }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);

        textPayload.GetProperty("eventCount").GetInt64().Should().Be(2);
        textPayload.GetProperty("handlerInvocationCount").GetInt64().Should().Be(5);
        textPayload.GetProperty("traceId").GetString().Should().Be("trace-1");
        textPayload.TryGetProperty("eventsCount", out _).Should().BeFalse(
            "existing scalar counts should win before synthetic collection summaries are added");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldKeepFallbackCompactWhenPayloadContainsNavigationAndLargeFields()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                status = "Ready",
                screenshotId = "shot-1",
                base64Image = new string('A', 512),
                nextSteps = new[] { new { tool = "get_bindings" } },
                navigation = new { recommended = new[] { new { tool = "get_bindings" } } }
            }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);

        textPayload.GetProperty("status").GetString().Should().Be("Ready");
        textPayload.GetProperty("screenshotId").GetString().Should().Be("shot-1");
        textPayload.TryGetProperty("nextSteps", out _).Should().BeFalse();
        textPayload.TryGetProperty("navigation", out _).Should().BeFalse();
        textPayload.TryGetProperty("base64Image", out _).Should().BeFalse();
    }
}
