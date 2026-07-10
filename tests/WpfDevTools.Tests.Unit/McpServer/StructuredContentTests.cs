using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Tests for structured content capabilities in ToolCallHelper.
/// Validates that tool results leverage the MCP SDK's StructuredContent
/// and Annotations properties for improved AI-client discoverability.
/// </summary>
[Collection("ToolCallHelperState")]
public class StructuredContentTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_SuccessResult_ShouldPopulateStructuredContent()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true, count = 42 }),
            null,
            CancellationToken.None);

        result.StructuredContent.Should().NotBeNull(
            "successful tool results should populate StructuredContent for machine-readable parsing");

        var structured = result.StructuredContent!.Value;
        structured.TryGetProperty("success", out var successProp).Should().BeTrue();
        successProp.GetBoolean().Should().BeTrue();
        structured.TryGetProperty("count", out var countProp).Should().BeTrue();
        countProp.GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ErrorResult_ShouldPopulateStructuredContent()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = false, error = "Not connected" }),
            null,
            CancellationToken.None);

        result.StructuredContent.Should().NotBeNull(
            "error tool results should also populate StructuredContent for structured error handling");

        var structured = result.StructuredContent!.Value;
        structured.TryGetProperty("success", out var successProp).Should().BeTrue();
        successProp.GetBoolean().Should().BeFalse();
        structured.TryGetProperty("error", out var errorProp).Should().BeTrue();
        errorProp.GetString().Should().Be("Not connected");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ErrorResult_ContentShouldHaveAnnotations()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = false, error = "Rate limit exceeded" }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0] as TextContentBlock;
        textBlock.Should().NotBeNull();
        textBlock!.Annotations.Should().NotBeNull(
            "error responses should include Annotations for AI-client priority hints");
        textBlock.Annotations!.Priority.Should().Be(1.0f,
            "errors should have highest priority to ensure they are surfaced to the user");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_SuccessResult_ContentShouldNotHaveAnnotations()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0] as TextContentBlock;
        textBlock.Should().NotBeNull();
        textBlock!.Annotations.Should().BeNull(
            "successful responses should not add unnecessary annotations overhead");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ExceptionError_ShouldPopulateStructuredContent()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => throw new InvalidOperationException("boom"),
            null,
            CancellationToken.None);

        result.StructuredContent.Should().NotBeNull(
            "exception errors should populate StructuredContent");
        var structured = result.StructuredContent!.Value;
        structured.TryGetProperty("success", out var successProp).Should().BeTrue();
        successProp.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_StructuredContentAndTextContent_ShouldUseCompactFallbackSummary()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true, data = "hello" }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0] as TextContentBlock;
        var textJson = JsonSerializer.Deserialize<JsonElement>(textBlock!.Text);
        var structured = result.StructuredContent!.Value;

        textJson.GetProperty("success").GetBoolean().Should().Be(structured.GetProperty("success").GetBoolean());
        textJson.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
        textJson.TryGetProperty("data", out _).Should().BeFalse();
    }
}
