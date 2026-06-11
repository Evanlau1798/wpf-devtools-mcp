using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public partial class ToolCallHelperTests
{
    // === Negative Test Cases (Edge Cases) ===

    [Fact]
    public async Task ExecuteAndWrapAsync_WithExtremelyLargeResult_ShouldSerializeSuccessfully()
    {
        // Arrange: Create a result with 10,000 items
        var largeArray = Enumerable.Range(0, 10000).Select(i => new { id = i, name = $"Item_{i}" }).ToArray();

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true, items = largeArray }),
            null,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsError.Should().BeFalse();
        var textContent = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        textContent!.Text.Should().Contain("\"hasStructuredContent\":true");
        textContent.Text.Should().Contain("\"itemsCount\":10000");
        textContent.Text.Should().NotContain("Item_9999");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithNullResult_ShouldHandleGracefully()
    {
        // Arrange: Tool returns null (edge case)
        Func<JsonElement?, CancellationToken, Task<object>> nullTool = (args, ct) =>
            Task.FromResult<object>(null!);

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(nullTool, null, CancellationToken.None);

        // Assert: Should serialize null as "null"
        result.Should().NotBeNull();
        var textContent = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        textContent!.Text.Should().Be("null");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithSpecialCharactersInResult_ShouldEscapeCorrectly()
    {
        // Arrange: Result with special characters that need JSON escaping
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new
            {
                success = true,
                message = "Line1\nLine2\tTabbed\"Quoted\\"
            }),
            null,
            CancellationToken.None);

        // Assert: Structured content should preserve the original string even when text content is compacted
        result.Should().NotBeNull();
        result.StructuredContent!.Value.GetProperty("message").GetString().Should().Be("Line1\nLine2\tTabbed\"Quoted\\");
    }

    [Fact]
    public void BuildJsonArgs_WithExtremelyLargeParameterCount_ShouldHandleAll()
    {
        // Arrange: 50 parameters (edge case for parameter handling)
        var parameters = Enumerable.Range(0, 50)
            .Select(i => ($"param{i}", (object?)i))
            .ToArray();

        // Act
        var result = ToolCallHelper.BuildJsonArgs(parameters);

        // Assert: All parameters should be included
        result.Should().NotBeNull();
        for (int i = 0; i < 50; i++)
        {
            result!.Value.TryGetProperty($"param{i}", out var prop).Should().BeTrue();
            prop.GetInt32().Should().Be(i);
        }
    }

    [Fact]
    public void BuildJsonArgs_WithUnicodeCharacters_ShouldPreserveCorrectly()
    {
        // Arrange: Unicode characters (emoji, Chinese, etc.)
        var result = ToolCallHelper.BuildJsonArgs(
            ("emoji", "\uD83C\uDF89\uD83D\uDE80"),
            ("chinese", "\u6E2C\u8A66"),
            ("arabic", "\u0627\u062E\u062A\u0628\u0627\u0631"));

        // Assert: Unicode should be preserved
        result.Should().NotBeNull();
        result!.Value.TryGetProperty("emoji", out var emoji).Should().BeTrue();
        emoji.GetString().Should().Be("\uD83C\uDF89\uD83D\uDE80");
        result.Value.TryGetProperty("chinese", out var chinese).Should().BeTrue();
        chinese.GetString().Should().Be("\u6E2C\u8A66");
        result.Value.TryGetProperty("arabic", out var arabic).Should().BeTrue();
        arabic.GetString().Should().Be("\u0627\u062E\u062A\u0628\u0627\u0631");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithVeryShortTimeout_ShouldTimeoutQuickly()
    {
        // This test verifies that the timeout mechanism works correctly
        // even for operations that would normally complete quickly
        Func<JsonElement?, CancellationToken, Task<object>> slowTool = async (args, ct) =>
        {
            // Never complete naturally; the test is asserting timeout handling.
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new { success = true };
        };

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(slowTool, null, CancellationToken.None, timeoutSeconds: 1);

        // Assert: Should timeout
        result.IsError.Should().BeTrue();
        var textContent = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        textContent!.Text.Should().Contain("timed out");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithCustomTimeoutOverride_ShouldAllowLongerRunningTool()
    {
        Func<JsonElement?, CancellationToken, Task<object>> slowButValidTool = async (args, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1200), ct);
            return new { success = true, message = "completed" };
        };

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            slowButValidTool,
            null,
            CancellationToken.None,
            timeoutSeconds: 2);

        result.IsError.Should().BeFalse();
        var textContent = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        textContent!.Text.Should().Contain("completed");
    }
}
