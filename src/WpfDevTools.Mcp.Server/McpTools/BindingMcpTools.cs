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
    private const string BindingMetadata = "CATEGORY: Binding | SAFETY: Check the SDK ReadOnly and Destructive flags before invoking this tool.\n\n";

    [McpServerTool(Name = "get_binding_mismatches", Title = "Detect WPF Binding Mismatches", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to detect deterministic WPF binding mismatches before they become silent runtime confusion.\n\n" +
        BindingMetadata + "[Binding] Cross-reference target DependencyProperty types with resolved binding source property types and report deterministic path, type, and nullability mismatches.\n\n" +
        "USE WHEN: A binding looks active but still behaves suspiciously, or you need to catch path/type issues without stitching together get_bindings, get_viewmodel, and get_dp_value_source.\n" +
        "DO NOT USE: For fuzzy guessing. This tool only reports deterministic mismatches and skips unresolved heuristics.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  mismatchCount: integer,\n" +
        "  mismatches: [{\n" +
        "    elementId, elementType, elementName, propertyName, bindingPath,\n" +
        "    targetType, sourceType, converter, origin,\n" +
        "    diagnosis: 'PathMismatch'|'TypeMismatch'|'TypeMismatchWithConverter'|'NullabilityMismatch',\n" +
        "    severity: 'Info'|'Warning'\n" +
        "  }]\n" +
        "}\n\n" +
        "DEFAULT BEHAVIOR: Unnamed framework template parts are excluded by default to reduce noise. Set includeFramework=true to include internal/template-generated framework mismatches.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect() or connect(processId) first\n" +
        "- \"element not found\" -> verify elementId from find_elements or get_visual_tree\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"BasicControlsTab\", recursive: true }\n" +
        "- { processId: 12345, recursive: true, includeFramework: true }")]
    public static Task<CallToolResult> GetBindingMismatches(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect() or connect(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID to inspect. Omit for the root window.")] string? elementId = null,
        [Description("When true, inspect descendant elements under the chosen root as well.")] bool recursive = false,
        [Description("When true, include unnamed framework template/internal mismatch entries that are excluded by default to reduce diagnostic noise.")] bool includeFramework = false,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("recursive", recursive),
            ("includeFramework", includeFramework));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetBindingMismatchesTool>(
                "GetBindingMismatchesTool",
                () => new GetBindingMismatchesTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_bindings", Title = "Inspect WPF Bindings", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to inspect WPF bindings on a runtime element or subtree before changing UI or ViewModel state.\n\n" +
        BindingMetadata + "[Binding] Get all DataBindings on an element. Shows binding path, mode " +
        "(OneWay/TwoWay/OneTime), converter, current runtime value, and current status. MultiBinding entries include bindingType and bindingPaths.\n\n" +
        "USE WHEN: You need to inspect binding configuration on a specific element or subtree.\n" +
        "BATCH MODE: Provide `elementIds` to inspect multiple roots in one call. Single-target responses keep the original shape; batch responses return `results` with per-item `elementId` correlation.\n" +
        "DO NOT USE: recursive=true on large apps without elementId scope (will be slow).\n" +
        "FILTERING: Optional `statusFilter` narrows the response to the binding statuses relevant to the current diagnosis.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  bindings: [{\n" +
        "    elementId, elementType, propertyName, path, bindingType, bindingPaths,\n" +
        "    mode: 'OneWay'|'TwoWay'|'OneTime'|'OneWayToSource',\n" +
        "    converter, updateSourceTrigger, status, currentValue\n" +
        "  }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId from get_visual_tree\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\" }\n" +
        "- { processId: 12345, recursive: true }")]
    public static Task<CallToolResult> GetBindings(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID to inspect. Omit for the root window.")] string? elementId = null,
        [Description("Optional list of element IDs for batch inspection. Use either elementId or elementIds, not both.")] string[]? elementIds = null,
        [Description("When true, inspect descendant elements under the chosen root as well.")] bool? recursive = null,
        [Description("Optional binding status filter such as 'All', 'Active', or 'Error'. Omit to return every binding.")] string? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("elementIds", elementIds),
            ("recursive", recursive),
            ("statusFilter", statusFilter));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetBindingsTool>("GetBindingsTool", () => new GetBindingsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_binding_errors", Title = "Diagnose WPF Binding Errors", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to diagnose WPF binding failures behind blank, stale, or incorrect UI data.\n\n" +
        BindingMetadata + "[Binding] Get ALL binding errors captured since Inspector connected. " +
        "FIRST tool to use when debugging data display issues.\n\n" +
        "USE WHEN: UI shows blank/wrong data, or you suspect binding path errors.\n" +
        "DO NOT USE: Before calling connect() - errors are only captured after injection; for validation rule errors use get_validation_errors.\n" +
        "WINDOWING: Optional `maxErrors` and `sinceTimestamp` let agents fetch only the newest or most relevant diagnostics.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  errorCount: integer,\n" +
        "  errors: [{\n" +
        "    diagnosticKind: 'BindingError',\n" +
        "    sourceKind: 'BindingTrace' | 'BindingExpression',\n" +
        "    severity: 'Error',\n" +
        "    timestamp: string (ISO 8601),\n" +
        "    message: string,\n" +
        "    eventType: string,\n" +
        "    sourceId: integer,\n" +
        "    elementId: string | null,\n" +
        "    suggestedElementId: string | null,\n" +
        "    matchConfidence: 'high' | 'low' | null,\n" +
        "    propertyName: string | null,\n" +
        "    bindingPath: string | null\n" +
        "  }]\n" +
        "}\n\n" +
        "sourceKind='BindingTrace': error captured from WPF PresentationTraceSources.\n" +
        "sourceKind='BindingExpression': error detected from live BindingExpression status inspection.\n" +
        "Empty errors array means no binding errors detected.\n" +
        "Validation rule errors belong in get_validation_errors, not get_binding_errors.\n" +
        "elementId is present when the failing DependencyObject can be identified directly. suggestedElementId is a best-effort match for trace-only errors.\n" +
        "NOTE: sourceId is a numeric trace ID, NOT an elementId. It cannot be used directly as the elementId parameter in other tools.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345 }")]
    public static Task<CallToolResult> GetBindingErrors(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID whose captured binding errors should be returned. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional maximum number of errors to return after filtering. Omit to return the full captured list.")] int? maxErrors = null,
        [Description("Optional ISO-8601 timestamp filter. Only errors at or after this instant are returned.")] string? sinceTimestamp = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("maxErrors", maxErrors),
            ("sinceTimestamp", sinceTimestamp));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetBindingErrorsTool>("GetBindingErrorsTool", () => new GetBindingErrorsTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_binding_value_chain", Title = "Trace WPF Binding Value Chain", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to trace how a WPF binding resolves from source data to the final runtime value.\n\n" +
        BindingMetadata + "[Binding] Get the complete value resolution chain for a binding on a specific property. " +
        "Shows each step from source to target including converters, fallback values, and StringFormat.\n\n" +
        "USE WHEN: Binding doesn't error but shows unexpected value; need to trace value transformation.\n" +
        "DO NOT USE: Without propertyName - it's required.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  hasBinding: boolean,\n" +
        "  propertyName,\n" +
        "  chainLength: integer,\n" +
        "  chain: [{\n" +
        "    step: 'Binding'|'LocalDataContext'|'InheritedDataContext'|'ResolvedSource'|'FinalValue',\n" +
        "    value, type\n" +
        "  }]\n" +
        "}\n\n" +
        "NOTE: Null-DataContext cases include explicit LocalDataContext and ancestor InheritedDataContext diagnostics when available.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no binding\" -> property has no binding (check with get_bindings first)\n" +
        "- \"propertyName required\" -> must specify which property to inspect\n\n" +
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\" }")]
    public static Task<CallToolResult> GetBindingValueChain(
        SessionManager sessionManager,
        [Description("DependencyProperty name whose binding value chain should be traced, such as Text.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the bound property. Omit for the root window.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(
                "get_binding_value_chain",
                () => new GenericPipeTool(sessionManager, "get_binding_value_chain", GenericPipeTool.ExtractElementAndPropertyParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "get_datacontext_chain", Title = "Trace WPF DataContext Chain", OpenWorld = false, ReadOnly = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to trace WPF DataContext inheritance through a runtime element hierarchy.\n\n" +
        BindingMetadata + "[Binding] Get the DataContext inheritance chain from an element up to the root. " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345 }\n" +
        "- { processId: 12345, elementId: \"ErrorTextBox1\" }")]
    public static Task<CallToolResult> GetDataContextChain(
        SessionManager sessionManager,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID whose DataContext inheritance path should be returned.")] string? elementId = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GetDataContextChainTool>("GetDataContextChainTool", () => new GetDataContextChainTool(sessionManager)).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }

    [McpServerTool(Name = "force_binding_update", Title = "Force WPF Binding Update", OpenWorld = false, Destructive = true, UseStructuredContent = false)]
    [Description(
        "Use this tool to force a WPF binding to refresh when runtime UI state appears stale.\n\n" +
        BindingMetadata + "[Binding] Force a binding to re-evaluate and transfer the current value. " +
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
        "EXAMPLES:\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\" }\n" +
        "- { processId: 12345, elementId: \"NameTextBox\", propertyName: \"Text\", direction: \"Target\" }")]
    public static Task<CallToolResult> ForceBindingUpdate(
        SessionManager sessionManager,
        [Description("DependencyProperty name whose binding should be refreshed.")] string propertyName,
        [Description("Optional connected WPF process ID returned by get_processes. Omit after connect(processId) or select_active_process(processId) has established the active process.")] int? processId = null,
        [Description("Optional element ID that owns the binding. Omit for the root window.")] string? elementId = null,
        [Description("Optional update direction: 'Source' (push UI value to ViewModel) or 'Target' (pull ViewModel value to UI). Default: both.")] string? direction = null,
        CancellationToken cancellationToken = default)
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", processId),
            ("elementId", elementId),
            ("propertyName", propertyName),
            ("direction", direction));

        return ToolCallHelper.ExecuteAndWrapAsync(
            (a, ct) => ToolCallHelper.CachedTool<GenericPipeTool>(
                "force_binding_update",
                () => new GenericPipeTool(sessionManager, "force_binding_update", GenericPipeTool.ExtractElementAndPropertyParams)
            ).ExecuteAsync(a, ct),
            args,
            cancellationToken);
    }
}



