namespace WpfDevTools.Mcp.Server.McpTools;

internal static class DependencyPropertyMcpToolDescriptions
{
    private const string DependencyPropertyMetadata = "CATEGORY: DependencyProperty\n" + ToolDescriptionFragments.ConnectPrerequisite;

    public const string GetDpValueSource =
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
        "SCHEMA SKETCH (not request JSON):\n" +
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
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"propertyName\": \"IsEnabled\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\", \"propertyName\": \"Text\" }\n" +
        "- { \"processId\": 12345, \"elementIds\": [\"SaveButton\", \"NameTextBox\"], \"propertyNames\": [\"IsEnabled\", \"Text\"] }";

    public const string GetDpMetadata =
        "Use this tool to inspect WPF DependencyProperty metadata before changing runtime values.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Get DependencyProperty metadata including default value, " +
        "inherits flag, affects measure/arrange, and coerce/validation callbacks.\n\n" +
        "USE WHEN: You need to understand property behavior at framework level (inheritance, layout impact).\n" +
        "DO NOT USE: For runtime value inspection (use get_dp_value_source instead).\n\n" +
        "SCHEMA SKETCH (not request JSON):\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, defaultValue, inherits, affectsMeasure, affectsArrange,\n" +
        "  hasCoerceCallback, hasValidationCallback\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is a valid DependencyProperty\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"propertyName\": \"IsEnabled\" }\n" +
        "- { \"processId\": 12345, \"propertyName\": \"Visibility\" }";

    public const string SetDpValue =
        "Use this tool to set a WPF DependencyProperty value during runtime debugging and UI verification.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Set a DependencyProperty value at runtime. " +
        "Value is forwarded as raw JSON so numbers, booleans, objects, and strings keep their shape.\n\n" +
        "USE WHEN: Testing UI behavior with different property values; debugging layout/styling issues.\n" +
        "DO NOT USE: For permanent changes (changes are NOT persisted to XAML).\n\n" +
        "WARNING: This modifies the running app. Changes are lost on app restart.\n\n" +
        "EXPRESSION ROLLBACK: If this overwrites a Binding, MultiBinding, or PriorityBinding expression, the response can report `replacedExpression=true` and `capturedRollbackExpression=true`. In the same session, clear_dp_value or restore_state_snapshot can then reapply that captured binding-backed expression. For two-way bindings where source-value rollback also matters, pair this with capture_state_snapshot(viewModelPropertyNames=...) so the source property can be restored deterministically.\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Omit it or use `compact` (default) to keep the core mutation result, use `minimal` for success/property/newValue confirmation only, or use `verbose` for requested/effective input + observedEffect; legacy `standard` remains accepted as a compatibility alias.\n\n" +
        "SCHEMA SKETCH (not request JSON):\n" +
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
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"propertyName\": \"IsEnabled\", \"value\": false }\n" +
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\", \"propertyName\": \"Text\", \"value\": \"New Value\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"Panel\", \"propertyName\": \"Width\", \"value\": 200 }";

    public const string ClearDpValue =
        "Use this tool to clear a local WPF DependencyProperty override and return to runtime defaults or styles.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Clear a DependencyProperty local value, " +
        "reverting it to its inherited, styled, or default value.\n\n" +
        "USE WHEN: Removing overrides applied by set_dp_value; testing default/inherited behavior.\n" +
        "DO NOT USE: On properties without local values (has no effect).\n\n" +
        "WARNING: This modifies the running app. Changes are NOT persisted.\n\n" +
        "EXPRESSION ROLLBACK: When the current local value came from set_dp_value replacing a captured Binding, MultiBinding, or PriorityBinding, this tool re-applies that binding-backed expression in the same session instead of falling back to plain ClearValue semantics. If you also need the original two-way source value restored, capture that ViewModel property in the snapshot workflow.\n\n" +
        "DETAIL MODE: Optional `detail` controls additive metadata. Omit it or use `compact` (default) to keep the core mutation result, use `minimal` for success/property/newValue confirmation only, or use `verbose` for requested/effective input + observedEffect; legacy `standard` remains accepted as a compatibility alias.\n\n" +
        "SCHEMA SKETCH (not request JSON):\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  propertyName, hadLocalValue, clearedValue, newValue,\n" +
        "  baseValueSource, valueType, restoredExpression?: boolean, expressionKind?: string\n" +
        "}\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is valid\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"propertyName\": \"IsEnabled\" }";

    public const string WatchDpChanges =
        "CURRENT BEHAVIOR: No observable effect over STDIO transport. Registration is stored internally only until the next successful drain_events readback or piggyback cycle, and change events are never pushed to the client. Use wait_for_dp_change or poll get_dp_value_source instead.\n\n" +
        "Use this tool to register WPF DependencyProperty watch state before polling for runtime changes.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Register a listener for property value changes. " +
        "Over STDIO, this is registration-only - events are not pushed, and the registration is intentionally transient so shared-session state does not accumulate stale watchers. Any successful drain_events readback ends that transient watch cycle, even when no DpChange payload is returned.\n\n" +
        "USE WHEN: You are preparing for future push-capable transports, or you explicitly want watch registration state.\n" +
        "DO NOT USE: Expecting real-time event delivery over STDIO or expecting watch state to survive unrelated drain_events reads - use wait_for_dp_change or poll get_dp_value_source instead.\n\n" +
        "SCHEMA SKETCH (not request JSON):\n" +
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
        "- { \"processId\": 12345, \"elementId\": \"NameTextBox\", \"propertyName\": \"Text\" }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"propertyName\": \"IsEnabled\" }";

