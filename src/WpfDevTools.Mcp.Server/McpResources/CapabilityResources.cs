using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.McpResources;

[McpServerResourceType]
public static class CapabilityResources
{
    private const string ResponseContractResourceUri = "wpf://contracts/response";

    private static readonly JsonSerializerOptions JsonResourceSerializerOptions = new()
    {
        WriteIndented = true
    };

    [McpServerResource(
        Name = "wpf_capabilities",
        Title = "WPF Capabilities",
        UriTemplate = "wpf://capabilities",
        MimeType = "text/markdown")]
    [Description("Capability summary for discovery, transport limits, server metadata, and high-value workflows.")]
    public static string GetCapabilities() =>
        $"""
        # WPF DevTools MCP Capabilities

        - Server: `wpf-devtools-mcp`
        - Version: `{ServerMetadata.GetDisplayVersion()}`
        - Schema version: `{ServerMetadata.GetSchemaVersion()}`
        - Response contract version: `{ResponseContractVersion.Current}`
        - Transport: `stdio`
        - Tool surface: WPF process discovery, connection, exact-match element search, tree inspection, binding diagnostics, DependencyProperty analysis, MVVM inspection, style/template inspection, interaction, layout, performance, and routed-event diagnostics
        - Prompt surface: workflow entry points for connection, binding diagnosis, command/click diagnosis, elevated-target diagnosis, performance profiling, and secondary-window inspection
        - Resource surface: capability summary, response contract JSON, workflow references, elevated-target limitations, injection failure notes, window/focus limitations, performance profiling notes, and runtime state safety notes
        - Feature flags: `prompts=true`, `resources=true`, `stateSnapshots=true`, `diagnosticNormalization=true`, `elevatedTargetDiagnostics=true`

        ## Recommended workflow shape

        - Start with `connect()` and let auto-discovery pick the single visible WPF target when possible.
        - Call `get_processes(windowFilter)` only when `connect()` reports multiple candidates or when you explicitly need a filtered process list before connecting.
        - Prefer `get_ui_summary`, `get_element_snapshot`, or `get_form_summary` before tree-heavy inspection.
        - After each diagnostic, interaction, or mutation, prefer returned `navigation.recommended`; treat compatibility `nextSteps` as the fallback for older clients before guessing the next tool.

        ## Response contract notes

        - Machine-readable JSON contract resource: `wpf://contracts/response`. Read it when clients need stable field-level metadata for `structuredContent`, `navigation`, `nextSteps`, `contextRefs`, and the `get_binding_errors` `navigation=false` opt-out without relying on prose alone.
        - By default, every tool response includes compatibility `nextSteps`; tools without runtime-computable guidance return `nextSteps: []`.
        - By default, responses also include a `navigation` envelope with `recommended`, `alternatives`, `prefetchTools`, and `contextRefs`.
        - `nextSteps` remains a compatibility field and is derived from `navigation.recommended` unless `get_binding_errors` explicitly receives `navigation=false`.
        - Clients may request `navigation=false` on `get_binding_errors` as an explicit opt-out to omit both `navigation` and compatibility `nextSteps` from a response. Schema-driven clients can rely on that opt-out there because the parameter is advertised in the tool schema today; do not assume other tool schemas expose that parameter unless they advertise it explicitly.
        - v2 adds optional `preconditions`, `expectedOutcome`, `workflowId`, `prefetchTools`, `whyNow`, and `confidence` fields on `nextSteps` entries.
        - `contextRefs` are descriptive JSON only; they are not executable handles or hidden server-side orchestration tokens.
        - `prefetchTools` is advisory only and contains tool names for clients that can load nearby schemas progressively.
        - Compatibility aliases remain in the current response contract to avoid breaking existing clients.
        - Compatibility aliases:
          - `currentValue -> effectiveValue`
          - `typeName -> viewModelType`
          - `avgRenderTime -> averageFrameTime`
          - `count -> totalCount`
          - `renderTimeMs -> renderTime`
        - `get_binding_errors` defaults to compact output; pass `compact=false` when the full free-form binding trace message is required.
        - Mutation and interaction tools default to `detail=compact` when clients want to drop additive normalization metadata without changing core semantics.
        - Use `detail=minimal` when mutation flows only need success/property/newValue confirmation.
        - Use `detail=verbose` when clients need requested/effective input plus observedEffect metadata; legacy `detail=standard` remains accepted as a compatibility alias.

        ## Transport notes

        - STDIO mode is request/response oriented.
        - Watchers and event traces should be treated as polling-oriented workflows, not true push subscriptions.
        - Treat prompt names and resource URIs as the portable discovery contract across clients.
        - Prompt discovery can surface as slash commands in clients that support MCP prompts.
        - Resource discovery can surface through `@resource` references in clients that support MCP resources.

        ## Known capability boundaries

        - Elevated targets require the MCP server itself to run as administrator.
        - Main-window targeting is the default when `elementId` is omitted.
        - Runtime mutations are not persisted to XAML.
        - Snapshot restore currently supports DependencyProperty local values, Binding-backed DependencyProperties captured in the same session, scalar ViewModel values, and focus restoration.
        """;

