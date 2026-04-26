using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class ToolCallHelperTextFallbackTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
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
    public async Task ExecuteAndWrapAsync_WithFullTextFallbackMode_ShouldEmitFullJsonText()
    {
        using var fallbackScope = EnvironmentVariableScope.Set("WPFDEVTOOLS_TEXT_FALLBACK_MODE", "full");
        var payload = new
        {
            success = true,
            status = "Ready",
            details = Enumerable.Range(0, 4).Select(index => new { index }).ToArray()
        };

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(payload),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);

        textPayload.GetProperty("details").GetArrayLength().Should().Be(4);
        textBlock.Text.Should().Be(result.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithMinimalObjectPayload_ShouldExplainStructuredContentAsCanonicalPayload()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true
            }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);

        textPayload.GetProperty("message").GetString().Should().Be(
            "Canonical payload available in structuredContent; content[0].text is a compact fallback.");
        textPayload.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
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

    [Fact]
    public async Task ExecuteAndWrapAsync_WithNestedRecoveryOnlyErrorPayload_ShouldProjectRecoveryFieldsIntoTextFallback()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = false,
                error = "Rate limit exceeded",
                errorCode = "RateLimitExceeded",
                recovery = new
                {
                    suggestedAction = "Wait and retry the same tool.",
                    retryAfterSeconds = 3,
                    availableTokens = 0,
                    requiresReconnect = false
                }
            }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);
        var structured = result.StructuredContent!.Value;

        textPayload.GetProperty("error").GetString().Should().Be("Rate limit exceeded");
        textPayload.GetProperty("suggestedAction").GetString().Should().Be("Wait and retry the same tool.");
        textPayload.GetProperty("retryAfterSeconds").GetInt64().Should().Be(3);
        textPayload.GetProperty("availableTokens").GetInt64().Should().Be(0);
        textPayload.GetProperty("requiresReconnect").GetBoolean().Should().BeFalse();

        structured.GetProperty("suggestedAction").GetString().Should().Be("Wait and retry the same tool.");
        structured.GetProperty("retryAfterSeconds").GetInt32().Should().Be(3);
        structured.GetProperty("availableTokens").GetInt32().Should().Be(0);
        structured.GetProperty("requiresReconnect").GetBoolean().Should().BeFalse();
        structured.GetProperty("recovery").GetProperty("suggestedAction").GetString().Should().Be("Wait and retry the same tool.");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithConflictingRecoveryAndTopLevelFields_ShouldProjectCanonicalRecoveryEverywhere()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = false,
                error = "Rate limit exceeded",
                errorCode = "RateLimitExceeded",
                suggestedAction = "Stale top-level guidance.",
                retryAfterSeconds = 9,
                recovery = new
                {
                    suggestedAction = "Wait and retry the same tool.",
                    retryAfterSeconds = 3,
                    availableTokens = 0,
                    availableEvents = new[] { "Click", "Loaded" }
                }
            }),
            null,
            CancellationToken.None);

        var textBlock = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        var textPayload = JsonSerializer.Deserialize<JsonElement>(textBlock.Text);
        var structured = result.StructuredContent!.Value;

        structured.GetProperty("suggestedAction").GetString().Should().Be("Wait and retry the same tool.");
        structured.GetProperty("retryAfterSeconds").GetInt32().Should().Be(3);
        structured.GetProperty("availableTokens").GetInt32().Should().Be(0);
        structured.GetProperty("availableEvents").EnumerateArray().Select(item => item.GetString()).Should().Contain(["Click", "Loaded"]);

        textPayload.GetProperty("suggestedAction").GetString().Should().Be("Wait and retry the same tool.");
        textPayload.GetProperty("retryAfterSeconds").GetInt64().Should().Be(3);
        textPayload.GetProperty("availableEvents").EnumerateArray().Select(item => item.GetString()).Should().Contain(["Click", "Loaded"]);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        private EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static EnvironmentVariableScope Set(string name, string value) => new(name, value);

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
