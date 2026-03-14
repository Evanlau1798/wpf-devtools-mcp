using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Tests for ToolCallHelper - the bridge between MCP SDK tool methods
/// and existing tool ExecuteAsync implementations.
/// </summary>
[Collection("ToolCallHelperState")]
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
        result.StructuredContent.Should().NotBeNull();
        result.StructuredContent!.Value.TryGetProperty("nextSteps", out var nextSteps).Should().BeTrue();
        nextSteps.ValueKind.Should().Be(JsonValueKind.Array);
        nextSteps.GetArrayLength().Should().Be(0);
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
        result.StructuredContent.Should().NotBeNull();
        result.StructuredContent!.Value.TryGetProperty("nextSteps", out var nextSteps).Should().BeTrue();
        nextSteps.ValueKind.Should().Be(JsonValueKind.Array);
        nextSteps.GetArrayLength().Should().Be(0);
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
    public async Task ExecuteAndWrapAsync_ShouldKeepExistingResponseFields_WhenAddingNextSteps()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true, message = "OK", count = 42 }),
            null,
            CancellationToken.None);

        var structured = result.StructuredContent!.Value;
        structured.GetProperty("success").GetBoolean().Should().BeTrue();
        structured.GetProperty("message").GetString().Should().Be("OK");
        structured.GetProperty("count").GetInt32().Should().Be(42);
        structured.TryGetProperty("nextSteps", out var nextSteps).Should().BeTrue();
        nextSteps.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldDeriveNextStepsFromNavigationRecommended()
    {
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ => new ToolNavigationEnvelope(
            [
                new ToolNextStep(
                    "get_datacontext_chain",
                    ToolCallHelper.BuildJsonArgs(("elementId", "TextBox_1"))!.Value,
                    "Inspect the DataContext inheritance for the failing element.",
                    ToolNextStepKind.Diagnostic,
                    1)
            ],
            [
                new ToolNextStep(
                    "get_bindings",
                    ToolCallHelper.BuildJsonArgs(("elementId", "TextBox_1"))!.Value,
                    "Inspect the binding declaration directly.",
                    ToolNextStepKind.Diagnostic,
                    2)
            ],
            ["get_bindings"],
            [
                ToolNavigationReference.Create(
                    "binding-issue",
                    ("elementId", "TextBox_1"),
                    ("propertyName", "Text"),
                    ("diagnosis", "PathMismatch"))
            ]));
        ToolCallHelper.SetNavigationPlannerForTesting(new ToolNavigationPlanner(registry));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true, errorCount = 1 }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "known_tool");

        var structured = result.StructuredContent!.Value;
        var navigation = structured.GetProperty("navigation");
        navigation.GetProperty("recommended")[0].GetProperty("tool").GetString().Should().Be("get_datacontext_chain");
        navigation.GetProperty("alternatives")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        navigation.GetProperty("prefetchTools")[0].GetString().Should().Be("get_bindings");
        navigation.GetProperty("contextRefs")[0].GetProperty("type").GetString().Should().Be("binding-issue");
        structured.GetProperty("nextSteps")[0].GetProperty("tool").GetString().Should().Be("get_datacontext_chain");
        structured.GetProperty("nextSteps").GetRawText().Should().Be(navigation.GetProperty("recommended").GetRawText());
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldPreserveLegacyNextStepsConsumers_WhenNavigationExists()
    {
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ => new ToolNavigationEnvelope(
            [
                new ToolNextStep(
                    "get_bindings",
                    ToolCallHelper.BuildJsonArgs(("elementId", "TextBox_1"))!.Value,
                    "Inspect the binding declaration directly.",
                    ToolNextStepKind.Diagnostic,
                    1)
            ],
            [],
            [],
            []));
        ToolCallHelper.SetNavigationPlannerForTesting(new ToolNavigationPlanner(registry));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true }),
            null,
            CancellationToken.None,
            toolName: "known_tool");

        result.StructuredContent!.Value.TryGetProperty("nextSteps", out var nextSteps).Should().BeTrue();
        nextSteps.ValueKind.Should().Be(JsonValueKind.Array);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_bindings");
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
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textContent!.Text);
        textPayload.GetProperty("success").GetBoolean().Should().BeTrue();
        textPayload.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
        textPayload.TryGetProperty("count", out _).Should().BeFalse();
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
            // Delay longer than the configured timeout
            await Task.Delay(TimeSpan.FromSeconds(McpServerConfiguration.DefaultToolTimeoutSeconds + 1), ct);
            return new { success = true };
        };

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(slowTool, null, CancellationToken.None);

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

    // === Structured error contract tests ===

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenToolThrowsException_ShouldReturnErrorWithErrorCode()
    {
        // Arrange: Tool throws a non-cancellation exception
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new InvalidOperationException("Something went wrong internally");

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        // Assert: Should include errorCode for machine recovery
        result.Should().NotBeNull();
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("success").GetBoolean().Should().BeFalse();
        structured.GetProperty("errorCode").GetString().Should().Be("OperationError");
        structured.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenToolThrowsArgumentException_ShouldReturnInvalidArgumentCode()
    {
        // Arrange
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new ArgumentException("Invalid parameter value");

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenCryptographicException_ShouldReturnSecurityErrorCode()
    {
        // Arrange: Simulates signature verification failure
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new System.Security.Cryptography.CryptographicException("localized OS error text");

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        // Assert: Error code should be SecurityError, message should NOT contain raw localized text
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        structured.GetProperty("error").GetString().Should().NotContain("localized OS error text");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenUnknownException_ShouldReturnInternalErrorCode()
    {
        // Arrange: Unknown exception type should not leak raw message
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new NotSupportedException("internal implementation detail");

        // Act
        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        // Assert: Should use InternalError code and hide raw message
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("InternalError");
        structured.GetProperty("error").GetString().Should().NotContain("internal implementation detail");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenFileNotFound_ShouldReturnFileNotFoundCode()
    {
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new FileNotFoundException("secret file path info");

        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("FileNotFound");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenAccessDenied_ShouldReturnAccessDeniedCode()
    {
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new UnauthorizedAccessException("detailed access info");

        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("AccessDenied");
    }

}
