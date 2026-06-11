using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

[Collection("TimingSensitive")]
public sealed partial class EventHandlerTraceModeTests
{
    private static readonly TimeSpan DispatcherSignalTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TraceRoutedEvents_WithUppercaseGetMode_ShouldReturnGetPayload()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { mode = "GET" });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("mode").GetString().Should().Be("get");
    }

    [Fact]
    public async Task TraceRoutedEvents_WithWhitespaceWrappedGetMode_ShouldNormalizeValue()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { mode = "  GET  " });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("mode").GetString().Should().Be("get");
    }

    [Fact]
    public async Task TraceRoutedEvents_CaptureMode_WithNegativeDuration_ShouldRejectBeforeStartingTrace()
    {
        var analyzer = new EventAnalyzer(new ElementFinder());
        var handler = new EventHandlers(analyzer);

        Func<Task> act = () => handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "capture",
                eventName = "Click",
                duration = -1
            }),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("isTracing").GetBoolean().Should().BeFalse();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
    }

    [StaFact]
    public async Task TraceRoutedEvents_StartMode_WithDurationAboveMaximum_ShouldReturnCappedEffectiveDuration()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var result = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 120000
            }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("requestedDuration").GetInt32().Should().Be(120000);
        payload.GetProperty("effectiveDuration").GetInt32().Should().Be(60000);
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_WithZeroEventsWhileTracing_ShouldReturnCaptureWindowTooShortReason()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var startParams = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = 300,
            allowShortStartDuration = true
        });

        var startResult = await handler.HandleAsync("trace_routed_events", startParams, CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("captureWindowTooShort");
        payload.GetProperty("diagnostics").GetProperty("windowExpiredBeforeGet").GetBoolean().Should().BeFalse();
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_WithMismatchedRequestedEvent_ShouldReturnFilterMismatchReason()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var startParams = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = 250,
            allowShortStartDuration = true
        });

        var startResult = await handler.HandleAsync("trace_routed_events", startParams, CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", eventName = "MouseDown" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("filterMismatch");
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_AfterTraceWindowEndsWithoutEvents_ShouldReturnEventNotRaisedReason()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        var startParams = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = 120,
            allowShortStartDuration = true
        });

        var startResult = await handler.HandleAsync("trace_routed_events", startParams, CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        await Task.Delay(220);

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().Be(0);
        payload.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("eventNotRaised");
        payload.GetProperty("diagnostics").GetProperty("windowExpiredBeforeGet").GetBoolean().Should().BeTrue();
        payload.GetProperty("diagnostics").GetProperty("expiredByMs").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("diagnostics").GetProperty("windowEndedAtUtc").GetDateTimeOffset().Should().BeBefore(DateTimeOffset.UtcNow);
        payload.GetProperty("diagnostics").GetProperty("getRequestedAtUtc").GetDateTimeOffset().Should().BeAfter(
            payload.GetProperty("diagnostics").GetProperty("windowEndedAtUtc").GetDateTimeOffset());
    }

    [StaFact]
    public async Task TraceRoutedEvents_GetMode_WithZeroEvents_ShouldExposeRegistrationDiagnostics()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new System.Windows.Controls.Button { Name = "TraceDiagnosticsButton" };
        var elementId = finder.GenerateElementId(button);

        var startParams = JsonSerializer.SerializeToElement(new
        {
            mode = "start",
            elementId,
            eventName = "Click",
            duration = 120,
            allowShortStartDuration = true
        });

        var startResult = await handler.HandleAsync("trace_routed_events", startParams, CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        await Task.Delay(220);

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("activeEventName").GetString().Should().Be("Click");
        payload.GetProperty("resolvedElementId").GetString().Should().Be(elementId);
        payload.GetProperty("resolvedElementType").GetString().Should().Be("Button");
        payload.GetProperty("traceStartedAtUtc").GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("effectiveDurationMs").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("registrationCount").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("diagnostics").GetProperty("registrationCount").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("diagnostics").GetProperty("resolvedElementId").GetString().Should().Be(elementId);
        payload.GetProperty("diagnostics").GetProperty("resolvedElementType").GetString().Should().Be("Button");
    }
}
