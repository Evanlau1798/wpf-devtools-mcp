using System.Buffers;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Navigation;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class TraceRoutedEventsTool
{
    private object MergePendingReplayIntoTraceResult(
        int processId,
        object response,
        ActiveTraceNavigationState? replayTraceSnapshot,
        bool allowPreSyncFallback)
    {
        var payload = JsonSerializer.SerializeToElement(response);
        var responseSessionId = GetOptionalString(payload, "sessionId");
        if (!IsSuccess(payload)
            || GetEventCount(payload) > 0
            || HasNonMergeableDiagnostics(payload)
            || !_sessionManager.TryPeekPendingEventReplayMetadata(processId, out var replayPayload, out var replaySavedAtUtc))
        {
            return response;
        }

        var activeTrace = ResolveReplayTraceState(
            processId,
            responseSessionId,
            replayTraceSnapshot,
            allowPreSyncFallback,
            allowExpiredPreSyncFallback: IsTracing(payload));
        if (activeTrace is null)
        {
            return response;
        }

        var replayEvents = TraceRoutedEventsReplayFormatter.GetMatchingReplayEvents(
            replayPayload,
            activeTrace,
            replaySavedAtUtc);
        if (replayEvents.Count == 0)
        {
            return response;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        var keepDiagnostics = ShouldKeepDiagnosticsAfterReplayMerge(payload);

        foreach (var property in payload.EnumerateObject())
        {
            if (property.NameEquals("eventCount")
                || property.NameEquals("events")
                || (property.NameEquals("diagnostics") && !keepDiagnostics))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteNumber("eventCount", replayEvents.Count);
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        foreach (var replayEvent in replayEvents)
        {
            TraceRoutedEventsReplayFormatter.WriteReplayTraceEvent(writer, replayEvent);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static bool ShouldKeepDiagnosticsAfterReplayMerge(JsonElement payload)
    {
        return payload.TryGetProperty("diagnostics", out var diagnostics)
            && diagnostics.ValueKind == JsonValueKind.Object
            && diagnostics.TryGetProperty("reasonCode", out var reasonCode)
            && reasonCode.ValueKind == JsonValueKind.String
            && string.Equals(reasonCode.GetString(), "cleanupFailed", StringComparison.Ordinal);
    }

    private ActiveTraceNavigationState? ResolveReplayTraceState(
        int processId,
        string? responseSessionId,
        ActiveTraceNavigationState? replayTraceSnapshot,
        bool allowPreSyncFallback,
        bool allowExpiredPreSyncFallback)
    {
        if (_sessionManager.TryGetNavigationState(processId, out var navigationState)
            && navigationState?.ActiveTrace is not null
            && !IsMissingSessionIdForSessionAwareTrace(navigationState.ActiveTrace, responseSessionId, "get")
            && !IsStaleTraceResponse(navigationState.ActiveTrace, responseSessionId, "get"))
        {
            return navigationState.ActiveTrace;
        }

        if (replayTraceSnapshot is null
            || IsMissingSessionIdForSessionAwareTrace(replayTraceSnapshot, responseSessionId, "get")
            || IsStaleTraceResponse(replayTraceSnapshot, responseSessionId, "get"))
        {
            return null;
        }

        var replayTraceExpired = replayTraceSnapshot.HasExpired(DateTimeOffset.UtcNow);
        if (!allowPreSyncFallback && !(allowExpiredPreSyncFallback && replayTraceExpired))
        {
            return null;
        }

        return replayTraceSnapshot;
    }

    private static int GetEventCount(JsonElement payload) =>
        payload.TryGetProperty("eventCount", out var eventCount)
        && eventCount.ValueKind == JsonValueKind.Number
        && eventCount.TryGetInt32(out var count)
            ? count
            : 0;

    private static bool HasNonMergeableDiagnostics(JsonElement payload) =>
        payload.TryGetProperty("diagnostics", out var diagnostics)
        && diagnostics.ValueKind == JsonValueKind.Object
        && ((diagnostics.TryGetProperty("reasonCode", out var reasonCode)
                && reasonCode.ValueKind == JsonValueKind.String
                && string.Equals(reasonCode.GetString(), "filterMismatch", StringComparison.Ordinal))
            || (diagnostics.TryGetProperty("requestedEventMismatch", out var requestedEventMismatch)
                && requestedEventMismatch.ValueKind == JsonValueKind.True));
}
