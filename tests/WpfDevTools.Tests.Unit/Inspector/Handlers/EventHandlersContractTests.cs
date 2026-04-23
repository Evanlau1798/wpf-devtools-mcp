using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Mcp.Server.McpTools;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Host.Handlers;

/// <summary>
/// Contract regression tests ensuring response shapes for event-related
/// operations remain stable across refactors.
/// </summary>
[Collection("ToolCallHelperState")]
public sealed class EventHandlersContractTests : IDisposable
{
    public void Dispose()
    {
        ToolCallHelper.ResetCacheForTesting();
    }

    [StaFact]
    public void FireRoutedEvent_ClickOnButtonWithCommand_ShouldReturnUsedOnClickFlag()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var commandExecuted = false;
        var button = new System.Windows.Controls.Button
        {
            Command = new TestRelayCommand(() => commandExecuted = true)
        };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.FireRoutedEvent(elementId, "Click", null);
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        // Assert
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("usedOnClick").GetBoolean().Should().BeTrue(
            "fire_routed_event('Click') on ButtonBase must include usedOnClick: true");
        commandExecuted.Should().BeTrue(
            "OnClick() path should execute the attached ICommand");
    }

    [StaFact]
    public void FireRoutedEvent_NonClickEvent_ShouldNotReturnUsedOnClickFlag()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.FireRoutedEvent(elementId, "LostFocus", null);
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        // Assert
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.TryGetProperty("usedOnClick", out _).Should().BeFalse(
            "Non-Click events must not include usedOnClick in the response");
    }

    [Fact]
    public async Task FireRoutedEvent_Navigation_ShouldRecommendSafeVerificationPath()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                eventName = "Click",
                message = "Invoked OnClick path",
                usedOnClick = true
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "SaveButton"), ("eventName", "Click")),
            CancellationToken.None,
            toolName: "fire_routed_event");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_ui_summary");
        nextSteps.EnumerateArray().Select(item => item.GetProperty("tool").GetString()).Should().NotContain("trace_routed_events");
    }

    [Fact]
    public async Task GetEventHandlers_WithIncompleteClickInspection_ShouldRecommendCommandFallback()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                elementId = "SaveButton",
                eventName = "Click",
                handlerCount = 0,
                mayBeIncomplete = true,
                handlers = Array.Empty<object>()
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "SaveButton"), ("eventName", "Click")),
            CancellationToken.None,
            toolName: "get_event_handlers");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_commands");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("SaveButton");
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_ZeroEventPayload_ShouldIncludeStableDiagnosticsReasonCode()
    {
        var finder = new ElementFinder();
        var handler = new EventHandlers(new EventAnalyzer(finder));
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var startResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 100,
                allowShortStartDuration = true
            }),
            CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        await Task.Delay(180);

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);
        var payload = JsonSerializer.SerializeToElement(getResult);

        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().BeOneOf(
            "captureWindowTooShort",
            "eventNotRaised",
            "filterMismatch",
            "cleanupFailed");
    }

    [Fact]
    public async Task DrainEvents_WhenPostDrainCleanupReportsFailure_ShouldPreservePayloadAndSurfaceCleanupIncomplete()
    {
        var handler = new EventHandlers(
            new EventAnalyzer(new ElementFinder()),
            () => new InvalidOperationException("cleanup failed"));

        var result = await handler.HandleAsync(
            "drain_events",
            JsonSerializer.SerializeToElement(new { maxEvents = 5 }),
            CancellationToken.None);
        var payload = JsonSerializer.SerializeToElement(result);

        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("pendingEventCount").GetInt32().Should().Be(0);
        payload.GetProperty("droppedEventCount").GetInt32().Should().Be(0);
        payload.GetProperty("cleanupIncomplete").GetBoolean().Should().BeTrue();
        payload.GetProperty("cleanupFailureType").GetString().Should().Be(nameof(InvalidOperationException));
        payload.GetProperty("cleanupFailureMessage").GetString().Should().Be("cleanup failed");
    }

    private sealed class TestRelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public TestRelayCommand(Action execute) => _execute = execute;
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
