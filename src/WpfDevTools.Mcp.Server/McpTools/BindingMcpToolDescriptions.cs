namespace WpfDevTools.Mcp.Server.McpTools;

internal static class BindingMcpToolDescriptions
{
    private const string BindingMetadata = "CATEGORY: Binding\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string GetBindingMismatches =
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
        "- { \"processId\": 12345 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"BasicControlsTab\", \"recursive\": true }\n" +
        "- { \"processId\": 12345, \"recursive\": true, \"includeFramework\": true }";

    public const string GetBindings =
        "Use this tool to inspect WPF bindings on a runtime element or subtree before changing UI or ViewModel state.\n\n" +
        BindingMetadata + "[Binding] Get all DataBindings on an element. Shows binding path, mode " +
        "(OneWay/TwoWay/OneTime), converter, current runtime value, and current status. MultiBinding entries include bindingType and bindingPaths.\n\n" +
        "USE WHEN: You need to inspect binding configuration on a specific element or subtree.\n" +
        "BATCH MODE: Provide `elementIds` to inspect multiple roots in one call. Single-target responses keep the original shape; batch responses return `results` with per-item `elementId` correlation.\n" +
        "DO NOT USE: recursive=true on large apps without elementId scope (will be slow).\n" +
        "FILTERING: Optional `statusFilter` narrows the response to the binding statuses relevant to the current diagnosis.\n\n" +
        ToolDescriptionFragments.ContractGuidance +
        "RESPONSE FIELDS: bindings for single-target inspection, or results/resultCount/successCount/failureCount for batch inspection; each binding can include bindingType, bindingPaths, and currentValue.\n" +
        "REQUEST OPTIONS: elementId or elementIds choose targets; recursive expands subtree inspection; statusFilter narrows returned binding statuses.\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\" }\n" +
        "- { \"processId\": 12345, \"recursive\": true }";

    public const string GetAffectedElements =
        "Use this tool to cheaply narrow down which WPF elements may be affected by a ViewModel property change before doing a full binding-tree verification pass.\n\n" +
        BindingMetadata + "[Binding] Scan a runtime element or subtree and return candidate elements whose binding declaration deterministically matches the supplied property name.\n\n" +
        "USE WHEN: An agent needs a fast candidate list before deciding whether to pay for get_bindings(recursive=true).\n" +
        "DO NOT USE: As proof that a ViewModel property definitely updates those elements, or when you need converter/runtime source certainty instead of best-effort triage.\n" +
        "MATCHING: Exact simple binding-path matches remain best-effort. Nested terminal segments such as `Item.SubItem.Name` and explicit `MultiBinding` child paths can return higher confidence when the declaration itself proves the relationship.\n" +
        "SOURCE MODEL: Bindings that can be proven to stay on the element's local or inherited DataContext chain remain in `affectedElements`. ElementName, RelativeSource, explicit Source, or missing-DataContext cases are surfaced separately through `unsupportedElements` instead of being guessed as ViewModel impact.\n" +
        "VERIFICATION: Successful responses include explicit uncertainty metadata and should usually be followed by get_bindings for precise confirmation.\n\n" +
        "RESPONSE FORMAT:\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName: string,\n" +
        "  viewModelType: string|null,\n" +
        "  confidence: 'low'|'best-effort'|'high',\n" +
        "  matchStrategy: 'simple-path-match'|'terminal-path-match'|'multibinding-child-path-match'|'source-excluded',\n" +
        "  requiresVerification: boolean,\n" +
        "  affectedCount: integer,\n" +
        "  unsupportedCount: integer,\n" +
        "  affectedElements: [{ elementId, elementType, elementName, dataContextType, propertyName, bindingPath, currentValue, status, matchConfidence, sourceClassification }],\n" +
        "  unsupportedElements: [{ elementId, elementType, elementName, dataContextType, propertyName, bindingPath, currentValue, status, matchConfidence, sourceClassification, unsupportedReason }]\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"propertyName required\" -> provide the ViewModel property name you want to match\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId from get_visual_tree or find_elements\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"propertyName\": \"Name\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"ProfilePanel\", \"propertyName\": \"IsEnabled\", \"recursive\": true }\n" +
        "- { \"processId\": 12345, \"propertyName\": \"Name\", \"viewModelType\": \"CustomerViewModel\" }";

    public const string GetBindingErrors =
        "Use this tool to diagnose WPF binding failures behind blank, stale, or incorrect UI data.\n\n" +
        BindingMetadata + "[Binding] Get ALL binding errors captured since Inspector connected. " +
        "FIRST tool to use when debugging data display issues.\n\n" +
        "USE WHEN: UI shows blank/wrong data, or you suspect binding path errors.\n" +
        "DO NOT USE: Before calling connect() - errors are only captured after injection; for validation rule errors use get_validation_errors.\n" +
        "WINDOWING: Optional `maxErrors` and `sinceTimestamp` let agents fetch only the newest or most relevant diagnostics. sinceTimestamp must include UTC `Z` or an explicit offset such as `2026-03-11T12:00:00Z` or `2026-03-11T12:00:00+05:00`. Compact mode is enabled by default to omit the verbose free-form message while preserving structured correlation fields; set `compact=false` when the full message text is required.\n\n" +
        ToolDescriptionFragments.ContractGuidance +
        "RESPONSE FIELDS: errorCount, errors, navigation, nextSteps, and per-error fields such as timestamp, sourceKind, elementId, suggestedElementId, propertyName, and bindingPath. Validation rule errors remain in get_validation_errors.\n" +
        "REQUEST OPTIONS: maxErrors and sinceTimestamp window the diagnostic stream; set compact=false to recover full trace messages; set navigation=false to omit navigation and nextSteps.\n\n" +
        "sourceKind='BindingTrace': error captured from WPF PresentationTraceSources.\n" +
        "sourceKind='BindingExpression': error detected from live BindingExpression status inspection.\n" +
        "Compact mode omits the verbose message field from each error; pass compact=false when you need the full message text.\n" +
        "Empty errors array means no binding errors detected.\n" +
        "Validation rule errors belong in get_validation_errors, not get_binding_errors.\n" +
        "elementId is present when the failing DependencyObject can be identified directly. suggestedElementId is a best-effort match for trace-only errors.\n" +
        "NOTE: sourceId is a numeric trace ID, NOT an elementId. It cannot be used directly as the elementId parameter in other tools.\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }";

    public const string GetBindingValueChain =
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
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\", \"propertyName\": \"Text\" }";

    public const string GetDataContextChain =
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
        "- { \"processId\": 12345 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"ErrorTextBox1\" }";

    public const string ForceBindingUpdate =
        "Use this tool to force a WPF binding to refresh when runtime UI state appears stale.\n\n" +
        BindingMetadata + "[Binding] Force a binding to re-evaluate and transfer the current value. " +
        "Use for UpdateSourceTrigger=Explicit bindings or when the source value changed but the UI didn't update.\n\n" +
        "USE WHEN: UI is stale despite source changes; testing UpdateSourceTrigger=Explicit bindings.\n" +
        "DO NOT USE: As a workaround for broken INotifyPropertyChanged (fix the ViewModel instead).\n\n" +
        "WARNING: This modifies the running app. By default, pushes the current UI target value to the binding source (UpdateSource). Specify direction='Target' to pull the current source value back to the UI target (UpdateTarget).\n\n" +
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
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\", \"propertyName\": \"Text\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\", \"propertyName\": \"Text\", \"direction\": \"Target\" }";
}
