using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for DependencyProperty tools (6 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class DependencyPropertyMcpTools
{
    private const string DependencyPropertyMetadata = "CATEGORY: DependencyProperty | SAFETY: Check the SDK ReadOnly and Destructive flags before invoking this tool.\n\n";
    private const string RuntimeNavigationGuidance = "FOLLOW-UP GUIDANCE: Successful responses may include runtime-computed `nextSteps`; prefer those returned follow-ups over ad hoc tool guessing.\n\n";
    [McpServerTool(Name = "get_dp_value_source", Title = "Inspect DependencyProperty Value Source", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to inspect the runtime source and precedence of a WPF DependencyProperty value.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Get the value source of a DependencyProperty. " +
        "Returns where the current value comes from: Default, Inherited, Style, Trigger, " +
        "TemplateBinding, LocalValue, or Animation.\n\n" +
        "USE WHEN: Property has unexpected value; need to understand precedence (Style vs LocalValue vs Animation).\n" +
        "BATCH MODE: Provide `elementIds`, `propertyNames`, or both to inspect multiple targets in one call. Single-target responses keep the original shape; batch responses return `results` with per-item correlation fields.\n" +
        "COMPACT MODE: Optional `compact=true` trims each result to the minimum fields agents typically need for precedence decisions.\n" +
        "DO NOT USE: Without propertyName or propertyNames - at least one target property is required.\n\n" +
        "NORMALIZATION: baseValueSource is normalized into stable categories for agents, " +
        "while rawBaseValueSource preserves the original WPF BaseValueSource enum name. " +
        "These two fields MAY legitimately differ: baseValueSource includes additional logic " +
        "(e.g., if ReadLocalValue() returns a value, baseValueSource becomes 'LocalValue' " +
        "even when GetValueSource().BaseValueSource reports 'Default'). " +
        "Use baseValueSource for agent decision-making; use rawBaseValueSource only for advanced debugging.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, currentValue, baseValueSource: 'Default'|'Inherited'|'Style'|'LocalValue'|'Trigger'|'Animation',\n" +
        "  rawBaseValueSource, hadLocalValue, localValue,\n" +
        "  isExpression, isAnimated, isCoerced, isCurrent\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is a valid DependencyProperty\n" +
        "- \"propertyName required\" -> must specify propertyName or propertyNames\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"IsEnabled\" }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\" }\n" +
        "- { processId: 12345, elementIds: [\"SaveButton\", \"NameTextBox\"], propertyNames: [\"IsEnabled\", \"Text\"] }")]
    public static Task<CallToolResult> GetDpValueSource(
        SessionManager sessionManager,
        [Description("Optional DependencyProperty name to inspect, such as Text or IsEnabled. Omit only when propertyNames is provided for batch inspection.")] string? propertyName = null,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        [Description("Optional list of element IDs for batch inspection. Use either elementId or elementIds, not both.")] string[]? elementIds = null,
        [Description("Optional list of property names for batch inspection. Use either propertyName or propertyNames, not both.")] string[]? propertyNames = null,
        [Description("Optional compact response mode. When true, only return the minimum decision-making fields for each result.")] bool compact = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("elementIds", elementIds),
            ("propertyName", propertyName),
            ("propertyNames", propertyNames),
            ("compact", compact));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetDpValueSourceTool>("GetDpValueSourceTool", () => new GetDpValueSourceTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_dp_metadata", Title = "Inspect DependencyProperty Metadata", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to inspect WPF DependencyProperty metadata before changing runtime values.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Get DependencyProperty metadata including default value, " +
        "inherits flag, affects measure/arrange, and coerce/validation callbacks.\n\n" +
        "USE WHEN: You need to understand property behavior at framework level (inheritance, layout impact).\n" +
        "DO NOT USE: For runtime value inspection (use get_dp_value_source instead).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, defaultValue, inherits, affectsMeasure, affectsArrange,\n" +
        "  hasCoerceCallback, hasValidationCallback\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is a valid DependencyProperty\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, propertyName: \"IsEnabled\" }\n" +
        "- { processId: 12345, propertyName: \"Visibility\" }")]
    public static Task<CallToolResult> GetDpMetadata(
        SessionManager sessionManager,
        [Description("DependencyProperty name whose metadata should be returned.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID used to resolve owner-specific metadata.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetDpMetadataTool>("GetDpMetadataTool", () => new GetDpMetadataTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "set_dp_value", Title = "Set WPF DependencyProperty Value", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to set a WPF DependencyProperty value during runtime debugging and UI verification.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Set a DependencyProperty value at runtime. " +
        "Value is forwarded as raw JSON so numbers, booleans, objects, and strings keep their shape.\n\n" +
        "USE WHEN: Testing UI behavior with different property values; debugging layout/styling issues.\n" +
        "DO NOT USE: For permanent changes (changes are NOT persisted to XAML).\n\n" +
        "WARNING: This modifies the running app. Changes are lost on app restart.\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Use `standard` (default) for requested/effective input + observedEffect, or `compact` to keep only the core mutation result.\n\n" +
        RuntimeNavigationGuidance +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, oldValue, newValue, requestedValue,\n" +
        "  baseValueSource, valueType\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is valid\n" +
        "- \"conversion failed\" -> value string cannot be converted to property type\n" +
        "- \"value required\" -> must provide value parameter\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"IsEnabled\", value: false }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\", value: \"New Value\" }\n" +
        "- { processId: 12345, elementId: \"Panel\", propertyName: \"Width\", value: 200 }")]
    public static Task<CallToolResult> SetDpValue(
        SessionManager sessionManager,
        [Description("DependencyProperty name to set at runtime.")] string propertyName,
        [Description("New property value encoded as raw JSON.")] JsonElement value,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        [Description("Optional metadata detail mode: 'standard' (default) or 'compact'.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("value", value),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<SetDpValueTool>("SetDpValueTool", () => new SetDpValueTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken,
            navigationState: ToolCallHelper.ResolveNavigationState(sessionManager, args),
            toolName: "set_dp_value");
    }

    [McpServerTool(Name = "clear_dp_value", Title = "Clear WPF DependencyProperty Value", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to clear a local WPF DependencyProperty override and return to runtime defaults or styles.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Clear a DependencyProperty local value, " +
        "reverting it to its inherited, styled, or default value.\n\n" +
        "USE WHEN: Removing overrides applied by set_dp_value; testing default/inherited behavior.\n" +
        "DO NOT USE: On properties without local values (has no effect).\n\n" +
        "WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Use `standard` (default) for requested/effective input + observedEffect, or `compact` to keep only the core mutation result.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, hadLocalValue, clearedValue, newValue,\n" +
        "  baseValueSource, valueType\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is valid\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"IsEnabled\" }")]
    public static Task<CallToolResult> ClearDpValue(
        SessionManager sessionManager,
        [Description("DependencyProperty name whose local value should be cleared.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        [Description("Optional metadata detail mode: 'standard' (default) or 'compact'.")] string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("detail", detail));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ClearDpValueTool>("ClearDpValueTool", () => new ClearDpValueTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "watch_dp_changes", Title = "Watch WPF DependencyProperty Changes", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to register WPF DependencyProperty watch state before polling for runtime changes.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Register a listener for property value changes. " +
        "CURRENT STDIO BEHAVIOR: registration-only. Change events are NOT pushed to the client; use get_dp_value_source to poll for changes.\n\n" +
        "USE WHEN: You are preparing for future push-capable transports, or you explicitly want watch registration state.\n" +
        "DO NOT USE: Expecting real-time event delivery over STDIO - use polling instead.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  message: string,\n" +
        "  propertyName: string,\n" +
        "  elementId: string|null\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is valid\n" +
        "- \"already watching this property\" -> watcher already exists for this element/property pair\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\" }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"IsEnabled\" }")]
    public static Task<CallToolResult> WatchDpChanges(
        SessionManager sessionManager,
        [Description("DependencyProperty name to watch for runtime changes.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<WatchDpChangesTool>("WatchDpChangesTool", () => new WatchDpChangesTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "wait_for_dp_change", Title = "Wait For WPF DependencyProperty Change", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to wait for a WPF DependencyProperty to change over a bounded polling window.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Wait for a DependencyProperty change using polling. " +
        "This tool is designed for STDIO transports where push notifications are not available.\n\n" +
        "USE WHEN: You need to wait for a property transition after an interaction, command, or state mutation without implementing your own polling loop.\n" +
        "DO NOT USE: As a real-time push subscription. This tool polls get_dp_value_source-style state until timeout.\n\n" +
        "OPTIONAL MATCHING: Provide `expectedValue` to wait until the property equals a specific value. Omit it to stop on any value change.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  changed: boolean,\n" +
        "  timedOut: boolean,\n" +
        "  elementId: string|null,\n" +
        "  propertyName: string,\n" +
        "  initialValue,\n" +
        "  currentValue,\n" +
        "  baseValueSource,\n" +
        "  elapsedMs: number,\n" +
        "  pollCount: number\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is a valid DependencyProperty\n" +
        "- \"invalid argument\" -> verify timeoutMs/pollIntervalMs are within allowed bounds\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"IsEnabled\", timeoutMs: 5000 }\n" +
        "- { processId: 12345, elementId: \"StatusText\", propertyName: \"Text\", expectedValue: \"Complete\", timeoutMs: 10000 }\n" +
        "- { elementId: \"NameTextBox\", propertyName: \"Text\", pollIntervalMs: 100, timeoutMs: 2000 }")]
    public static Task<CallToolResult> WaitForDpChange(
        SessionManager sessionManager,
        [Description("DependencyProperty name to monitor for changes.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the property. Omit for the root window.")] string? elementId = null,
        [Description("Optional timeout in milliseconds. Default: 5000.")] int? timeoutMs = null,
        [Description("Optional polling interval in milliseconds. Default: 200.")] int? pollIntervalMs = null,
        [Description("Optional expected property value. Omit to stop on any value change.")] JsonElement? expectedValue = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("timeoutMs", timeoutMs),
            ("pollIntervalMs", pollIntervalMs),
            ("expectedValue", expectedValue));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<WaitForDpChangeTool>("WaitForDpChangeTool", () => new WaitForDpChangeTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
