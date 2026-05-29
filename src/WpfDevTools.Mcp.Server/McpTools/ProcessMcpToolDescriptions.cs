namespace WpfDevTools.Mcp.Server.McpTools;

internal static class ProcessMcpToolDescriptions
{
    private const string ProcessMetadata = "CATEGORY: Process\n\n";

    public const string GetProcesses =
        "Use this tool to resolve target ambiguity after connect() reports multiple candidates, or when you explicitly need process metadata before connecting.\n\n" +
        ProcessMetadata + "[Process] List allowlisted WPF processes available for inspection and surface the metadata needed for explicit target selection.\n\n" +
        "POLICY PRECONDITION: WPFDEVTOOLS_MCP_ALLOWED_TARGETS scopes discovery to allowlisted targets only; malformed configured entries return InvalidPolicyConfiguration without target metadata. Targets denied by policy are counted but their process name, window title, executable path, architecture, runtime, and elevation metadata are not disclosed.\n" +
        "USE WHEN: connect() reports multiple candidates; you need architecture/elevation/window metadata before choosing an allowlisted target; you need an explicit visible/all/foreground candidate list before choosing a specific processId.\n" +
        "DO NOT USE: As the default first step when connect() auto-discovery can already resolve the target, repeatedly in a loop (process list changes infrequently), or when connect(windowFilter='all') already expresses the broader auto-discovery scope you need.\n\n" +
        "WINDOW FILTERS: Omit windowFilter for the visible-only default; use windowFilter='all' for hidden/background windows; use windowFilter='foreground' for the active foreground WPF window.\n" +
        ToolDescriptionFragments.ContractGuidance +
        "RESPONSE FIELDS: processes plus per-candidate processId, processName, windowTitle, runtime, requiresElevationToConnect, canConnectFromCurrentServer, and connectionWarning; redactedTargetCount and policyEnvVar report policy-denied targets without exposing their metadata. redactedTargetCount is counted before nameFilter so denied target names are never used as a filtering side channel.\n" +
        "REQUEST OPTIONS: nameFilter narrows candidate names; windowFilter='foreground' scopes enumeration to the active WPF window.\n\n" +
        "EXAMPLES:\n" +
        "- { }\n" +
        "- { \"nameFilter\": \"TestApp\" }\n" +
        "- { \"windowFilter\": \"foreground\" }";

    public const string SelectActiveProcess =
        "Use this tool to explicitly choose which connected WPF process should be used when later tool calls omit processId.\n\n" +
        ProcessMetadata + ToolDescriptionFragments.ConnectPrerequisite +
        "[Process] Set the active connected process for processId-omission workflows.\n\n" +
        "USE WHEN: Multiple WPF sessions are connected and you want one explicit default target.\n" +
        "DO NOT USE: Before connect(processId) has succeeded for the chosen process.\n\n" +
        "SCHEMA SKETCH (not request JSON):\n" +
        "{ success: boolean, processId?: number, message?: string, error?: string, errorCode?: string }\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }";

    public const string GetActiveProcess =
        "Use this tool to verify which connected WPF process is currently active for omitted processId workflows.\n\n" +
        ProcessMetadata + "[Process] Returns the active selected process, if any.\n\n" +
        "USE WHEN: Verifying session state before omitting processId in later calls.\n" +
        "DO NOT USE: As a substitute for connect(), or when you already pass processId explicitly on every later tool call.\n\n" +
        "SCHEMA SKETCH (not request JSON):\n" +
        "{ success: boolean, hasActiveProcess: boolean, processId?: number, selectedAtUtc?: string }\n\n" +
        "EXAMPLES:\n" +
        "- { }";

    public const string Connect =
        "Use this tool to connect to a running WPF process before any inspection tool is used.\n\n" +
        ProcessMetadata + "[Process] Connect to a WPF application by injecting the Inspector DLL. " +
        "MUST be called before any other inspection tool. Returns success status.\n\n" +
        "POLICY PRECONDITION: WPFDEVTOOLS_MCP_ALLOWED_TARGETS must contain the reviewed target's exact local absolute executable path before this tool can attach; unset values fail closed with SecurityError, and malformed configured entries fail closed with InvalidPolicyConfiguration.\n" +
        "USE WHEN: Before using any inspection tools after the target executable is allowlisted. If processId is omitted, connect auto-discovers the allowlisted target when exactly one WPF process is running under the chosen window filter. After connect succeeds, build initial context with get_ui_summary, get_element_snapshot, or get_form_summary before expanding trees or relying on screenshots.\n" +
        "DO NOT USE: As a health check on an already-connected target; use ping instead.\n\n" +
        "AUTO-DISCOVERY: Omit processId to auto-connect when exactly one allowlisted WPF process is available. Omit windowFilter for the visible-only default. Use connect(windowFilter='all') when hidden/background targets must participate in auto-discovery without a separate process listing step. Use selectionStrategy='largest_working_set' only when you intentionally want the largest allowlisted candidate, including connect(selectionStrategy='largest_working_set', windowFilter='all') for broad multi-process auto-selection.\n" +
        "TIMEOUT: Connection attempt times out after 30 seconds.\n" +
        ToolDescriptionFragments.ContractGuidance +
        "RESPONSE FIELDS: processId, processName, windowTitle, autoDiscovered, autoSelected, selectionReason, candidateCount, redactedCandidateCount, policyEnvVar, candidate processes when ambiguity remains, requiresElevationToConnect, canConnectFromCurrentServer, and suggestedAction.\n" +
        "REQUEST OPTIONS: processId selects an explicit target; selectionStrategy controls auto-selection behavior including largest_working_set; windowFilter widens or narrows auto-discovery scope.\n\n" +
        "EXAMPLES:\n" +
        "- { }\n" +
        "- { \"processId\": 12345 }\n" +
        "- { \"selectionStrategy\": \"largest_working_set\" }\n" +
        "- { \"windowFilter\": \"all\" }\n" +
        "- { \"selectionStrategy\": \"largest_working_set\", \"windowFilter\": \"all\" }";

    public const string Ping =
        "Use this tool to verify a connected WPF inspector session is still healthy before deeper runtime inspection.\n\n" +
        ProcessMetadata + ToolDescriptionFragments.ConnectPrerequisite +
        "[Process] Check connection health and measure round-trip latency to the Inspector DLL " +
        "in the target process. Returns latency in milliseconds.\n\n" +
        "USE WHEN: Verifying connection is still alive; measuring IPC performance.\n" +
        "DO NOT USE: Before calling connect() (will fail).\n\n" +
        "TIMEOUT: Ping times out after 5 seconds.\n\n" +
        "SCHEMA SKETCH (not request JSON):\n" +
        "{\n" +
        "  success: boolean,\n" +
        "  status: string,\n" +
        "  processId: number,\n" +
        "  latencyMs: number,\n" +
        "  lastActivity: string\n" +
        "}\n\n" +
        "Typical latency: 0.1-1ms (Named Pipes). >100ms indicates performance issues.\n\n" +
        "ERRORS:\n" +
        "- \"not connected\" -> call connect(processId) first\n" +
        "- \"timeout\" -> process may be frozen or unresponsive\n\n" +
        "EXAMPLES:\n" +
        "- { \"processId\": 12345 }";
}