    public const string WaitForDpChange =
        "Use this tool to wait for a WPF DependencyProperty to change over a bounded polling window.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Wait for a DependencyProperty change using polling. " +
        "This tool is designed for STDIO transports where push notifications are not available.\n\n" +
        "USE WHEN: You need to wait for a property transition after an interaction, command, or state mutation without implementing your own polling loop.\n" +
        "DO NOT USE: As a real-time push subscription. This tool polls get_dp_value_source-style state until timeout.\n\n" +
        "READ-ONLY MODE: This call only polls existing runtime state and does not mutate the application.\n\n" +
        "LIMITS: timeoutMs defaults to 5000 and is capped at 25000 so the bounded wait finishes before the inspector host hard request timeout.\n\n" +
        "OPTIONAL MATCHING: Provide `expectedValue` to wait until the property equals a specific value. Omit it to stop on any value change.\n\n" +
        "SERIALIZED-CLIENT WORKFLOW: If your MCP client cannot issue a concurrent mutation while this wait is running, use `wait_for_dp_change_after_mutation` for the destructive mutation-plus-wait workflow instead of overloading this read-only tool.\n\n" +
        "SCHEMA SKETCH (not request JSON):\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  changed: boolean,\n" +
        "  timedOut: boolean,\n" +
        "  observedChange: boolean,\n" +
        "  matchedExpectedValueAtStart: boolean,\n" +
        "  completionReason: 'ExpectedValueAlreadySatisfied'|'ExpectedValueReached'|'ValueChanged'|'TimedOut',\n" +
        "  stateAfterTimeoutUnknown: boolean,\n" +
        "  requiresReconnect: boolean,\n" +
        "  elementId: string|null,\n" +
        "  propertyName: string,\n" +
        "  initialValue,\n" +
        "  initialBaseValueSource,\n" +
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
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"propertyName\": \"IsEnabled\", \"timeoutMs\": 5000 }\n" +
        "- { \"processId\": 12345, \"elementId\": \"StatusText\", \"propertyName\": \"Text\", \"expectedValue\": \"Complete\", \"timeoutMs\": 10000 }\n" +
        "- { \"elementId\": \"NameTextBox\", \"propertyName\": \"Text\", \"pollIntervalMs\": 100, \"timeoutMs\": 2000 }";

    public const string WaitForDpChangeAfterMutation =
        "Use this tool to execute one live runtime mutation and then wait for a WPF DependencyProperty to change over a bounded polling window.\n\n" +
        DependencyPropertyMetadata + "[DependencyProperty] Execute one serialized mutation step, then wait for the resulting DependencyProperty transition using polling. " +
        "This tool is designed for STDIO transports where push notifications are not available and the client cannot issue concurrent calls on the same session.\n\n" +
        "USE WHEN: Your MCP client must mutate and then wait inside one bounded request without sending a second concurrent tool call.\n" +
        "DO NOT USE: For plain read-only waits. Use wait_for_dp_change when you only need to observe existing runtime state.\n\n" +
        "MUTATION STEP: Provide `triggerMutation` using the same shape as one `batch_mutate` step. The server will execute that mutation first, then wait for the property transition. This workflow is destructive because it mutates live runtime state before waiting.\n\n" +
        "LIMITS: timeoutMs defaults to 5000 and is capped at 25000 so the mutation-plus-wait request finishes before the inspector host hard request timeout.\n\n" +
        "OPTIONAL MATCHING: Provide `expectedValue` to wait until the property equals a specific value after the mutation. Omit it to stop on any value change.\n\n" +
        "SCHEMA SKETCH (not request JSON):\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  changed: boolean,\n" +
        "  timedOut: boolean,\n" +
        "  observedChange: boolean,\n" +
        "  matchedExpectedValueAtStart: boolean,\n" +
        "  completionReason: 'ExpectedValueReached'|'ValueChanged'|'TimedOut'|'TriggerMutationTimedOut',\n" +
        "  stateAfterTimeoutUnknown: boolean,\n" +
        "  requiresReconnect: boolean,\n" +
        "  elementId: string|null,\n" +
        "  propertyName: string,\n" +
        "  initialValue,\n" +
        "  initialBaseValueSource,\n" +
        "  currentValue,\n" +
        "  baseValueSource,\n" +
        "  elapsedMs: number,\n" +
        "  pollCount: number\n" +
        "}\n\n" +
        "TRIGGER TIMEOUTS: If `triggerMutation` exceeds the remaining timeout budget, the tool returns `completionReason = 'TriggerMutationTimedOut'`, sets `stateAfterTimeoutUnknown = true`, and sets `requiresReconnect = true` because the server resets the pipe to avoid leaving a stale response queued. Reconnect and re-read state before assuming whether the mutation eventually landed.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"property not found\" -> verify propertyName is a valid DependencyProperty\n" +
        "- \"invalid argument\" -> verify timeoutMs/pollIntervalMs are within allowed bounds\n\n" +
        "EXAMPLES:\n" +
        "- { \"elementId\": \"SearchProbeTextBox\", \"propertyName\": \"Text\", \"expectedValue\": \"Ready\", \"triggerMutation\": { \"tool\": \"modify_viewmodel\", \"args\": { \"propertyName\": \"SearchText\", \"value\": \"Ready\" } } }\n" +
        "- { \"processId\": 12345, \"elementId\": \"SaveButton\", \"propertyName\": \"IsEnabled\", \"triggerMutation\": { \"tool\": \"execute_command\", \"args\": { \"commandName\": \"RefreshCommand\" } }, \"timeoutMs\": 5000 }";
}
