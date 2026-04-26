using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;
using WpfDevTools.Shared.Messages;

namespace WpfDevTools.Mcp.Server.Tools;

public abstract partial class PipeConnectedToolBase
{
    /// <summary>
    /// Create error response for missing required parameter.
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
    /// Create error response for not-connected process.
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

    private static ToolErrorPayload CreatePipeDisconnectedError(int processId) =>
        new()
        {
            Error = $"Named pipe not connected for process {processId}. The Inspector DLL may have crashed or the target process exited. Try reconnecting with connect(processId: {processId}).",
            ErrorCode = ToolErrorCode.NotConnected.ToString(),
            Hint = $"Call connect(processId: {processId}) to re-establish the inspector session before retrying."
        };

    internal static object CreateInspectorError(InspectorError error)
    {
        var data = error.Data;
        var hint = GetString(data, "hint") ?? GetInspectorHint(error.Code.ToString());
        var suggestedAction = GetString(data, "suggestedAction");
        var requiresReconnect = GetBool(data, "requiresReconnect");
        var stateAfterTimeoutUnknown = GetBool(data, "stateAfterTimeoutUnknown");
        var processId = GetInt(data, "processId");
        var timeoutSeconds = GetInt(data, "timeoutSeconds");

        return new ToolErrorPayload
        {
            Error = error.Message,
            ErrorCode = error.Code.ToString(),
            Hint = hint,
            ErrorData = data.HasValue ? data.Value : null,
            Recovery = CreateRecovery(
                hint,
                suggestedAction,
                requiresReconnect,
                stateAfterTimeoutUnknown,
                processId,
                timeoutSeconds),
            SuggestedAction = suggestedAction,
            RequiresReconnect = requiresReconnect,
            StateAfterTimeoutUnknown = stateAfterTimeoutUnknown,
            ProcessId = processId,
            TimeoutSeconds = timeoutSeconds
        };
    }

    internal static ToolErrorPayload CreatePipeTimeoutError(
        int processId,
        string message,
        bool requiresReconnect)
    {
        var suggestedAction = requiresReconnect
            ? $"Reconnect to process {processId} and re-read target state before retrying."
            : "Retry after confirming the target is responsive. Reconnect if subsequent pipe-backed requests fail.";
        const string hint = "The Inspector request timed out; the target process may be frozen or the pipe session may be stale.";

        return new ToolErrorPayload
        {
            Error = message,
            ErrorCode = "Timeout",
            Hint = hint,
            ErrorData = new
            {
                processId,
                stateAfterTimeoutUnknown = true,
                requiresReconnect
            },
            Recovery = new ToolErrorRecovery
            {
                Hint = hint,
                SuggestedAction = suggestedAction,
                RequiresReconnect = requiresReconnect,
                StateAfterTimeoutUnknown = true,
                ProcessId = processId
            },
            SuggestedAction = suggestedAction,
            RequiresReconnect = requiresReconnect,
            StateAfterTimeoutUnknown = true,
            ProcessId = processId
        };
    }

    private static ToolErrorRecovery? CreateRecovery(
        string? hint,
        string? suggestedAction,
        bool? requiresReconnect,
        bool? stateAfterTimeoutUnknown,
        int? processId,
        int? timeoutSeconds)
    {
        if (hint is null
            && suggestedAction is null
            && requiresReconnect is null
            && stateAfterTimeoutUnknown is null
            && processId is null
            && timeoutSeconds is null)
        {
            return null;
        }

        return new ToolErrorRecovery
        {
            Hint = hint,
            SuggestedAction = suggestedAction,
            RequiresReconnect = requiresReconnect,
            StateAfterTimeoutUnknown = stateAfterTimeoutUnknown,
            ProcessId = processId,
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static string? GetInspectorHint(string errorCode) => errorCode switch
    {
        "ElementNotFound" => "Refresh the visual/logical tree and confirm the elementId before retrying.",
        "PropertyNotFound" => "Verify the propertyName spelling and the target element type.",
        "EventNotFound" => "Use a valid eventName for the target control type.",
        _ => null
    };

    private static string? GetString(JsonElement? data, string propertyName) =>
        data is { ValueKind: JsonValueKind.Object }
        && data.Value.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? GetBool(JsonElement? data, string propertyName) =>
        data is { ValueKind: JsonValueKind.Object }
        && data.Value.TryGetProperty(propertyName, out var property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static int? GetInt(JsonElement? data, string propertyName) =>
        data is { ValueKind: JsonValueKind.Object }
        && data.Value.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var value)
            ? value
            : null;
}
