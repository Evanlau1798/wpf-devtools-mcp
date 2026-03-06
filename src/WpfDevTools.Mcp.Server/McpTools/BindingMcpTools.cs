using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// MCP SDK wrapper for Binding Diagnostics tools (5 tools).
/// Bridges [McpServerTool] attributes to existing tool ExecuteAsync implementations.
/// </summary>
[McpServerToolType]
public static class BindingMcpTools
{
    [McpServerTool(Name = "get_bindings", ReadOnly = true)]
    [Description(
        "[Binding] Get all DataBindings on an element. Shows binding path, mode " +
        "(OneWay/TwoWay/OneTime), source type, converter, and current status.\n\n" +
        "USE WHEN: You need to inspect binding configuration on a specific element or subtree.\n" +
        "DO NOT USE: recursive=true on large apps without elementId scope (will be slow).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  bindings: [{\n" +
        "    elementId, elementType, propertyName, bindingPath,\n" +
        "    mode: 'OneWay'|'TwoWay'|'OneTime'|'OneWayToSource',\n" +
        "    sourceType, converter, updateSourceTrigger, status\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId from get_visual_tree\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }\n" +
        "- { processId: 12345, recursive: true }")]
    public static Task<CallToolResult> GetBindings(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        bool? recursive = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("recursive", recursive));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetBindingsTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_binding_errors", ReadOnly = true)]
    [Description(
        "[Binding] Get ALL binding errors captured since Inspector connected. " +
        "FIRST tool to use when debugging data display issues.\n\n" +
        "USE WHEN: UI shows blank/wrong data, or you suspect binding path errors.\n" +
        "DO NOT USE: Before calling connect() - errors are only captured after injection.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  errors: [{\n" +
        "    elementType, elementName, propertyName, bindingPath,\n" +
        "    errorType: 'PathError'|'ConverterError'|'ValidationError',\n" +
        "    errorMessage\n" +
        "  }]\n" +
        "}\n\n" +
        "Empty errors array means no binding errors detected.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> GetBindingErrors(
        SessionManager sessionManager,
        int processId,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetBindingErrorsTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_binding_value_chain", ReadOnly = true)]
    [Description(
        "[Binding] Get the complete value resolution chain for a binding on a specific property. " +
        "Shows each step from source to target including converters, fallback values, and StringFormat.\n\n" +
        "USE WHEN: Binding doesn't error but shows unexpected value; need to trace value transformation.\n" +
        "DO NOT USE: Without propertyName - it's required.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  chain: [{\n" +
        "    step: 'Source'|'Converter'|'StringFormat'|'Fallback'|'Target',\n" +
        "    value, type\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no binding\" -> property has no binding (check with get_bindings first)\n" +
        "- \"propertyName required\" -> must specify which property to inspect\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\" }")]
    public static Task<CallToolResult> GetBindingValueChain(
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
            (a, ct) => new GenericPipeTool(sessionManager, "get_binding_value_chain",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var prop = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(a, "propertyName");
                    if (string.IsNullOrEmpty(prop))
                        return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                    return (pid, (object?)new { elementId = eid, propertyName = prop }, null);
                }).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_datacontext_chain", ReadOnly = true)]
    [Description(
        "[Binding] Get the DataContext inheritance chain from an element up to the root. " +
        "Shows each ancestor's DataContext type and value.\n\n" +
        "USE WHEN: Binding path is correct but can't find source; need to understand DataContext inheritance.\n" +
        "DO NOT USE: When binding error already shows the issue (use get_binding_errors first).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  chain: [{\n" +
        "    elementId, elementType, dataContextType, dataContextValue, isInherited\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId is valid\n\n" +
        "Examples:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"ErrorTextBox1\" }")]
    public static Task<CallToolResult> GetDataContextChain(
        SessionManager sessionManager,
        int processId,
        string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => new GetDataContextChainTool(sessionManager).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "force_binding_update", Destructive = true)]
    [Description(
        "[Binding] Force a binding to re-evaluate and transfer the current value. " +
        "Use for UpdateSourceTrigger=Explicit bindings or when the source value changed but the UI didn't update.\n\n" +
        "USE WHEN: UI is stale despite source changes; testing UpdateSourceTrigger=Explicit bindings.\n" +
        "DO NOT USE: As a workaround for broken INotifyPropertyChanged (fix the ViewModel instead).\n\n" +
        "WARNING: This modifies the running app (triggers UpdateSource and UpdateTarget).\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  updated: boolean\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no binding\" -> property has no binding\n" +
        "- \"propertyName required\" -> must specify which property\n\n" +
        "Examples:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\" }")]
    public static Task<CallToolResult> ForceBindingUpdate(
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
            (a, ct) => new GenericPipeTool(sessionManager, "force_binding_update",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var prop = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(a, "propertyName");
                    if (string.IsNullOrEmpty(prop))
                        return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                    return (pid, (object?)new { elementId = eid, propertyName = prop }, null);
                }).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}
