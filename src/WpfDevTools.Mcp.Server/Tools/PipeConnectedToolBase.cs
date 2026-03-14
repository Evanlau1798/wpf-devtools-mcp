using System.Buffers;
using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.ErrorHandling;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Base class for MCP tools that communicate with Inspector via Named Pipes
/// </summary>
public abstract class PipeConnectedToolBase
{
    private const int DefaultPiggybackMaxEvents = 25;

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
            processId = ParameterParser.ParseIntParam(arguments, "processId");

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
    /// Create error response for missing required parameter
    /// </summary>
    protected static object CreateMissingParamError(string paramName) =>
        new ToolErrorPayload
        {
            Error = $"Missing required parameter: {paramName}",
            ErrorCode = ToolErrorCode.MissingRequiredParameter.ToString(),
            Hint = $"Provide {paramName} explicitly, or establish an active process/session before retrying."
        };

    /// <summary>
    /// Create error response for invalid parameter value.
    /// </summary>
    protected static object CreateInvalidParamError(string message) =>
        new ToolErrorPayload
        {
            Error = message,
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = "Correct the parameter value and retry the tool."
        };

    /// <summary>
    /// Create error response for not-connected process
    /// </summary>
    protected static object CreateNotConnectedError(int processId) =>
        new ToolErrorPayload
        {
            Error = $"Process {processId} is not connected. Call connect(processId: {processId}) first, then retry this tool.",
            ErrorCode = ToolErrorCode.NotConnected.ToString(),
            Hint = $"Call connect(processId: {processId}) before using inspection or mutation tools."
        };

    /// <summary>
    /// Create error response for omitted processId when no active process has been selected.
    /// </summary>
    protected static object CreateNoActiveProcessError() =>
        new ToolErrorPayload
        {
            Error = "No active process is selected. Provide processId explicitly or select an active process first.",
            ErrorCode = ToolErrorCode.NoActiveProcess.ToString(),
            Hint = "Call select_active_process(processId) or connect(processId) before omitting processId."
        };

    /// <summary>
    /// Send a request to the Inspector DLL via Named Pipe
    /// </summary>
    protected async Task<object> SendInspectorRequestAsync(
        int processId, string method, object? parameters, CancellationToken ct)
    {
        var result = await SendInspectorRequestCoreAsync(processId, method, parameters, ct).ConfigureAwait(false);
        return await TryPiggybackPendingEventsAsync(processId, method, result, ct).ConfigureAwait(false);
    }

    private async Task<object> SendInspectorRequestCoreAsync(
        int processId,
        string method,
        object? parameters,
        CancellationToken ct)
    {
        // Get pipe client atomically - avoids TOCTOU race between HasSession and GetPipeClient
        var client = _sessionManager.GetPipeClient(processId);
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
            return new ToolErrorPayload
            {
                Error = $"Named pipe not connected for process {processId}. The Inspector DLL may have crashed or the target process exited. Try reconnecting with connect(processId: {processId}).",
                ErrorCode = ToolErrorCode.NotConnected.ToString(),
                Hint = $"Call connect(processId: {processId}) to re-establish the inspector session before retrying."
            };
        }

        var response = await client.SendRequestAsync(
            method,
            Guid.NewGuid().ToString("N"),
            parameters,
            ct).ConfigureAwait(false);

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
        string method,
        object result,
        CancellationToken ct)
    {
        if (string.Equals(method, "drain_events", StringComparison.Ordinal))
        {
            return result;
        }

        var payload = ToJsonElement(result);
        if (!IsSuccessfulPayload(payload))
        {
            return result;
        }

        var drainResult = await SendInspectorRequestCoreAsync(
            processId,
            "drain_events",
            new { maxEvents = DefaultPiggybackMaxEvents },
            ct).ConfigureAwait(false);
        var drainPayload = ToJsonElement(drainResult);
        if (!IsSuccessfulPayload(drainPayload))
        {
            return result;
        }

        var pendingEventCount = GetIntProperty(drainPayload, "pendingEventCount");
        var droppedEventCount = GetIntProperty(drainPayload, "droppedEventCount");
        if (pendingEventCount <= 0 && droppedEventCount <= 0)
        {
            return result;
        }

        return MergePendingEvents(payload, drainPayload);
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

    private static object CreateInspectorError(InspectorError error)
    {
        return error.Data.HasValue
            ? new ToolErrorPayload
            {
                Error = error.Message,
                ErrorCode = error.Code.ToString(),
                Hint = GetInspectorHint(error.Code.ToString()),
                ErrorData = error.Data.Value
            }
            : new ToolErrorPayload
            {
                Error = error.Message,
                ErrorCode = error.Code.ToString(),
                Hint = GetInspectorHint(error.Code.ToString())
            };
    }

    private static string? GetInspectorHint(string errorCode) => errorCode switch
    {
        "ElementNotFound" => "Refresh the visual/logical tree and confirm the elementId before retrying.",
        "PropertyNotFound" => "Verify the propertyName spelling and the target element type.",
        "EventNotFound" => "Use a valid eventName for the target control type.",
        _ => null
    };

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

    private static object MergePendingEvents(JsonElement primaryPayload, JsonElement drainPayload)
    {
        if (primaryPayload.ValueKind != JsonValueKind.Object)
        {
            return primaryPayload;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in primaryPayload.EnumerateObject())
        {
            if (property.NameEquals("pendingEvents")
                || property.NameEquals("pendingEventCount")
                || property.NameEquals("droppedEventCount"))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        if (drainPayload.TryGetProperty("pendingEventCount", out var pendingEventCount))
        {
            writer.WritePropertyName("pendingEventCount");
            pendingEventCount.WriteTo(writer);
        }

        if (drainPayload.TryGetProperty("droppedEventCount", out var droppedEventCount))
        {
            writer.WritePropertyName("droppedEventCount");
            droppedEventCount.WriteTo(writer);
        }

        if (drainPayload.TryGetProperty("pendingEvents", out var pendingEvents))
        {
            writer.WritePropertyName("pendingEvents");
            pendingEvents.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
