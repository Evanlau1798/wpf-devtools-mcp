using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class EventHandlerTraceDiagnosticsRaceTests
{
    [Fact]
    public void BuildZeroEventDiagnostics_WhenTraceWindowAlreadyEndedButIsTracingIsStillTrue_ShouldReturnEventNotRaised()
    {
        var method = typeof(EventHandlers).GetMethod(
            "BuildZeroEventDiagnostics",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var traceResult = new
        {
            success = true,
            eventCount = 0,
            isTracing = true,
            events = Array.Empty<object>(),
            handlerInvocationCount = 0
        };
        var metadata = new TraceSessionMetadata(
            EventName: "Click",
            ElementId: "Button_1",
            StartedAtUtc: DateTimeOffset.UtcNow.AddMilliseconds(-600),
            EffectiveDurationMs: 100,
            RegistrationCount: 1,
            ResolvedElementType: "Button");

        var diagnostics = method!.Invoke(null, [traceResult, metadata, null]);

        var payload = JsonSerializer.SerializeToElement(diagnostics);
        payload.GetProperty("reasonCode").GetString().Should().Be("eventNotRaised");
        payload.GetProperty("windowExpiredBeforeGet").GetBoolean().Should().BeTrue();
    }
}
