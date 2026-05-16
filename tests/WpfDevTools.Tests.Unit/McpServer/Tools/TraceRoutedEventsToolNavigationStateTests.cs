using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public partial class TraceRoutedEventsToolTests
{
    [Fact]
    public async Task Execute_WithCaptureCleanupFailure_ShouldUseResponseDurationWhenRequestDurationIsOmitted()
    {
        const int processId = 22006;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"sessionId":"trace-capture-cleanup-failed-fallback","mode":"capture","eventName":"Click","duration":150,"isTracing":false,"eventCount":1,"events":[{"eventName":"Click"}],"handlerInvocationCount":1,"diagnostics":{"reasonCode":"cleanupFailed","cleanupFailureType":"TimeoutException"}}""");
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            eventName = "Click",
            elementId = "SaveButton",
            mode = "capture"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.EffectiveDuration.Should().Be(TimeSpan.FromMilliseconds(150));
        state.ActiveTrace.IgnoreExpiry.Should().BeTrue();
        state.ActiveTrace.HasExpired(DateTimeOffset.UtcNow.AddMinutes(1)).Should().BeFalse();
        state.ActiveTrace.HasExpired(DateTimeOffset.UtcNow.AddMinutes(3)).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithCaptureCleanupFailure_ShouldReconstructStartTimeFromResponseDurationBeforeRequestedDuration()
    {
        const int processId = 22014;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"sessionId":"trace-capture-cleanup-failed-capped","mode":"capture","eventName":"Click","duration":60000,"isTracing":false,"eventCount":1,"events":[{"eventName":"Click"}],"handlerInvocationCount":1,"diagnostics":{"reasonCode":"cleanupFailed","cleanupFailureType":"TimeoutException"}}""");
        var tool = new TraceRoutedEventsTool(connected.SessionManager);
        var before = DateTimeOffset.UtcNow;

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            eventName = "Click",
            elementId = "SaveButton",
            duration = 30000,
            mode = "capture"
        }), CancellationToken.None);

        var after = DateTimeOffset.UtcNow;

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.EffectiveDuration.Should().Be(TimeSpan.FromMilliseconds(60000));
        state.ActiveTrace.StartedAtUtc.Should().BeOnOrAfter(before.AddMilliseconds(-61000));
        state.ActiveTrace.StartedAtUtc.Should().BeOnOrBefore(after.AddMilliseconds(-59000));
    }

    [Fact]
    public async Task Execute_WithRepeatedCleanupFailedGet_ShouldKeepOriginalFollowUpExpiry()
    {
        const int processId = 22013;
        var response = """{"success":true,"sessionId":"trace-cleanup-followup","mode":"get","isTracing":false,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"cleanupFailed","activeEventName":"Click","resolvedElementId":"SaveButton"},"activeEventName":"Click","resolvedElementId":"SaveButton","traceStartedAtUtc":"2026-01-01T00:00:00+00:00","effectiveDurationMs":150,"registrationCount":1}""";
        using var connected = await CreateConnectedSessionAsync(processId, response);
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        connected.SessionManager.TryGetNavigationState(processId, out var stateAfterFirstGet).Should().BeTrue();
        var firstExpiry = stateAfterFirstGet!.ActiveTrace!.FollowUpExpiresAtUtc;

        await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        connected.SessionManager.TryGetNavigationState(processId, out var stateAfterSecondGet).Should().BeTrue();
        stateAfterSecondGet!.ActiveTrace.Should().NotBeNull();
        stateAfterSecondGet.ActiveTrace!.FollowUpExpiresAtUtc.Should().Be(firstExpiry);
    }

    [Fact]
    public async Task Execute_WithGetFilterMismatch_ShouldPreserveExistingActiveTraceState()
    {
        const int processId = 22007;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"sessionId":"trace-filter-mismatch","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"filterMismatch","requestedEventName":"MouseDown","activeEventName":"Click","resolvedElementId":"SaveButton"}}""");
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "SaveButton",
                DateTimeOffset.UtcNow.AddSeconds(-1),
                TimeSpan.FromSeconds(5),
                SessionId: "trace-filter-mismatch"));
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get",
            eventName = "MouseDown",
            elementId = "OtherButton"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.EventName.Should().Be("Click");
        state.ActiveTrace.ElementId.Should().Be("SaveButton");
    }

    [Fact]
    public async Task Execute_WithCompletedCaptureFromStaleSession_ShouldNotClearNewerActiveTraceState()
    {
        const int processId = 22008;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"sessionId":"trace-old","mode":"capture","eventName":"Click","duration":150,"isTracing":false,"eventCount":1,"events":[{"eventName":"Click"}],"handlerInvocationCount":1}""");
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "SaveButton",
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(5),
                SessionId: "trace-new"));
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            eventName = "Click",
            elementId = "SaveButton",
            duration = 150,
            mode = "capture"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.SessionId.Should().Be("trace-new");
    }

    [Fact]
    public async Task Execute_WithGetResponseAfterStateWasCleared_ShouldNotRecreateActiveTraceState()
    {
        const int processId = 22009;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"sessionId":"trace-old-get","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"captureWindowTooShort","activeEventName":"Click","resolvedElementId":"SaveButton"}}""");
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "SaveButton",
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                SessionId: "trace-old-get"));
            connected.SessionManager.ClearActiveTraceState(processId, "trace-old-get");
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().BeNull();
    }

    [Fact]
    public async Task Execute_WithLateGetFromOlderEndedSession_ShouldNotRehydrateAfterNewerSessionAlsoEnded()
    {
        const int processId = 22015;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"sessionId":"trace-old-get","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"captureWindowTooShort","activeEventName":"Click","resolvedElementId":"SaveButton"},"activeEventName":"Click","resolvedElementId":"SaveButton","traceStartedAtUtc":"2026-01-01T00:00:00+00:00","effectiveDurationMs":1000,"registrationCount":1}""");
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "SaveButton",
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                SessionId: "trace-old-get"));
        connected.SessionManager.ClearActiveTraceState(processId, "trace-old-get");
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "SaveButton",
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                SessionId: "trace-new-get"));
        connected.SessionManager.ClearActiveTraceState(processId, "trace-new-get");
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().BeNull();
        state.RecentlyEndedTraceSessionIds.Should().Contain(new[] { "trace-old-get", "trace-new-get" });
    }

    [Fact]
    public async Task Execute_WithGetResponseAfterOrdinaryStateLoss_ShouldRehydrateActiveTraceState()
    {
        const int processId = 22010;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"sessionId":"trace-rehydrate","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"captureWindowTooShort","activeEventName":"Click","resolvedElementId":"SaveButton"},"activeEventName":"Click","resolvedElementId":"SaveButton","traceStartedAtUtc":"2026-01-01T00:00:00+00:00","effectiveDurationMs":1000,"registrationCount":1}""");
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "SaveButton",
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                SessionId: "trace-rehydrate"));
        connected.SessionManager.ClearActiveTraceState(processId);
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.SessionId.Should().Be("trace-rehydrate");
        state.ActiveTrace.EventName.Should().Be("Click");
        state.ActiveTrace.ElementId.Should().Be("SaveButton");
    }

    [Fact]
    public async Task Execute_WithSuccessfulGetAfterOrdinaryStateLoss_ShouldRehydrateFromTopLevelTraceMetadata()
    {
        const int processId = 22017;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                sessionId = "trace-rehydrate-success",
                mode = "get",
                isTracing = true,
                eventCount = 1,
                events = new[] { new { eventName = "Click" } },
                handlerInvocationCount = 1,
                activeEventName = "Click",
                resolvedElementId = "SaveButton",
                traceStartedAtUtc = "2026-01-01T00:00:00+00:00",
                effectiveDurationMs = 1000,
                registrationCount = 1
            }));
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "SaveButton",
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                SessionId: "trace-rehydrate-success"));
        connected.SessionManager.ClearActiveTraceState(processId);
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.SessionId.Should().Be("trace-rehydrate-success");
        state.ActiveTrace.EventName.Should().Be("Click");
        state.ActiveTrace.ElementId.Should().Be("SaveButton");
        state.ActiveTrace.EffectiveDuration.Should().Be(TimeSpan.FromMilliseconds(1000));
        state.ActiveTrace.StartedAtUtc.Should().Be(DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
    }

    [StaFact]
    public async Task Execute_WithRealInspectorGetPayload_ShouldRehydrateUsingPublicTraceMetadata()
    {
        const int processId = 22016;
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new Button { Name = "CrossLayerTraceButton" };
        var elementId = finder.GenerateElementId(button);

        var startResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 1200,
                allowShortStartDuration = true
            }),
            CancellationToken.None);

        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        var inspectorGetResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get" }),
            CancellationToken.None);
        var inspectorPayload = JsonSerializer.SerializeToElement(inspectorGetResult);
        inspectorPayload.GetProperty("success").GetBoolean().Should().BeTrue();
        inspectorPayload.GetProperty("isTracing").GetBoolean().Should().BeTrue();

        using var connected = await CreateConnectedSessionAsync(
            processId,
            JsonSerializer.Serialize(inspectorGetResult));
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.EventName.Should().Be("Click");
        state.ActiveTrace.ElementId.Should().Be(elementId);
        state.ActiveTrace.EffectiveDuration.Should().Be(TimeSpan.FromMilliseconds(inspectorPayload.GetProperty("effectiveDurationMs").GetInt32()));
        state.ActiveTrace.StartedAtUtc.Should().Be(DateTimeOffset.Parse(inspectorPayload.GetProperty("traceStartedAtUtc").GetString()!));
    }

    [Fact]
    public async Task Execute_WithGetResponseAfterExpiredCleanupFailedState_ShouldNotRefreshExpiredTrace()
    {
        const int processId = 22011;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"sessionId":"trace-expired-cleanup-failed","mode":"get","isTracing":false,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"cleanupFailed","activeEventName":"Click","resolvedElementId":"SaveButton"},"activeEventName":"Click","resolvedElementId":"SaveButton","traceStartedAtUtc":"2026-01-01T00:00:00+00:00","effectiveDurationMs":150,"registrationCount":1}""");
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "SaveButton",
                DateTimeOffset.UtcNow.AddMinutes(-5),
                TimeSpan.FromMilliseconds(150),
                SessionId: "trace-expired-cleanup-failed",
                IgnoreExpiry: true,
                FollowUpExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1)));
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().BeNull();
        state.LastEndedTraceSessionId.Should().Be("trace-expired-cleanup-failed");
    }

    [Fact]
    public async Task Execute_WithGetResponseAfterExpiredOrdinaryTraceState_ShouldRehydrateWhenInspectorStillReportsTracing()
    {
        const int processId = 22012;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"sessionId":"trace-expired-normal","mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"captureWindowTooShort","activeEventName":"Click","resolvedElementId":"SaveButton"},"activeEventName":"Click","resolvedElementId":"SaveButton","traceStartedAtUtc":"2026-01-01T00:00:00+00:00","effectiveDurationMs":100,"registrationCount":1}""");
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "SaveButton",
                DateTimeOffset.UtcNow.AddSeconds(-5),
                TimeSpan.FromMilliseconds(100),
                SessionId: "trace-expired-normal"));
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.SessionId.Should().Be("trace-expired-normal");
        state.LastEndedTraceSessionId.Should().BeNull();
    }

}
