using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Base class for MCP tools that communicate with Inspector via Named Pipes
/// </summary>
public abstract partial class PipeConnectedToolBase
{
    private const int DefaultPiggybackMaxEvents = 25;
    private static readonly TimeSpan PiggybackTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Session manager for tracking connected processes
    /// </summary>
    protected readonly SessionManager _sessionManager;

    /// <summary>
    /// Initializes a new instance of the PipeConnectedToolBase class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    /// <exception cref="ArgumentNullException">Thrown when sessionManager is null</exception>
    protected PipeConnectedToolBase(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    /// <summary>
    /// Parse processId and optional elementId from JSON arguments.
    /// Returns (processId, elementId, errorResult). If errorResult is non-null, return it immediately.
    /// </summary>
    public static (int processId, string? elementId, object? error) ParseCommonParams(JsonElement? arguments)
        => ParseCommonParams(arguments, null);

    /// <summary>
    /// Parse processId and optional elementId from JSON arguments, using the active process when allowed.
    /// </summary>
    public static (int processId, string? elementId, object? error) ParseCommonParams(
        JsonElement? arguments,
        SessionManager? sessionManager)
    {
        int? processId = null;
        string? elementId = null;
        var shouldValidateElementId = false;

        if (arguments.HasValue)
        {
            if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
                arguments,
                "processId",
                1,
                int.MaxValue,
                out processId,
                out var processIdError))
            {
                return (-1, null, processIdError);
            }

            if (arguments.Value.TryGetProperty("elementId", out var eidProp))
            {
                if (eidProp.ValueKind == JsonValueKind.Null)
                {
                    elementId = null;
                }
                else if (eidProp.ValueKind == JsonValueKind.String)
                {
                    elementId = eidProp.GetString();
                    shouldValidateElementId = true;
                }
                else
                {
                    return (-1, null, CreateInvalidParamError("elementId must be a string when provided"));
                }
            }
        }

        if (!processId.HasValue)
        {
            if (sessionManager != null && sessionManager.TryGetActiveProcessId(out var activeProcessId))
            {
                processId = activeProcessId;
            }
            else
            {
                return (-1, elementId, sessionManager != null
                    ? CreateNoActiveProcessError()
                    : CreateMissingParamError("processId"));
            }
        }

        if (processId.Value <= 0)
            return (-1, elementId, CreateInvalidParamError("processId must be a positive integer"));

        if (shouldValidateElementId && !ParameterParser.ValidateElementId(elementId, out var elementIdError))
            return (-1, elementId, CreateInvalidParamError(elementIdError!));

