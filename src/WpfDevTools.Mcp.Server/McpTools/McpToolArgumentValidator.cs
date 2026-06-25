using System.Text.Json;
using ModelContextProtocol.Protocol;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.McpTools;

internal static class McpToolArgumentValidator
{
    private static readonly HashSet<string> FindElementsArgumentNames = new(StringComparer.Ordinal)
    {
        "processId",
        "elementId",
        "typeName",
        "controlType",
        "typeNames",
        "elementName",
        "automationId",
        "propertyName",
        "propertyValue",
        "maxResults",
        "maxTraversalNodes",
        "matchMode"
    };

    private static readonly Dictionary<string, string[]> RequiredArgumentsByTool = new(StringComparer.Ordinal)
    {
        ["get_event_handlers"] = ["eventName", "elementId"],
        ["fire_routed_event"] = ["eventName", "elementId"]
    };

    public static CallToolResult? Validate(
        string? toolName,
        IEnumerable<KeyValuePair<string, JsonElement>>? arguments)
    {
        var argumentArray = arguments?.ToArray();
        if (toolName is not null
            && RequiredArgumentsByTool.TryGetValue(toolName, out var requiredArguments)
            && TryFindMissingRequiredArgument(argumentArray, requiredArguments, out var missingArgument))
        {
            return CreateMissingRequiredArgumentErrorResult(missingArgument);
        }

        if (!string.Equals(toolName, "find_elements", StringComparison.Ordinal) || argumentArray is null)
        {
            return null;
        }

        foreach (var argument in argumentArray)
        {
            if (FindElementsArgumentNames.Contains(argument.Key))
            {
                continue;
            }

            return CreateFindElementsErrorResult(argument.Key);
        }

        return null;
    }

    private static bool TryFindMissingRequiredArgument(
        IReadOnlyCollection<KeyValuePair<string, JsonElement>>? arguments,
        IReadOnlyList<string> requiredArguments,
        out string missingArgument)
    {
        foreach (var requiredArgument in requiredArguments)
        {
            if (arguments is null || !TryGetArgument(arguments, requiredArgument, out var argument))
            {
                missingArgument = requiredArgument;
                return true;
            }

            if (argument.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                missingArgument = requiredArgument;
                return true;
            }

            if (argument.ValueKind == JsonValueKind.String && argument.GetString()?.Length == 0)
            {
                missingArgument = requiredArgument;
                return true;
            }
        }

        missingArgument = string.Empty;
        return false;
    }

    private static bool TryGetArgument(
        IEnumerable<KeyValuePair<string, JsonElement>> arguments,
        string name,
        out JsonElement value)
    {
        foreach (var argument in arguments)
        {
            if (string.Equals(argument.Key, name, StringComparison.Ordinal))
            {
                value = argument.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    internal static ToolErrorPayload? ValidateFindElementsArguments(JsonElement? arguments)
    {
        if (!arguments.HasValue || arguments.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in arguments.Value.EnumerateObject())
        {
            if (FindElementsArgumentNames.Contains(property.Name))
            {
                continue;
            }

            return CreateFindElementsErrorPayload(property.Name);
        }

        return null;
    }

    private static CallToolResult CreateMissingRequiredArgumentErrorResult(string argumentName)
    {
        return ToolCallHelper.CreateStructuredErrorResult(
            $"Missing required parameter: {argumentName}",
            ToolErrorCode.MissingRequiredParameter.ToString(),
            hint: $"Provide {argumentName} explicitly, or establish an active process/session before retrying.",
            suggestedAction: $"Retry with {argumentName} set.");
    }

    private static CallToolResult CreateFindElementsErrorResult(string argumentName)
    {
        var payload = CreateFindElementsErrorPayload(argumentName);
        return ToolCallHelper.CreateStructuredErrorResult(
            payload.Error,
            payload.ErrorCode,
            payload.Hint,
            suggestedAction: payload.Hint);
    }

    private static ToolErrorPayload CreateFindElementsErrorPayload(string argumentName)
    {
        if (string.Equals(argumentName, "nameFilter", StringComparison.Ordinal))
        {
            return new()
            {
                Error = "Unknown argument 'nameFilter' for find_elements. Use 'elementName' for FrameworkElement.Name or 'automationId' for AutomationProperties.AutomationId; 'nameFilter' is only supported by get_processes.",
                ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
                Hint = "For named WPF controls, call find_elements with elementName. Use get_processes(nameFilter) only when filtering process names before connecting."
            };
        }

        return new()
        {
            Error = $"Unknown argument '{argumentName}' for find_elements.",
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = "Use one of: processId, elementId, typeName, controlType, typeNames, elementName, automationId, propertyName, propertyValue, maxResults, maxTraversalNodes, matchMode."
        };
    }
}
