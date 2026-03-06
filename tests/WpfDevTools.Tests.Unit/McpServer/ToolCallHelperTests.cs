using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Tests for ToolCallHelper - the bridge between MCP SDK tool methods
/// and existing tool ExecuteAsync implementations.
/// </summary>
public class ToolCallHelperTests
{
    // === BuildJsonArgs Tests ===

    [Fact]
    public void BuildJsonArgs_WithNoParameters_ShouldReturnNull()
    {
        var result = ToolCallHelper.BuildJsonArgs();

        result.Should().BeNull();
    }

    [Fact]
    public void BuildJsonArgs_WithAllNullValues_ShouldReturnNull()
    {
        var result = ToolCallHelper.BuildJsonArgs(
            ("processId", null),
            ("elementId", null));

        result.Should().BeNull();
    }

    [Fact]
    public void BuildJsonArgs_WithSingleParameter_ShouldReturnJsonElement()
    {
        var result = ToolCallHelper.BuildJsonArgs(("processId", 12345));

        result.Should().NotBeNull();
        result!.Value.TryGetProperty("processId", out var pid).Should().BeTrue();
        pid.GetInt32().Should().Be(12345);
    }

    [Fact]
    public void BuildJsonArgs_WithMultipleParameters_ShouldIncludeAll()
    {
        var result = ToolCallHelper.BuildJsonArgs(
            ("processId", 12345),
            ("elementId", "Button_1"),
            ("depth", 5));

        result.Should().NotBeNull();
        var json = result!.Value;
        json.TryGetProperty("processId", out var pid).Should().BeTrue();
        pid.GetInt32().Should().Be(12345);
        json.TryGetProperty("elementId", out var eid).Should().BeTrue();
        eid.GetString().Should().Be("Button_1");
        json.TryGetProperty("depth", out var depth).Should().BeTrue();
        depth.GetInt32().Should().Be(5);
    }

    [Fact]
    public void BuildJsonArgs_WithMixedNullAndNonNull_ShouldExcludeNulls()
    {
        var result = ToolCallHelper.BuildJsonArgs(
            ("processId", 12345),
            ("elementId", null),
            ("depth", 3));

        result.Should().NotBeNull();
        var json = result!.Value;
        json.TryGetProperty("processId", out _).Should().BeTrue();
        json.TryGetProperty("elementId", out _).Should().BeFalse();
        json.TryGetProperty("depth", out _).Should().BeTrue();
    }

    [Fact]
    public void BuildJsonArgs_WithStringParameter_ShouldSerializeCorrectly()
    {
        var result = ToolCallHelper.BuildJsonArgs(("nameFilter", "TestApp"));

        result.Should().NotBeNull();
        result!.Value.TryGetProperty("nameFilter", out var nf).Should().BeTrue();
        nf.GetString().Should().Be("TestApp");
    }

    [Fact]
    public void BuildJsonArgs_WithBooleanFalse_ShouldIncludeIt()
    {
        var result = ToolCallHelper.BuildJsonArgs(("recursive", false));

        result.Should().NotBeNull();
        result!.Value.TryGetProperty("recursive", out var val).Should().BeTrue();
        val.GetBoolean().Should().BeFalse();
    }

    // === ExecuteAndWrapAsync Tests ===

    [Fact]
    public async Task ExecuteAndWrapAsync_WithSuccessResult_ShouldReturnNonErrorResult()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true, message = "OK" }),
            null,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithErrorResult_ShouldSetIsErrorTrue()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = false, error = "Not connected" }),
            null,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
        result.Content.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithArgs_ShouldPassArgsToExecute()
    {
        JsonElement? receivedArgs = null;

        var args = ToolCallHelper.BuildJsonArgs(("processId", 999));
        await ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) =>
            {
                receivedArgs = a;
                return Task.FromResult<object>(new { success = true });
            },
            args,
            CancellationToken.None);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.Value.TryGetProperty("processId", out var pid).Should().BeTrue();
        pid.GetInt32().Should().Be(999);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldSerializeResultAsJson()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) => Task.FromResult<object>(new { success = true, count = 42 }),
            null,
            CancellationToken.None);

        var textContent = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        textContent.Should().NotBeNull();
        textContent!.Text.Should().Contain("\"success\"");
        textContent.Text.Should().Contain("\"count\"");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldRespectCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => ToolCallHelper.ExecuteAndWrapAsync(
            (args, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<object>(new { success = true });
            },
            null,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenToolExceedsTimeout_ShouldReturnTimeoutError()
    {
        // Arrange: Create a tool that takes 10 seconds (exceeds 5s timeout)
        Func<JsonElement?, CancellationToken, Task<object>> slowTool = async (args, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new { success = true };
        };

        // Act: Execute with no external cancellation
        var result = await ToolCallHelper.ExecuteAndWrapAsync(slowTool, null, CancellationToken.None);

        // Assert: Should return timeout error
        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
        var textContent = result.Content[0] as ModelContextProtocol.Protocol.TextContentBlock;
        textContent.Should().NotBeNull();
        textContent!.Text.Should().Contain("timed out");
        textContent.Text.Should().Contain("5 seconds");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenExternalCancellation_ShouldPropagateCorrectly()
    {
        // Arrange: Create a tool that respects cancellation
        Func<JsonElement?, CancellationToken, Task<object>> cancellableTool = async (args, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new { success = true };
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act & Assert: Should throw OperationCanceledException (not return timeout error)
        var act = () => ToolCallHelper.ExecuteAndWrapAsync(cancellableTool, null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // === IsToolResultError Tests ===

    [Fact]
    public void IsToolResultError_JsonElement_WithSuccessFalse_ShouldReturnTrue()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"success":false,"error":"fail"}""");
        ToolCallHelper.IsToolResultError(element).Should().BeTrue();
    }

    [Fact]
    public void IsToolResultError_JsonElement_WithSuccessTrue_ShouldReturnFalse()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"success":true}""");
        ToolCallHelper.IsToolResultError(element).Should().BeFalse();
    }

    [Fact]
    public void IsToolResultError_JsonElement_WithNoSuccessField_ShouldReturnFalse()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""{"data":"test"}""");
        ToolCallHelper.IsToolResultError(element).Should().BeFalse();
    }

    [Fact]
    public void IsToolResultError_JsonElement_WithNonObjectKind_ShouldReturnFalse()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("""[1,2,3]""");
        ToolCallHelper.IsToolResultError(element).Should().BeFalse();
    }
}
