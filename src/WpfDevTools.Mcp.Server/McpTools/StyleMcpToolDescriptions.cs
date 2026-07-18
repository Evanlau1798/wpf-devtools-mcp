namespace WpfDevTools.Mcp.Server.McpTools;

internal static class StyleMcpToolDescriptions
{
    private const string StyleMetadata = "CATEGORY: Style\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string GetAppliedStyles =
        "Use this tool to inspect applied WPF styles and understand runtime appearance sources.\n\n" +
        StyleMetadata + "Get all applied styles on a WPF element. Returns style type, target type, " +
        "Returns style type, setters (property, owner, qualifiedProperty, value), and local resource references.\n\n" +
        "USE WHEN: Element has unexpected appearance; need to understand which styles are applied.\n" +
        "BATCH MODE: Provide `elementIds` to inspect multiple elements in one call. Single-target responses keep the original shape; batch responses return `results` with per-item `elementId` correlation.\n" +
        "COMPACT MODE: Optional `compact=true` returns style summaries without enumerating every setter value.\n" +
        "DO NOT USE: For runtime property values (use get_dp_value_source instead).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - hasStyle: boolean,\n" +
        "  - localResourceReferenceCount: integer,\n" +
        "  - localResourceReferences: [{ property, expressionType, valueSource }],\n" +
        "  - styles: [{\n" +
        "    - styleType: 'Implicit'|'Explicit',\n" +
        "    - targetType,\n" +
        "    - targetTypeFullName,\n" +
        "    - baseValueSource,\n" +
        "    - setters: [{ property, ownerType, ownerTypeFullName, qualifiedProperty, value }]\n\n" +
        "ERRORS:\n" +
        "- \"element not found\" -> verify elementId\n\n";

    public const string GetTriggers =
        "Use this tool to inspect WPF style and template triggers that affect runtime UI state.\n\n" +
        StyleMetadata + "Get all triggers from a WPF element's styles and templates. " +
        "Returns trigger type (Property/Data/Event/MultiTrigger), conditions, and setter actions.\n\n" +
        "USE WHEN: Conditional styling not working; need to understand trigger logic.\n" +
        "DO NOT USE: For static styles (use get_applied_styles instead).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - triggers: [{\n" +
        "    - triggerType: 'Property'|'Data'|'Event'|'MultiTrigger',\n" +
        "    - conditions: [{ property, value }],\n" +
        "    - setters: [{ property, value }]\n\n" +
        "ERRORS:\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"elementId required\" -> must specify which element\n\n";

    public const string GetResourceChain =
        "Use this tool to trace WPF resource lookup order for a runtime XAML resource key.\n\n" +
        StyleMetadata + "Get the resource lookup chain for a XAML resource key. " +
        "Shows which ResourceDictionary at which level (element, window, app, theme) provides the resource.\n\n" +
        "USE WHEN: Resource not found errors; need to understand resource lookup order.\n" +
        "DO NOT USE: Without resourceKey - it's required.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - found: boolean,\n" +
        "  - chain: [{\n" +
        "    - level: 'Element'|'Window'|'Application'|'Theme',\n" +
        "    - dictionarySource, value\n\n" +
        "ERRORS:\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"resourceKey required\" -> must specify which resource to look up\n\n";

    public const string OverrideStyleSetter =
        "Use this tool to override a WPF style setter during runtime debugging without changing XAML.\n\n" +
        StyleMetadata + "Override a style setter value on a WPF element at runtime. " +
        "Applies a local value that takes precedence over the style.\n\n" +
        "USE WHEN: Testing different style values; debugging style precedence issues.\n" +
        "DO NOT USE: For permanent changes (not persisted to XAML).\n\n" +
        "WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
        ToolDescriptionFragments.DetailMode +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - propertyName, oldValue, newValue, valueType\n\n" +
        "ERRORS:\n" +
        "- \"element not found\" -> verify elementId\n" +
        "- \"property not found\" -> verify propertyName is valid\n" +
        "- \"conversion failed\" -> value cannot be converted to property type\n" +
        "- \"propertyName required\" -> must specify which property\n" +
        "- \"value required\" -> must provide new value\n\n";
}
