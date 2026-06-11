using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

internal sealed record ToolRecoveryProjection(
    string? Error,
    string? ErrorCode,
    string? Hint,
    string? SuggestedAction,
    bool? RequiresReconnect,
    bool? StateAfterTimeoutUnknown,
    int? ProcessId,
    int? TimeoutSeconds,
    int? RetryAfterSeconds,
    string? RetryAfter,
    int? AvailableTokens,
    string[]? AvailableEvents)
{
    public ToolErrorRecovery? ToRecovery() =>
        Hint is null
        && SuggestedAction is null
        && RequiresReconnect is null
        && StateAfterTimeoutUnknown is null
        && ProcessId is null
        && TimeoutSeconds is null
        && RetryAfterSeconds is null
        && RetryAfter is null
        && AvailableTokens is null
        && AvailableEvents is null
            ? null
            : new ToolErrorRecovery
            {
                Hint = Hint,
                SuggestedAction = SuggestedAction,
                RequiresReconnect = RequiresReconnect,
                StateAfterTimeoutUnknown = StateAfterTimeoutUnknown,
                ProcessId = ProcessId,
                TimeoutSeconds = TimeoutSeconds,
                RetryAfterSeconds = RetryAfterSeconds,
                RetryAfter = RetryAfter,
                AvailableTokens = AvailableTokens,
                AvailableEvents = AvailableEvents
            };
}

internal static class ToolRecoveryPayload
{
    public static ToolRecoveryProjection Extract(JsonElement response) =>
        new(
            GetString(response, "error"),
            GetString(response, "errorCode"),
            GetString(response, "hint"),
            GetString(response, "suggestedAction"),
            GetBool(response, "requiresReconnect"),
            GetBool(response, "stateAfterTimeoutUnknown"),
            GetInt(response, "processId"),
            GetInt(response, "timeoutSeconds"),
            GetInt(response, "retryAfterSeconds"),
            GetString(response, "retryAfter"),
            GetInt(response, "availableTokens"),
            GetStringArray(response, "availableEvents"));

    public static bool IsTimeoutOrTransportRecovery(JsonElement response)
    {
        var projection = Extract(response);
        return string.Equals(projection.ErrorCode, "Timeout", StringComparison.Ordinal)
            || string.Equals(projection.ErrorCode, "TransportReset", StringComparison.Ordinal)
            || projection.RequiresReconnect == true
            || projection.StateAfterTimeoutUnknown == true;
    }

    public static bool HasRecoveryGuidance(JsonElement response) =>
        Extract(response).ToRecovery() is not null;

    public static ToolErrorPayload CreateStepFailure(
        string contextMessage,
        string fallbackHint,
        JsonElement response)
    {
        var projection = Extract(response);
        return new ToolErrorPayload
        {
            Error = string.IsNullOrWhiteSpace(projection.Error)
                ? contextMessage
                : $"{contextMessage} {projection.Error}".Trim(),
            ErrorCode = string.IsNullOrWhiteSpace(projection.ErrorCode)
                ? ToolErrorCode.OperationFailed.ToString()
                : projection.ErrorCode!,
            Hint = projection.Hint ?? fallbackHint,
            ErrorData = response.Clone(),
            Recovery = projection.ToRecovery(),
            SuggestedAction = projection.SuggestedAction,
            RequiresReconnect = projection.RequiresReconnect,
            StateAfterTimeoutUnknown = projection.StateAfterTimeoutUnknown,
            ProcessId = projection.ProcessId,
            TimeoutSeconds = projection.TimeoutSeconds,
            RetryAfterSeconds = projection.RetryAfterSeconds,
            RetryAfter = projection.RetryAfter,
            AvailableTokens = projection.AvailableTokens,
            AvailableEvents = projection.AvailableEvents
        };
    }

    private static string? GetString(JsonElement response, string propertyName)
    {
        if (response.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return response.TryGetProperty("recovery", out var recovery)
            && recovery.ValueKind == JsonValueKind.Object
            && recovery.TryGetProperty(propertyName, out property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool? GetBool(JsonElement response, string propertyName)
    {
        if (response.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return property.GetBoolean();
        }

        return response.TryGetProperty("recovery", out var recovery)
            && recovery.ValueKind == JsonValueKind.Object
            && recovery.TryGetProperty(propertyName, out property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? property.GetBoolean()
                : null;
    }

    private static int? GetInt(JsonElement response, string propertyName)
    {
        if (response.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value))
        {
            return value;
        }

        return response.TryGetProperty("recovery", out var recovery)
            && recovery.ValueKind == JsonValueKind.Object
            && recovery.TryGetProperty(propertyName, out property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value)
                ? value
                : null;
    }

    private static string[]? GetStringArray(JsonElement response, string propertyName)
    {
        if (TryGetStringArray(response, propertyName, out var values))
        {
            return values;
        }

        return response.TryGetProperty("recovery", out var recovery)
            && recovery.ValueKind == JsonValueKind.Object
            && TryGetStringArray(recovery, propertyName, out values)
                ? values
                : null;
    }

    private static bool TryGetStringArray(JsonElement response, string propertyName, out string[] values)
    {
        values = [];
        if (!response.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var items = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } value)
            {
                items.Add(value);
            }
        }

        values = items.ToArray();
        return true;
    }
}
