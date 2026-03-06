using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for DependencyProperty tools (5 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class DependencyPropertyMcpTools
{
    [McpServerTool(Name = "get_dp_value_source", ReadOnly = true)]
    [Description(
        "[DependencyProperty] Get the value source of a DependencyProperty. " +
        "Returns where the current value comes from: Default, Inherited, Style, Trigger, " +
        "TemplateBinding, LocalValue, or Animation.\n\n" +
        "USE WHEN: Property has unexpected value; need to understand precedence (Style vs LocalValue vs Animation).\n" +
        "DO NOT USE: Without propertyName - it's required.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, currentValue, valueSource: 'Default'|'Inherited'|'Style'|'LocalValue'|'Trigger'|'Animation',\n" +
        "  isAnimated, isCoerced\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is a valid DependencyProperty\n" +
        "- \"propertyName required\" -> must specify which property\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"IsEnabled\" }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\" }")]
    public static Task<CallToolResult> GetDpValueSource(
        SessionManager sessionManager,
        int processId,
        string propertyName,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetDpValueSourceTool>("GetDpValueSourceTool", () => new GetDpValueSourceTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_dp_metadata", ReadOnly = true)]
    [Description(
        "[DependencyProperty] Get DependencyProperty metadata including default value, " +
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
        "Examples:\n" +
        "- { processId: 12345, propertyName: \"IsEnabled\" }\n" +
        "- { processId: 12345, propertyName: \"Visibility\" }")]
    public static Task<CallToolResult> GetDpMetadata(
        SessionManager sessionManager,
        int processId,
        string propertyName,
        string? elementId = null,
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

    [McpServerTool(Name = "set_dp_value", Destructive = true)]
    [Description(
        "[DependencyProperty] Set a DependencyProperty value at runtime. " +
        "Value is a string that gets type-converted.\n\n" +
        "USE WHEN: Testing UI behavior with different property values; debugging layout/styling issues.\n" +
        "DO NOT USE: For permanent changes (changes are NOT persisted to XAML).\n\n" +
        "WARNING: This modifies the running app. Changes are lost on app restart.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, oldValue, newValue\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is valid\n" +
        "- \"conversion failed\" -> value string cannot be converted to property type\n" +
        "- \"value required\" -> must provide value parameter\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"IsEnabled\", value: \"False\" }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\", value: \"New Value\" }")]
    public static Task<CallToolResult> SetDpValue(
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
            (a, ct) => ToolCallHelper.CachedTool<SetDpValueTool>("SetDpValueTool", () => new SetDpValueTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "clear_dp_value", Destructive = true)]
    [Description(
        "[DependencyProperty] Clear a DependencyProperty local value, " +
        "reverting it to its inherited, styled, or default value.\n\n" +
        "USE WHEN: Removing overrides applied by set_dp_value; testing default/inherited behavior.\n" +
        "DO NOT USE: On properties without local values (has no effect).\n\n" +
        "WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, clearedValue, newValue\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is valid\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"IsEnabled\" }")]
    public static Task<CallToolResult> ClearDpValue(
        SessionManager sessionManager,
        int processId,
        string propertyName,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<ClearDpValueTool>("ClearDpValueTool", () => new ClearDpValueTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "watch_dp_changes", ReadOnly = true)]
    [Description(
        "[DependencyProperty] Register a listener for property value changes. " +
        "NOTE: In STDIO transport, change events are NOT pushed. Use get_dp_value_source to poll for changes.\n\n" +
        "USE WHEN: HTTP+SSE transport is available (planned Phase 2+).\n" +
        "DO NOT USE: In STDIO mode - events cannot be pushed; use polling instead.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  watching: boolean,\n" +
        "  note: 'Events require HTTP+SSE transport'\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is valid\n" +
        "- \"transport not supported\" -> STDIO cannot push events\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\" }\n" +
        "- { processId: 12345, elementId: \"SaveButton\", propertyName: \"IsEnabled\" }")]
    public static Task<CallToolResult> WatchDpChanges(
        SessionManager sessionManager,
        int processId,
        string propertyName,
        string? elementId = null,
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
}
