using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Style/Template tools (4 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class StyleMcpTools
{
    [McpServerTool(Name = "get_applied_styles", ReadOnly = true)]
    [Description(
        "[Style] Get all applied styles on a WPF element. Returns style type, target type, " +
        "setters (property+value), and whether it's an implicit or explicit style.\n\n" +
        "USE WHEN: Element has unexpected appearance; need to understand which styles are applied.\n" +
        "DO NOT USE: For runtime property values (use get_dp_value_source instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  styles: [{\n" +
        "    styleType: 'Implicit'|'Explicit',\n" +
        "    targetType,\n" +
        "    setters: [{ property, value }]\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> GetAppliedStyles(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetAppliedStylesTool>("GetAppliedStylesTool", () => new GetAppliedStylesTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_triggers", ReadOnly = true)]
    [Description(
        "[Style] Get all triggers from a WPF element's styles and templates. " +
        "Returns trigger type (Property/Data/Event/MultiTrigger), conditions, and setter actions.\n\n" +
        "USE WHEN: Conditional styling not working; need to understand trigger logic.\n" +
        "DO NOT USE: For static styles (use get_applied_styles instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  triggers: [{\n" +
        "    triggerType: 'Property'|'Data'|'Event'|'MultiTrigger',\n" +
        "    conditions: [{ property, value }],\n" +
        "    setters: [{ property, value }]\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"elementId required\" -> must specify which element\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\" }")]
    public static Task<CallToolResult> GetTriggers(
        SessionManager sessionManager,
        int processId,
        string elementId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetTriggersTool>("GetTriggersTool", () => new GetTriggersTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_resource_chain", ReadOnly = true)]
    [Description(
        "[Style] Get the resource lookup chain for a XAML resource key. " +
        "Shows which ResourceDictionary at which level (element, window, app, theme) provides the resource.\n\n" +
        "USE WHEN: Resource not found errors; need to understand resource lookup order.\n" +
        "DO NOT USE: Without resourceKey - it's required.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  found: boolean,\n" +
        "  chain: [{\n" +
        "    level: 'Element'|'Window'|'Application'|'Theme',\n" +
        "    dictionarySource, value\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"resourceKey required\" -> must specify which resource to look up\n\n" +
        "Examples:\n" +
        "- { processId: 12345, resourceKey: \"PrimaryBrush\" }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", resourceKey: \"ButtonStyle\" }")]
    public static Task<CallToolResult> GetResourceChain(
        SessionManager sessionManager,
        int processId,
        string resourceKey,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("resourceKey", resourceKey));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetResourceChainTool>("GetResourceChainTool", () => new GetResourceChainTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "override_style_setter", Destructive = true)]
    [Description(
        "[Style] Override a style setter value on a WPF element at runtime. " +
        "Applies a local value that takes precedence over the style.\n\n" +
        "USE WHEN: Testing different style values; debugging style precedence issues.\n" +
        "DO NOT USE: For permanent changes (not persisted to XAML).\n\n" +
        "WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, oldValue, newValue\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"property not found\" -> verify propertyName is valid\n" +
        "- \"conversion failed\" -> value cannot be converted to property type\n" +
        "- \"propertyName required\" -> must specify which property\n" +
        "- \"value required\" -> must provide new value\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"Background\", value: \"Red\" }")]
    public static Task<CallToolResult> OverrideStyleSetter(
        SessionManager sessionManager,
        int processId,
        string propertyName,
        string value,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("value", value));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<OverrideStyleSetterTool>("OverrideStyleSetterTool", () => new OverrideStyleSetterTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
