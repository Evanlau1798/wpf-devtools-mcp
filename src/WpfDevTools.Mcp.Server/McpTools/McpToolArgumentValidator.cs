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
        "typeNames",
        "elementName",
        "automationId",
        "propertyName",
        "propertyValue",
        "maxResults",
        "maxTraversalNodes",
        "matchMode"
    };

    public static CallToolResult? Validate(
        string? toolName,
        IEnumerable<KeyValuePair<string, JsonElement>>? arguments)
    {
        if (!string.Equals(toolName, "find_elements", StringComparison.Ordinal) || arguments is null)
        {
            return null;
        }

        foreach (var argument in arguments)
        {
            if (FindElementsArgumentNames.Contains(argument.Key))
            {
                continue;
            }

            return CreateFindElementsErrorResult(argument.Key);
        }

        return null;
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
            Hint = "Use one of: processId, elementId, typeName, typeNames, elementName, automationId, propertyName, propertyValue, maxResults, maxTraversalNodes, matchMode."
        };
    }
}