        return (processId.Value, elementId, null);
    }

    /// <summary>
    /// Parse a string parameter from JSON arguments
    /// </summary>
    protected static string? ParseStringParam(JsonElement? arguments, string paramName)
        => ParameterParser.ParseStringParam(arguments, paramName);

    /// <summary>
    /// Parse a string array parameter from JSON arguments.
    /// </summary>
    protected static string[]? ParseStringArrayParam(JsonElement? arguments, string paramName)
        => ParameterParser.ParseStringArrayParam(arguments, paramName);

    /// <summary>
    /// Parse an integer parameter from JSON arguments
    /// </summary>
    protected static int? ParseIntParam(JsonElement? arguments, string paramName)
        => ParameterParser.ParseIntParam(arguments, paramName);

    /// <summary>
    /// Parse a boolean parameter from JSON arguments
    /// </summary>
    protected static bool? ParseBoolParam(JsonElement? arguments, string paramName)
        => ParameterParser.ParseBoolParam(arguments, paramName);

    /// <summary>
    /// Parse mutation detail mode from JSON arguments.
    /// </summary>
    protected static (MutationDetailMode mode, object? error) ParseMutationDetailMode(JsonElement? arguments)
        => MutationDetailModeParser.Parse(arguments);

    /// <summary>
    /// Send a request to the Inspector DLL via Named Pipe
    /// </summary>
    protected async Task<object> SendInspectorRequestAsync(
        int processId,
        string method,
        object? parameters,
        CancellationToken ct,
        bool piggybackPendingEvents = true)
    {
        if (!_sessionManager.TryGetSessionGeneration(processId, out var expectedSessionGeneration))
        {
            return CreateNotConnectedError(processId);
        }

        return await SendInspectorRequestAsync(
            processId,
            expectedSessionGeneration,
            method,
            parameters,
            ct,
            piggybackPendingEvents).ConfigureAwait(false);
    }

    protected async Task<object> SendInspectorRequestAsync(
        int processId,
        long expectedSessionGeneration,
        string method,
        object? parameters,
        CancellationToken ct,
        bool piggybackPendingEvents = true)
    {
        var result = await SendInspectorRequestCoreAsync(
            processId,
            expectedSessionGeneration,
            method,
            parameters,
            ct).ConfigureAwait(false);
        if (!piggybackPendingEvents)
        {
            return result;
        }

        return await TryPiggybackPendingEventsAsync(
            processId,
            expectedSessionGeneration,
            method,
            result,
            ct).ConfigureAwait(false);
    }

    protected Task<object> SendInspectorRequestWithPiggybackAsync(
        int processId,
        string method,
        object? parameters,
        CancellationToken ct) =>
        SendInspectorRequestAsync(processId, method, parameters, ct, piggybackPendingEvents: true);

    protected Task<object> SendInspectorRequestWithoutPiggybackAsync(
        int processId,
        string method,
        object? parameters,
        CancellationToken ct) =>
        SendInspectorRequestAsync(processId, method, parameters, ct, piggybackPendingEvents: false);

    private async Task<object> SendInspectorRequestCoreAsync(
        int processId,
        long expectedSessionGeneration,
        string method,
        object? parameters,
        CancellationToken ct)
    {
        // Get the pipe client and verify the request is still bound to the same session generation.
        var client = _sessionManager.GetPipeClient(processId, expectedSessionGeneration);
        if (client == null)
            return CreateNotConnectedError(processId);

        // SECURITY: Check rate limit to prevent DoS attacks (only for connected sessions)
        var rateLimitStatus = _sessionManager.CheckRateLimitStatus(processId);
        if (!rateLimitStatus.Allowed)
        {
            return RateLimitResponseFactory.Create(
                rateLimitStatus,
                "Rate limit exceeded. Please slow down your requests.");
        }

        if (!client.IsConnected)
        {
            return CreatePipeDisconnectedError(processId);
        }

        InspectorResponse response;
        try
        {
            response = await client.SendRequestAsync(
                method,
                Guid.NewGuid().ToString("N"),
                parameters,
                ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            return CreatePipeTimeoutError(processId, ex.Message, requiresReconnect: !client.IsConnected);
        }
        catch (System.IO.IOException ex)
        {
            return CreatePipeTransportResetError(processId, ex.Message);
        }
        catch (InvalidOperationException ex) when (!client.IsConnected)
        {
            return CreatePipeTransportResetError(processId, ex.Message);
        }

        _sessionManager.UpdateLastActivity(processId);

        if (response.Error != null)
        {
            return CreateInspectorError(response.Error);
        }

        return response.Result.HasValue
            ? (object)response.Result.Value
            : new { success = true };
    }

    private async Task<object> TryPiggybackPendingEventsAsync(
        int processId,
        long expectedSessionGeneration,
        string method,
        object result,
        CancellationToken ct)
    {
        if (string.Equals(method, "drain_events", StringComparison.Ordinal))
        {
            return result;
        }

        if (!_sessionManager.IsCurrentSessionGeneration(processId, expectedSessionGeneration))
        {
            return result;
        }

        var payload = ToJsonElement(result);
        if (!IsSuccessfulPayload(payload))
        {
            return result;
        }

        if (ct.IsCancellationRequested)
        {
            return result;
        }

        using var piggybackCts = new CancellationTokenSource(PiggybackTimeout);
        try
        {
            using var replayLock = await _sessionManager.AcquirePendingEventReplayLockAsync(processId, piggybackCts.Token).ConfigureAwait(false);
            if (replayLock.SessionGeneration != expectedSessionGeneration)
            {
                return result;
            }

            object drainResult;
            drainResult = await SendInspectorRequestCoreAsync(
                processId,
                expectedSessionGeneration,
                "drain_events",
                new { maxEvents = DefaultPiggybackMaxEvents },
                piggybackCts.Token).ConfigureAwait(false);

            var drainPayload = ToJsonElement(drainResult);
            if (!IsSuccessfulPayload(drainPayload))
            {
                return MergePiggybackFailureDiagnostics(
                    result,
                    ResolvePiggybackFailureType(drainPayload),
                    GetStringProperty(drainPayload, "errorCode"),
                    GetStringProperty(drainPayload, "error"));
            }

            _sessionManager.TryPeekPendingEventReplay(processId, replayLock.SessionGeneration, out var existingReplayPayload);
            if (existingReplayPayload.ValueKind != JsonValueKind.Undefined)
            {
                var replayMergeResult = DrainEventsTool.MergeReplayPayloadForSharedBuffer(
                    existingReplayPayload,
                    drainPayload,
                    DefaultPiggybackMaxEvents,
                    eventTypes: null,
                    elementId: null,
                    sinceTimestamp: null);
                var replayStoragePayload = DrainEventsTool.MergeReplayPayloadForSharedBuffer(
                    existingReplayPayload,
                    drainPayload,
                    maxEvents: null,
                    eventTypes: null,
                    elementId: null,
                    sinceTimestamp: null).ResponsePayload;

                var mergedPendingEventCount = GetIntProperty(replayMergeResult.ResponsePayload, "pendingEventCount");
                var mergedDroppedEventCount = GetIntProperty(replayMergeResult.ResponsePayload, "droppedEventCount");
                if (mergedPendingEventCount <= 0
                    && mergedDroppedEventCount <= 0
                    && !HasCleanupIncompleteDiagnostics(replayMergeResult.ResponsePayload))
                {
                    return result;
                }

                _sessionManager.TryTakePendingEventReplay(processId, replayLock.SessionGeneration, out _);
                _sessionManager.SavePendingEventReplay(
                    processId,
                    replayLock.SessionGeneration,
                    replayStoragePayload);

                return MergePendingEvents(payload, replayMergeResult.ResponsePayload);
            }

            var pendingEventCount = GetIntProperty(drainPayload, "pendingEventCount");
            var droppedEventCount = GetIntProperty(drainPayload, "droppedEventCount");
            if (pendingEventCount <= 0
                && droppedEventCount <= 0
                && !HasCleanupIncompleteDiagnostics(drainPayload))
            {
                return result;
            }

            _sessionManager.SavePendingEventReplay(processId, replayLock.SessionGeneration, drainPayload);
            return MergePendingEvents(payload, drainPayload);
        }
        catch (Exception ex)
        {
            return MergePiggybackFailureDiagnostics(
                result,
                ResolvePiggybackFailureType(ex),
                null,
                ex.Message);
        }
    }

    protected static object AddSuccessMetadata(
        object result,
        object requestedInput,
        string notes,
        bool usedFallback = false,
        MutationDetailMode detailMode = MutationDetailMode.Compact)
    {
        var element = result is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(result);

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("success", out var successProp) ||
            !successProp.GetBoolean())
        {
            return result;
        }

        var payload = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            payload[property.Name] = property.Value.Clone();
        }

        if (detailMode == MutationDetailMode.Minimal)
        {
            return JsonSerializer.SerializeToElement(TrimMinimalMutationPayload(payload));
        }

        if (detailMode == MutationDetailMode.Compact)
        {
            if (usedFallback)
            {
                payload["usedFallback"] = true;
            }

            return JsonSerializer.SerializeToElement(payload);
        }

        payload["requestedInput"] = JsonSerializer.SerializeToElement(requestedInput);
        payload["effectiveInput"] = JsonSerializer.SerializeToElement(requestedInput);
        payload["observedEffect"] = element.Clone();
        payload["usedFallback"] = usedFallback;
        payload["notes"] = notes;

        return JsonSerializer.SerializeToElement(payload);
    }

    private static Dictionary<string, object?> TrimMinimalMutationPayload(Dictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("success", out var success))
        {
            return payload;
        }

        var minimal = new Dictionary<string, object?>
        {
            ["success"] = success
        };

        if (payload.TryGetValue("propertyName", out var propertyName))
        {
            minimal["propertyName"] = propertyName;
        }

        if (payload.TryGetValue("newValue", out var newValue))
        {
            minimal["newValue"] = newValue;
        }

        if (payload.TryGetValue("hadLocalValue", out var hadLocalValue))
        {
            minimal["hadLocalValue"] = hadLocalValue;
        }

        if (minimal.Count > 1)
        {
            return minimal;
        }

        return payload;
    }

    private static bool IsSuccessfulPayload(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("success", out var success)
        && success.ValueKind == JsonValueKind.True;

    private static JsonElement ToJsonElement(object payload) =>
        payload is JsonElement element
            ? element.Clone()
            : JsonSerializer.SerializeToElement(payload);

    private static int GetIntProperty(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var value)
            ? value
            : 0;

    private static string? GetStringProperty(JsonElement payload, string propertyName) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