    [McpServerResource(
        Name = "wpf_response_contract",
        Title = "Response Contract",
        UriTemplate = ResponseContractResourceUri,
        MimeType = "application/json")]
    [Description("Machine-readable JSON contract for structuredContent, navigation, nextSteps, contextRefs, and Claude-compatible tools/list behavior.")]
    public static string GetResponseContract()
    {
        var nextStepEntry = new
        {
            tool = new { type = "string" },
            @params = new { type = "object" },
            reason = new { type = "string" },
            kind = new
            {
                type = "integer",
                allowedValues = new[]
                {
                    new { name = nameof(ToolNextStepKind.Diagnostic), value = (int)ToolNextStepKind.Diagnostic },
                    new { name = nameof(ToolNextStepKind.Action), value = (int)ToolNextStepKind.Action },
                    new { name = nameof(ToolNextStepKind.Verification), value = (int)ToolNextStepKind.Verification },
                    new { name = nameof(ToolNextStepKind.Navigation), value = (int)ToolNextStepKind.Navigation }
                }
            },
            priority = new { type = "integer" },
            preconditions = new { type = "string[]", optional = true },
            expectedOutcome = new { type = "string", optional = true },
            workflowId = new { type = "string", optional = true },
            prefetchTools = new { type = "string[]", optional = true },
            whyNow = new { type = "string", optional = true },
            confidence = new { type = "string", optional = true }
        };

        var contract = new
        {
            server = "wpf-devtools-mcp",
            resourceUri = ResponseContractResourceUri,
            schemaVersion = ServerMetadata.GetSchemaVersion(),
            responseContractVersion = ResponseContractVersion.Current,
            toolCallResult = new
            {
                structuredContentField = "result.structuredContent",
                textFallbackField = "result.content[0].text",
                annotationsField = "result.content[0].annotations",
                automationPreferredField = "result.structuredContent",
                textFallbackSemantics = "compact-summary-only",
                textFallbackIsFullPayload = false,
                structuredContentPreferred = true
            },
            toolPayload = new
            {
                canonicalField = "structuredContent",
                requiredBaseFields = new[] { "success" },
                additiveFields = new[] { "nextSteps", "navigation", "pendingEvents" }
            },
            navigation = new
            {
                field = "navigation",
                includedByDefault = true,
                properties = new
                {
                    recommended = new { type = "ToolNextStep[]" },
                    alternatives = new { type = "ToolNextStep[]" },
                    prefetchTools = new { type = "string[]" },
                    contextRefs = new { type = "ToolNavigationReference[]" }
                },
                optOut = new
                {
                    tool = "get_binding_errors",
                    parameter = "navigation",
                    falseValueOmits = new[] { "navigation", "nextSteps" }
                }
            },
            nextSteps = new
            {
                field = "nextSteps",
                derivedFrom = "navigation.recommended",
                entry = nextStepEntry
            },
            contextRefs = new
            {
                field = "navigation.contextRefs",
                entry = new
                {
                    type = new { type = "string" },
                    additionalProperties = new { type = "json" }
                }
            },
            compatibility = new
            {
                toolListOutputSchema = "omitted",
                toolListOutputSchemaReason = "Claude tools/list compatibility while structuredContent remains canonical",
                deprecatedAliases = ResponseContractVersion.DeprecatedAliases
            },
            highValueTools = new object[]
            {
                new
                {
                    tool = "connect",
                    contractName = "connect-result",
                    canonicalPayloadField = "result.structuredContent",
                    textFallbackField = "result.content[0].text",
                    contractResource = ResponseContractResourceUri,
                    topLevelFields = new[]
                    {
                        "success",
                        "message",
                        "processId",
                        "processName",
                        "windowTitle",
                        "autoDiscovered",
                        "autoSelected",
                        "selectionReason",
                        "candidateCount",
                        "requiresElevationToConnect",
                        "canConnectFromCurrentServer",
                        "suggestedAction",
                        "processes"
                    },
                    nestedResponsePaths = new[]
                    {
                        "processes[].processId",
                        "processes[].processName",
                        "processes[].windowTitle",
                        "processes[].requiresElevationToConnect",
                        "processes[].canConnectFromCurrentServer",
                        "processes[].connectionWarning"
                    },
                    requestParameters = new[]
                    {
                        "processId",
                        "selectionStrategy",
                        "windowFilter"
                    }
                },
                new
                {
                    tool = "get_processes",
                    contractName = "process-list",
                    canonicalPayloadField = "result.structuredContent",
                    textFallbackField = "result.content[0].text",
                    contractResource = ResponseContractResourceUri,
                    topLevelFields = new[]
                    {
                        "success",
                        "processes",
                        "message"
                    },
                    nestedResponsePaths = new[]
                    {
                        "processes[].processId",
                        "processes[].processName",
                        "processes[].windowTitle",
                        "processes[].runtime",
                        "processes[].requiresElevationToConnect",
                        "processes[].canConnectFromCurrentServer",
                        "processes[].connectionWarning"
                    },
                    requestParameters = new[]
                    {
                        "nameFilter",
                        "windowFilter"
                    }
                },
                new
                {
                    tool = "get_bindings",
                    contractName = "binding-inspection",
                    canonicalPayloadField = "result.structuredContent",
                    textFallbackField = "result.content[0].text",
                    contractResource = ResponseContractResourceUri,
                    topLevelFields = new[]
                    {
                        "success",
                        "bindings",
                        "results",
                        "resultCount",
                        "successCount",
                        "failureCount"
                    },
                    nestedResponsePaths = new[]
                    {
                        "bindings[].bindingType",
                        "bindings[].bindingPaths",
                        "bindings[].currentValue",
                        "results[].elementId",
                        "results[].bindings"
                    },
                    requestParameters = new[]
                    {
                        "elementId",
                        "elementIds",
                        "recursive",
                        "statusFilter"
                    }
                },
                new
                {
                    tool = "get_binding_errors",
                    contractName = "binding-errors",
                    canonicalPayloadField = "result.structuredContent",
                    textFallbackField = "result.content[0].text",
                    contractResource = ResponseContractResourceUri,
                    topLevelFields = new[]
                    {
                        "success",
                        "errorCount",
                        "errors",
                        "navigation",
                        "nextSteps"
                    },
                    nestedResponsePaths = new[]
                    {
                        "errors[].timestamp",
                        "errors[].sourceKind",
                        "errors[].elementId",
                        "errors[].suggestedElementId",
                        "errors[].propertyName",
                        "errors[].bindingPath"
                    },
                    requestParameters = new[]
                    {
                        "maxErrors",
                        "sinceTimestamp",
                        "compact",
                        "navigation"
                    }
                },
                new
                {
                    tool = "get_ui_summary",
                    contractName = "ui-summary",
                    canonicalPayloadField = "result.structuredContent",
                    textFallbackField = "result.content[0].text",
                    contractResource = ResponseContractResourceUri,
                    topLevelFields = new[]
                    {
                        "success",
                        "rootElementId",
                        "rootElementType",
                        "rootElementName",
                        "depth",
                        "depthMode",
                        "scopeVisibility",
                        "isCurrentlyVisible",
                        "summaryText",
                        "semanticNodeCount",
                        "nodes"
                    },
                    nestedResponsePaths = new[]
                    {
                        "nodes[].elementId",
                        "nodes[].elementType",
                        "nodes[].elementName",
                        "nodes[].kind",
                        "nodes[].depth",
                        "nodes[].text",
                        "nodes[].currentValue",
                        "nodes[].annotations"
                    },
                    requestParameters = new[]
                    {
                        "elementId",
                        "depth",
                        "depthMode",
                        "summaryOnly"
                    }
                },
                new
                {
                    tool = "get_element_snapshot",
                    contractName = "element-snapshot",
                    canonicalPayloadField = "result.structuredContent",
                    textFallbackField = "result.content[0].text",
                    contractResource = ResponseContractResourceUri,
                    topLevelFields = new[]
                    {
                        "success",
                        "elementId",
                        "elementType",
                        "elementName",
                        "dataContextType",
                        "properties",
                        "bindings",
                        "validationErrors",
                        "style",
                        "layout"
                    },
                    nestedResponsePaths = Array.Empty<string>(),
                    requestParameters = new[]
                    {
                        "elementId",
                        "includeProperties"
                    }
                },
                new
                {
                    tool = "get_form_summary",
                    contractName = "form-summary",
                    canonicalPayloadField = "result.structuredContent",
                    textFallbackField = "result.content[0].text",
                    contractResource = ResponseContractResourceUri,
                    topLevelFields = new[]
                    {
                        "success",
                        "formScope",
                        "scopeVisibility",
                        "isCurrentlyVisible",
                        "inputs",
                        "commands",
                        "summary"
                    },
                    nestedResponsePaths = new[]
                    {
                        "summary.totalInputs",
                        "summary.emptyInputs",
                        "summary.errorCount",
                        "summary.validationSubmittable",
                        "summary.interactionSubmittable",
                        "summary.isSubmittable"
                    },
                    requestParameters = new[]
                    {
                        "elementId",
                        "includeFramework"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(contract, JsonResourceSerializerOptions);
    }

    [McpServerResource(
        Name = "wpf_binding_workflow",
        Title = "Binding Workflow",
        UriTemplate = "wpf://workflows/binding-debug",
        MimeType = "text/markdown")]
    [Description("Canonical workflow for binding errors, value-chain tracing, DataContext inheritance, and validation correlation.")]
    public static string GetBindingWorkflow() =>
        """
        # Binding Debug Workflow

        Use this when UI data is blank, wrong, or stale.

        1. `connect()`
        2. If `connect()` reports multiple candidates, call `get_processes(windowFilter)` and retry `connect(processId)`
        3. `get_binding_errors`
        4. Follow `navigation.recommended` or `nextSteps` from the latest diagnostic result
        5. If the latest diagnostic still leaves the failing element ambiguous, call `get_element_snapshot` for one-call local context
        6. `get_bindings`
        7. `get_binding_value_chain`
        8. `get_datacontext_chain`
        9. `get_validation_errors` when validation may block updates

        Cross-tool semantics:
        - `get_binding_errors` reports failures captured by WPF binding tracing and returns compact payloads by default.
        - `get_binding_value_chain` explains how one binding resolved.
        - `get_datacontext_chain` explains inherited source context.
        - `get_validation_errors` explains rule-based invalid state on the element subtree.
        """;

    [McpServerResource(
        Name = "wpf_elevated_target_limitations",
        Title = "Elevated Target Limitations",
        UriTemplate = "wpf://limitations/elevated-targets",
        MimeType = "text/markdown")]
    [Description("Explains why elevated targets may be discoverable but still reject connect or control operations.")]
    public static string GetElevatedTargetLimitations() =>
        """
        # Elevated Target Limitations

        A non-administrator MCP server can often discover an elevated WPF process, but it cannot inject into or control it.

        Expected behavior:
        - `get_processes` may list the target and mark `isElevated` / `requiresElevationToConnect`.
        - `connect` may fail with `AccessDenied`.
        - Interaction and mutation tools also require matching privilege if the target is elevated.

        In stdio mode, the MCP server inherits the privilege level of the host client process.
        To control an administrator-launched target, start the host client and MCP server as administrator.
        """;

    [McpServerResource(
        Name = "wpf_injection_failure_limitations",
        Title = "Injection Failure Limitations",
        UriTemplate = "wpf://limitations/injection-failures",
        MimeType = "text/markdown")]
    [Description("Summarizes high-signal injection failure causes, packaging constraints, and the current SDK-mode escape hatch.")]
    public static string GetInjectionFailureLimitations() =>
        """
        # Injection Failure Limitations

        Common high-signal failure classes:

        - `architecture mismatch`: MCP server / injector and target process bitness do not match.
        - `access denied`: target privilege is higher than the MCP host process.
        - `timeout`: target process is hung, blocked, or the inspector pipe never became available.
        - `injection-constrained packaging`: single-file and Native AOT targets cannot use raw injection. Trimmed targets remain risky because publish trimming may remove required types needed by either raw injection or SDK-host startup.
        - `security software interference`: AV / endpoint controls may block DLL injection or named pipes.

        Current mitigation guidance:

        - Prefer matching architecture builds.
        - Run the MCP host as administrator for elevated targets.
        - Use SDK mode by starting the target-side host with InspectorSdk.Initialize() and matching transport settings, including the same absolute WPFDEVTOOLS_CERT_DIR value when TLS is enabled. Set WPFDEVTOOLS_AUTH_SECRET and WPFDEVTOOLS_CERT_DIR together on both sides before initialization; if either value is missing, SDK startup now fails closed. This keeps the overall WPF DevTools workflow available for single-file and Native AOT packaging; for trimmed apps it remains the preferred fallback, not a guarantee, and the default-hardened MCP server will not reuse a plaintext SDK host.
        """;


    [McpServerResource(
        Name = "wpf_window_focus_limitations",
        Title = "Window And Focus Limitations",
        UriTemplate = "wpf://limitations/window-focus",
        MimeType = "text/markdown")]
    [Description("Clarifies main-window defaults, multi-window targeting rules, and focus-sensitive behavior.")]
    public static string GetWindowFocusLimitations() =>
        """
        # Window And Focus Limitations

        Multi-window WPF inspection is supported, but targeting rules matter:

        - Omitting `elementId` targets `Application.MainWindow`, not the currently focused window.
        - Use `get_windows` first when dialogs, popups, or tool windows are present.
        - Re-check `get_windows` after focus or visibility changes.
        - Prefer explicit window `elementId` targeting for tree inspection, screenshots, and interactions.
        - `focus_element` and `simulate_keyboard` require the target to be attached to the active rendered visual tree; controls inside inactive tabs must be activated before focus-sensitive actions.

        This avoids accidental inspection of the wrong root when secondary windows are active.
        """;

    [McpServerResource(
        Name = "wpf_performance_profiling_notes",
        Title = "Performance Profiling Notes",
        UriTemplate = "wpf://limitations/performance-profiling",
        MimeType = "text/markdown")]
    [Description("Explains warm-up requirements, polling cadence, and interpretation guidance for performance tools.")]
    public static string GetPerformanceProfiling() =>
        """
        # Performance Profiling Notes

        Performance tools require specific awareness for correct interpretation:

        ## Warm-up
        - `get_render_stats` returns zeros on the first call because the Inspector has not yet observed a render cycle.
        - Call `get_render_stats` once to start monitoring, wait a few seconds, then call again for meaningful data.

        ## Polling cadence
        - Avoid calling `get_render_stats` or `measure_element_render_time` in tight loops.
        - Use one-shot polling: call, wait, call again. The rate limit (300 req/min) applies.

        ## Interpretation
        - `get_visual_count` > 5000 elements may indicate a complex tree worth optimizing.
        - `find_binding_leaks(threshold)` reports binding references above the threshold; high counts may indicate leaked DataContext references.
        - `measure_element_render_time` isolates rendering cost to a single element subtree.

        ## Session scope
        - Performance metrics are session-scoped and reset on reconnect.
        - Monitoring starts when the Inspector DLL is injected and stops on process disconnect.
        """;

    [McpServerResource(
        Name = "wpf_state_safety_notes",
        Title = "Runtime State Safety Notes",
        UriTemplate = "wpf://limitations/state-safety",
        MimeType = "text/markdown")]
    [Description("Summarizes the shipped mutation safety model, snapshot boundaries, and rollback expectations.")]
    public static string GetStateSafetyNotes() =>
        """
        # Runtime State Safety Notes

        Mutation tools change the live application state but do not persist to XAML.

        Current safety model:
        - Changes are process-local and reset on app restart.
        - Snapshot/restore is session-scoped, not durable persistence.
        - `capture_state_snapshot` can capture DependencyProperty local values, scalar ViewModel values, and focus state.
        - Binding-backed DependencyProperties captured in the same session can be restored through the saved restore handle; non-Binding expressions are still surfaced as skipped capability boundaries instead of pretending they can be reconstructed.
        - `restore_state_snapshot` can replay captured local-value DependencyProperties, scalar ViewModel values, and focus state in the same connected session.
        - Prefer minimal mutations and capture a snapshot before a debugging sequence when rollback matters.

        Use this guidance in demos, troubleshooting, and regression flows to avoid cross-test contamination.
        """;
}
