namespace WpfDevTools.Mcp.Server.McpTools;

internal static class StyleMcpToolDescriptions
{
    private const string StyleMetadata = "CATEGORY: Style\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string GetAppliedStyles =
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
        "- { \"processId\": 12345 }";

    public const string GetTriggers =
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
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }";

    public const string GetResourceChain =
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
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"resourceKey\": \"ButtonStyle\" }";

    public const string OverrideStyleSetter =
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
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"propertyName\": \"Background\", \"value\": \"Red\" }";
}
