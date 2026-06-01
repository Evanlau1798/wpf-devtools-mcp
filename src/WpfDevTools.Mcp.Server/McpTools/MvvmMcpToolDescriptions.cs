namespace WpfDevTools.Mcp.Server.McpTools;

internal static class MvvmMcpToolDescriptions
{
    private const string MvvmMetadata = "CATEGORY: MVVM\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string GetViewModel =
        "Use this tool to inspect the current WPF ViewModel and runtime DataContext state for an element.\n\n" +
        MvvmMetadata + "[MVVM] Get the ViewModel (DataContext) of an element. Returns: typeName, " +
        "all properties with their current values, and whether INotifyPropertyChanged is implemented.\n\n" +
        "USE WHEN: Need to inspect ViewModel state; verify DataContext is set correctly.\n" +
        "DO NOT USE: For binding path issues (use get_datacontext_chain instead).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - typeName, implementsINotifyPropertyChanged: boolean,\n" +
        "  - properties: [{ name, value, type, canWrite }]\n\n" +
        "FILTERING: Optional `propertyNames` lets agents request only the ViewModel properties relevant to the current diagnosis.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no datacontext\" -> element has no DataContext set\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\" }";

    public const string GetCommands =
        "Use this tool to inspect WPF ViewModel commands and understand runtime CanExecute state.\n\n" +
        MvvmMetadata + "[MVVM] Get all ICommand properties from the ViewModel. Returns: commandName, " +
        "canExecute status, commandType. Use to check why a button is disabled.\n\n" +
        "USE WHEN: Button is disabled; need to check ICommand.CanExecute status.\n" +
        "DO NOT USE: For non-MVVM apps (commands won't exist).\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - commands: [{\n" +
        "    - name, type, canExecute: boolean\n\n" +
        "Empty commands array means no ICommand properties found.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no datacontext\" -> element has no ViewModel\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\" }";

    public const string ExecuteCommand =
        "Use this tool to execute a WPF ViewModel command without going through a button click path.\n\n" +
        MvvmMetadata + "[MVVM] Execute an ICommand on the ViewModel. Checks CanExecute first. " +
        "Returns execution result.\n\n" +
        "USE WHEN: Testing command logic; simulating button clicks via command.\n" +
        "DO NOT USE: When CanExecute is false (will fail); check with get_commands first.\n\n" +
        "WARNING: This triggers real application logic (saves data, navigates, etc.).\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Omit it or use `compact` (default) to keep only the core command result, use `minimal` for the most concise success confirmation, or use `verbose` for requested/effective input + observedEffect; legacy `standard` remains accepted as a compatibility alias.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - commandName,\n" +
        "  - executed: boolean,\n" +
        "  - canExecute: boolean\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"command not found\" -> verify commandName exists (use get_commands)\n" +
        "- \"cannot execute\" -> CanExecute returned false\n" +
        "- \"commandName required\" -> must specify which command\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"commandName\": \"SaveCommand\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"commandName\": \"SaveCommand\" }";

    public const string GetValidationErrors =
        "Use this tool to inspect WPF validation errors across a runtime element subtree, including inactive tabs.\n\n" +
        MvvmMetadata + "[MVVM] Get validation errors from a WPF element and all its logical and visual descendants (recursive). " +
        "Returns all WPF validation errors (via Validation.GetErrors) aggregated from the target element and its entire subtree.\n\n" +
        "USE WHEN: Form shows validation errors; need to understand validation state; querying a parent to find all child validation errors at once.\n" +
        "BATCH MODE: Provide `elementIds` to inspect multiple scopes in one call. Single-target responses keep the original shape; batch responses return `results` with per-item `elementId` correlation.\n" +
        "DO NOT USE: For binding path errors (use get_binding_errors instead).\n\n" +
        "AGGREGATION: When called on a parent element (e.g., StackPanel, Grid, Window), " +
        "errors from ALL logical and visual descendant elements are collected recursively (max depth: 50, max errors: 200). " +
        "This includes inactive TabItem content and other subtree content that may not currently be visible in the visual tree. " +
        "Each error includes elementType and elementName to identify the source element.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - errorCount: number,\n" +
        "  - errors: [{\n" +
        "    - errorContent: string,\n" +
        "    - isRuleError: boolean,\n" +
        "    - ruleType: string,\n" +
        "    - elementType: string,  // e.g., 'TextBox' - identifies which descendant has the error\n" +
        "    - elementName: string|null  // x:Name of the element, if set\n\n" +
        "Empty errors array means no validation errors in the element or its subtree.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"element not found\" -> verify elementId\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }  \n" +
        "- { \"processId\": 12345, \"elementId\": \"FormPanel\" }  \n" +
        "- { \"processId\": 12345, \"elementId\": \"AgeTextBox\" }";

    public const string ModifyViewModel =
        "Use this tool to modify a WPF ViewModel property during runtime debugging and UI verification.\n\n" +
        MvvmMetadata + "[MVVM] Modify a ViewModel property value via reflection. UI updates automatically " +
        "ONLY if the ViewModel implements INotifyPropertyChanged. Check get_viewmodel first to confirm property name.\n\n" +
        "USE WHEN: Testing UI updates with different ViewModel values; debugging binding issues.\n" +
        "DO NOT USE: For permanent changes (not persisted); when INotifyPropertyChanged is missing (UI won't update).\n\n" +
        "WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Omit it or use `compact` (default) to keep the core mutation result, use `minimal` for success/property/newValue confirmation only, or use `verbose` for requested/effective input + observedEffect; legacy `standard` remains accepted as a compatibility alias.\n\n" +
        "RESPONSE SUMMARY:\n" +
        "  - success: boolean,\n" +
        "  - propertyName, oldValue, newValue, propertyType, canWrite, requestedValueType, convertedValueType\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"no datacontext\" -> element has no ViewModel\n" +
        "- \"property not found\" -> verify propertyName exists (use get_viewmodel)\n" +
        "- \"conversion failed\" -> value cannot be converted to property type\n" +
        "- \"propertyName required\" -> must specify which property\n" +
        "- \"value required\" -> must provide new value\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"propertyName\": \"Name\", \"value\": \"John Doe\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\", \"propertyName\": \"Age\", \"value\": 30 }";
}