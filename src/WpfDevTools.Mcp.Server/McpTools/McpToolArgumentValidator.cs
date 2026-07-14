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
        "query",
        "typeName",
        "controlType",
        "typeNames",
        "elementName",
        "automationId",
        "propertyName",
        "propertyValue",
        "maxResults",
        "maxTraversalNodes",
        "matchMode",
        "typeMatchMode"
    };

    private static readonly HashSet<string> SerializeToXamlArgumentNames = new(StringComparer.Ordinal)
    {
        "processId",
        "elementId"
    };

    private static readonly Dictionary<string, string[]> RequiredArgumentsByTool = new(StringComparer.Ordinal)
    {
        ["clear_dp_value"] = ["propertyName"],
        ["click_element"] = ["elementId"],
        ["diagnose_visibility"] = ["elementId"],
        ["drag_and_drop"] = ["sourceElementId", "targetElementId"],
        ["execute_command"] = ["commandName"],
        ["fire_routed_event"] = ["eventName", "elementId"],
        ["focus_element"] = ["elementId"],
        ["force_binding_update"] = ["propertyName"],
        ["get_affected_elements"] = ["propertyName"],
        ["get_binding_value_chain"] = ["propertyName"],
        ["get_dp_metadata"] = ["propertyName"],
        ["get_element_snapshot"] = ["elementId"],
        ["get_event_handlers"] = ["eventName", "elementId"],
        ["get_interaction_readiness"] = ["elementId"],
        ["get_resource_chain"] = ["resourceKey"],
        ["get_state_diff"] = ["snapshotId"],
        ["get_template_tree"] = ["elementId"],
        ["get_triggers"] = ["elementId"],
        ["highlight_element"] = ["elementId"],
        ["modify_viewmodel"] = ["propertyName", "value"],
        ["override_style_setter"] = ["propertyName", "value", "elementId"],
        ["restore_state_snapshot"] = ["snapshotId"],
        ["scroll_to_element"] = ["elementId"],
        ["serialize_to_xaml"] = ["elementId"],
        ["set_dp_value"] = ["propertyName", "value"],
        ["simulate_keyboard"] = ["key"],
        ["wait_for_dp_change"] = ["propertyName"],
        ["wait_for_dp_change_after_mutation"] = ["propertyName", "triggerMutation"],
        ["watch_dp_changes"] = ["propertyName"]
    };

    public static CallToolResult? Validate(
        string? toolName,
        IEnumerable<KeyValuePair<string, JsonElement>>? arguments)
    {
        var argumentArray = arguments?.ToArray();
        if (string.Equals(toolName, "serialize_to_xaml", StringComparison.Ordinal)
            && argumentArray is not null)
        {
            foreach (var argument in argumentArray)
            {
                if (SerializeToXamlArgumentNames.Contains(argument.Key))
                {
                    continue;
                }

                return CreateSerializeToXamlErrorResult(argument.Key);
            }
        }

        if (string.Equals(toolName, "element_screenshot", StringComparison.Ordinal)
            && argumentArray is not null
            && TryGetArgument(argumentArray, "outputPath", out _))
        {
            return CreateElementScreenshotOutputPathErrorResult();
        }

        if (string.Equals(toolName, "drain_events", StringComparison.Ordinal)
            && argumentArray is not null
            && TryGetArgument(argumentArray, "eventTypes", out var eventTypes)
            && eventTypes.ValueKind is not JsonValueKind.Array and not JsonValueKind.Null)
        {
            return CreateDrainEventsEventTypesErrorResult();
        }

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

            if (IsMissingRequiredArgumentValue(requiredArgument, argument))
            {
                missingArgument = requiredArgument;
                return true;
            }
        }

        missingArgument = string.Empty;
        return false;
    }

    private static bool IsMissingRequiredArgumentValue(string requiredArgument, JsonElement argument)
    {
        if (argument.ValueKind == JsonValueKind.Undefined)
        {
            return true;
        }

        if (string.Equals(requiredArgument, "value", StringComparison.Ordinal))
        {
            return false;
        }

        if (argument.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        return argument.ValueKind == JsonValueKind.String && argument.GetString()?.Length == 0;
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

    private static CallToolResult CreateSerializeToXamlErrorResult(string argumentName)
    {
        var payload = CreateSerializeToXamlErrorPayload(argumentName);
        return ToolCallHelper.CreateStructuredErrorResult(
            payload.Error,
            payload.ErrorCode,
            payload.Hint,
            suggestedAction: payload.Hint);
    }

    private static CallToolResult CreateElementScreenshotOutputPathErrorResult()
    {
        const string hint = "element_screenshot file mode is resource-backed. Omit outputPath, call with outputMode='file', then read the returned resourceUri with resources/read.";
        return ToolCallHelper.CreateStructuredErrorResult(
            "Unknown argument 'outputPath' for element_screenshot. File mode returns a server-owned resourceUri and does not write to an agent-selected local path.",
            ToolErrorCode.InvalidArgument.ToString(),
            hint,
            suggestedAction: hint);
    }

    private static CallToolResult CreateDrainEventsEventTypesErrorResult()
    {
        const string hint = "Pass eventTypes as an array such as [\"RoutedEvent\"], pass [\"all\"], or omit eventTypes to drain all buffered event types.";
        return ToolCallHelper.CreateStructuredErrorResult(
            "eventTypes must be an array of event-type strings.",
            ToolErrorCode.InvalidArgument.ToString(),
            hint,
            suggestedAction: hint);
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
            Hint = "Use one of: processId, elementId, query, typeName, controlType, typeNames, elementName, automationId, propertyName, propertyValue, maxResults, maxTraversalNodes, matchMode, typeMatchMode."
        };
    }

    private static ToolErrorPayload CreateSerializeToXamlErrorPayload(string argumentName) =>
        new()
        {
            Error = $"Unknown argument '{argumentName}' for serialize_to_xaml.",
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = "Call get_ui_summary, get_visual_tree, or find_elements first to obtain a current elementId, then call serialize_to_xaml with only processId and elementId."
        };
}
