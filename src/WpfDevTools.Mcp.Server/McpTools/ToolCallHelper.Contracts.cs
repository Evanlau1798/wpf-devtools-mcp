using System.Text.Json;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    private static bool TryBuildCanonicalRecovery(JsonElement element, out JsonElement recovery)
    {
        var recoveryProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (element.TryGetProperty("recovery", out var existingRecovery)
            && existingRecovery.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in existingRecovery.EnumerateObject())
            {
                recoveryProperties[property.Name] = property.Value.Clone();
            }
        }

        foreach (var propertyName in RecoveryCompatibilityFields)
        {
            if (recoveryProperties.ContainsKey(propertyName)
                || !element.TryGetProperty(propertyName, out var property)
                || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            recoveryProperties[propertyName] = property.Clone();
        }

        if (recoveryProperties.Count == 0)
        {
            recovery = default;
            return false;
        }

        recovery = JsonSerializer.SerializeToElement(recoveryProperties, SerializerOptions);
        return true;
    }

    private static int? TryGetProcessId(JsonElement? args)
    {
        if (args is not { } candidate
            || candidate.ValueKind != JsonValueKind.Object
            || !candidate.TryGetProperty("processId", out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var processId))
        {
            return null;
        }

        return processId;
    }

    private static (bool RequiresReconnect, int? ProcessId) ResolveTimeoutRecovery(
        string toolName,
        JsonElement? args,
        SessionManager? sessionManager = null)
    {
        if (TimeoutReconnectOptOutTools.Contains(toolName))
        {
            return (false, null);
        }

        var processId = TryGetProcessId(args);
        if (processId is null
            && sessionManager is not null
            && sessionManager.TryGetActiveProcessId(out var activeProcessId))
        {
            processId = activeProcessId;
        }

        if (processId is null)
        {
            return (false, null);
        }

        return (true, processId);
    }

    private static bool ShouldIncludeNavigation(string toolName, JsonElement? args)
    {
        if (args is not { } candidate
            || candidate.ValueKind != JsonValueKind.Object
            || !candidate.TryGetProperty("navigation", out var property)
            || property.ValueKind != JsonValueKind.False)
        {
            return true;
        }

        return !NavigationOptOutTools.Contains(toolName);
    }

    private static bool TryGetBool(JsonElement? args, string propertyName)
    {
        return args is { } candidate
            && candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();
    }
}
