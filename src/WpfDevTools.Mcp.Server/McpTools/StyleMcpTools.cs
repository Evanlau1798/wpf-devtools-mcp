using System.Text.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Style/Template tools.
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class StyleMcpTools
{
    private const string StyleMetadata = "CATEGORY: Style\n" + ToolDescriptionFragments.ConnectPrerequisite;
    [McpServerTool(Name = "get_applied_styles", Title = "Inspect WPF Applied Styles", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to inspect applied WPF styles and understand runtime appearance sources.\n\n" +
        StyleMetadata + "[Style] Get all applied styles on a WPF element. Returns style type, target type, " +
        "setters (property+value), whether it's an implicit or explicit style, and localResourceReferences when appearance comes from a local resource expression instead of a Style.\n\n" +
        "USE WHEN: Element has unexpected appearance; need to understand which styles are applied.\n" +
        "BATCH MODE: Provide `elementIds` to inspect multiple elements in one call. Single-target responses keep the original shape; batch responses return `results` with per-item `elementId` correlation.\n" +
        "COMPACT MODE: Optional `compact=true` returns style summaries without enumerating every setter value.\n" +
        "DO NOT USE: For runtime property values (use get_dp_value_source instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  hasStyle: boolean,\n" +
        "  localResourceReferenceCount: integer,\n" +
        "  localResourceReferences: [{ property, expressionType, valueSource }],\n" +
        "  styles: [{\n" +
        "    styleType: 'Implicit'|'Explicit',\n" +
        "    targetType,\n" +
        "    setters: [{ property, value }]\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }\n" +
        "- { \"processId\": 12345 }")]
    public static Task<CallToolResult> GetAppliedStyles(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose applied styles should be returned. Omit for the root window.")] string? elementId = null,
        [Description("Optional list of element IDs for batch inspection. Use either elementId or elementIds, not both.")] string[]? elementIds = null,
        [Description("Optional compact response mode. When true, return style summaries instead of full setter payloads.")] bool compact = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("elementIds", elementIds),
            ("compact", compact));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetAppliedStylesTool>(sessionManager, "GetAppliedStylesTool", () => new GetAppliedStylesTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_triggers", Title = "Inspect WPF Triggers", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to inspect WPF style and template triggers that affect runtime UI state.\n\n" +
        StyleMetadata + "[Style] Get all triggers from a WPF element's styles and templates. " +
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
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }")]
    public static Task<CallToolResult> GetTriggers(
        SessionManager sessionManager,
        [Description("Element ID whose style and template triggers should be listed.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetTriggersTool>(sessionManager, "GetTriggersTool", () => new GetTriggersTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_resource_chain", Title = "Trace WPF Resource Chain", OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to trace WPF resource lookup order for a runtime XAML resource key.\n\n" +
        StyleMetadata + "[Style] Get the resource lookup chain for a XAML resource key. " +
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
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"resourceKey\": \"PrimaryBrush\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"resourceKey\": \"ButtonStyle\" }")]
    public static Task<CallToolResult> GetResourceChain(
        SessionManager sessionManager,
        [Description("XAML resource key to resolve, such as PrimaryBrush or ButtonStyle.")] string resourceKey,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional starting element ID for resource lookup. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("resourceKey", resourceKey));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetResourceChainTool>(sessionManager, "GetResourceChainTool", () => new GetResourceChainTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "override_style_setter", Title = "Override WPF Style Setter", OpenWorld = false, Destructive = true, UseStructuredContent = true)]
    [Description(
        "Use this tool to override a WPF style setter during runtime debugging without changing XAML.\n\n" +
        StyleMetadata + "[Style] Override a style setter value on a WPF element at runtime. " +
        "Applies a local value that takes precedence over the style.\n\n" +
        "USE WHEN: Testing different style values; debugging style precedence issues.\n" +
        "DO NOT USE: For permanent changes (not persisted to XAML).\n\n" +
        "WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Omit it or use `compact` (default) to keep only the core mutation result. Use `minimal` for success/property/newValue confirmation only, `verbose` for requested/effective input + observedEffect, or legacy `standard` as a compatibility alias.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, oldValue, newValue, valueType\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"property not found\" -> verify propertyName is valid\n" +
        "- \"conversion failed\" -> value cannot be converted to property type\n" +
        "- \"propertyName required\" -> must specify which property\n" +
        "- \"value required\" -> must provide new value\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"propertyName\": \"Background\", \"value\": \"Red\" }")]
    public static Task<CallToolResult> OverrideStyleSetter(
        SessionManager sessionManager,
        [Description("Style-backed property name to override at runtime.")] string propertyName,
        [Description("New property value encoded as raw JSON.")] JsonElement value,
        [Description("Required element ID whose style setter should be overridden.")] string elementId,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [AllowedValues("compact", "minimal", "verbose", "standard")]
        [Description("Optional metadata detail mode: omit or use 'compact' (default), use 'minimal' for success/property/newValue confirmation only, use 'verbose' for full additive metadata, or 'standard' as a compatibility alias.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("value", value),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<OverrideStyleSetterTool>(sessionManager, "OverrideStyleSetterTool", () => new OverrideStyleSetterTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "override_style_setter");
    }
}
