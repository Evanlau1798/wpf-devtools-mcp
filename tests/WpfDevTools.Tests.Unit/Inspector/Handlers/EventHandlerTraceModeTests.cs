using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class EventHandlerTraceModeTests
{
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
    }
}
