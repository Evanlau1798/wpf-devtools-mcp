using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.McpResources;

public static partial class CapabilityResources
{
    private static readonly JsonSerializerOptions JsonResourceSerializerOptions = new()
    {
        WriteIndented = true
    };

    [McpServerResource(
        Name = "wpf_response_contract",
        Title = "Response Contract",
        UriTemplate = ResponseContractResourceUri,
        MimeType = "application/json")]
    [Description("Machine-readable JSON contract for structuredContent, navigation, nextSteps, contextRefs, and tools/list outputSchema behavior.")]
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
                fullFidelityPayloadField = "result.structuredContent",
                textFallbackSemantics = "compact-summary-only",
                textFallbackFidelity = "lossy-compatibility-projection",
                textFallbackIsFullPayload = false,
                textFallbackDefaultMode = "compact",
                textFallbackModes = new[] { "compact", "full" },
                textFallbackModeEnvironmentVariable = McpServerConfiguration.TextFallbackModeEnvVar,
                textFallbackFullModeSemantics = "full-json-compatibility-with-large-sensitive-fields-omitted",
                textFallbackFullModeIsFullFidelity = false,
                textFallbackFullModeOmittedFieldFamilies = new[]
                {
                    "base64-images",
                    "raw-xaml-or-markup",
                    "logs-and-traces"
                },
                olderTextOnlyClientGuidance = $"Use result.structuredContent for the full-fidelity JSON payload. Set {McpServerConfiguration.TextFallbackModeEnvVar}=full only as a compatibility override for legacy text-only MCP clients; large or sensitive fields remain omitted from content[0].text.",
                structuredContentPreferred = true
            },
            toolPayload = new
            {
                canonicalField = "structuredContent",
                requiredBaseFields = new[] { "success" },
                additiveFields = new[]
                {
                    "nextSteps",
                    "navigation",
                    "pendingEvents",
                    "pendingEventCount",
                    "droppedEventCount",
                    "cleanupIncomplete",
                    "cleanupFailureMessage",
                    "cleanupFailureType",
                    "pendingEventsOrigin",
                    "pendingEventsMayIncludePriorContext",
                    "pendingEventsSuggestedAction",
                    "pendingEventsAreAdvisory",
                    "pendingEventsSummary"
                }
            },
            policyProfiles = new[]
            {
                new
                {
                    name = "inspect-only",
                    agentUse = "Use for scene-first read workflows that inspect UI text, tree shape, bindings, DP values, and state diffs without runtime mutation.",
                    requiredEnvVars = new[]
                    {
                        new { name = McpServerConfiguration.AllowedTargetsEnvVar, value = "exact local absolute target executable path", purpose = "permits connect() to the reviewed target" },
                        new { name = McpServerConfiguration.AllowSensitiveReadsEnvVar, value = "true", purpose = "permits target UI text, binding values, DP values, scene summaries, and state reads" }
                    },
                    primaryTools = new[] { "connect", "get_ui_summary", "get_element_snapshot", "get_bindings", "get_state_diff" },
                    extraGateGuidance = "Do not request screenshots, ViewModel inspection, or mutation gates unless the task requires them."
                },
                new
                {
                    name = "screenshot-evidence",
                    agentUse = "Use when visual pixel evidence is required after scene-first lookup identifies a concrete elementId.",
                    requiredEnvVars = new[]
                    {
                        new { name = McpServerConfiguration.AllowedTargetsEnvVar, value = "exact local absolute target executable path", purpose = "permits connect() to the reviewed target" },
                        new { name = McpServerConfiguration.AllowScreenshotsEnvVar, value = "true", purpose = "permits element_screenshot at the MCP boundary" }
                    },
                    primaryTools = new[] { "connect", "get_ui_summary", "find_elements", "element_screenshot" },
                    extraGateGuidance = "Pair with sensitive reads only when the workflow also needs target text or runtime values."
                },
                new
                {
                    name = "mutation-safe",
                    agentUse = "Use for runtime-only interactions or temporary mutations guarded by snapshot, diff, and restore.",
                    requiredEnvVars = new[]
                    {
                        new { name = McpServerConfiguration.AllowedTargetsEnvVar, value = "exact local absolute target executable path", purpose = "permits connect() to the reviewed target" },
                        new { name = McpServerConfiguration.AllowDestructiveToolsEnvVar, value = "true", purpose = "permits runtime mutation, interaction, snapshots, event drains, and restore tools" },
                        new { name = McpServerConfiguration.AllowSensitiveReadsEnvVar, value = "true", purpose = "permits state diff and runtime value verification" }
                    },
                    primaryTools = new[] { "capture_state_snapshot", "click_element", "set_dp_value", "get_state_diff", "restore_state_snapshot" },
                    extraGateGuidance = "Capture state before the mutation and restore before ending the workflow."
                },
                new
                {
                    name = "mvvm-inspection",
                    agentUse = "Use when DataContext, ViewModel properties, commands, or command execution are required.",
                    requiredEnvVars = new[]
                    {
                        new { name = McpServerConfiguration.AllowedTargetsEnvVar, value = "exact local absolute target executable path", purpose = "permits connect() to the reviewed target" },
                        new { name = McpServerConfiguration.AllowViewModelInspectionEnvVar, value = "true", purpose = "permits ViewModel and command inspection tools" },
                        new { name = McpServerConfiguration.AllowSensitiveReadsEnvVar, value = "true", purpose = "permits target runtime values commonly needed to validate MVVM state" }
                    },
                    primaryTools = new[] { "get_datacontext_chain", "get_viewmodel", "get_commands", "execute_command", "modify_viewmodel" },
                    extraGateGuidance = "Enable destructive tools too only when executing commands or modifying ViewModel state."
                }
            },
            pendingEventsAdditiveContract = new
            {
                topLevelFields = new[]
                {
                    "pendingEvents",
                    "pendingEventCount",
                    "droppedEventCount",
                    "cleanupIncomplete",
                    "cleanupFailureMessage",
                    "cleanupFailureType",
                    "pendingEventsOrigin",
                    "pendingEventsMayIncludePriorContext",
                    "pendingEventsSuggestedAction",
                    "pendingEventsAreAdvisory",
                    "pendingEventsSummary"
                },
                piggybackScope = "any successful pipe-backed tool response that keeps default piggyback drain behavior",
                illustrativeTools = new[] { "get_binding_errors" },
                pendingEventsOriginField = "pendingEventsOrigin",
                pendingEventsOriginValues = new[] { "piggybackSharedBuffer" },
                pendingEventsMayIncludePriorContextField = "pendingEventsMayIncludePriorContext",
                pendingEventsSuggestedActionField = "pendingEventsSuggestedAction",
                pendingEventsAreAdvisoryField = "pendingEventsAreAdvisory",
                pendingEventsSummaryField = "pendingEventsSummary",
                deterministicDrainTool = "drain_events",
                priorContextGuidance = "When pendingEventsOrigin is piggybackSharedBuffer and pendingEventsMayIncludePriorContext is true, pendingEvents can include prior context from earlier watch activity. Treat pendingEventsAreAdvisory=true and pendingEventsSummary as a compact caveat, and use drain_events directly when a clean action window matters.",
                cleanBufferWorkflow = new[]
                {
                    "call drain_events with the narrowest useful filters before the action",
                    "perform the action or mutation",
                    "call drain_events again to read only the action window"
                }
            },
            errorPayload = new
            {
                requiredBaseFields = new[] { "success", "error" },
                machineReadableCodeField = "errorCode",
                structuredContextField = "errorData",
                canonicalRecoveryField = "recovery",
                compatibilityProjectionFields = new[]
                {
                    "hint",
                    "suggestedAction",
                    "requiresReconnect",
                    "stateAfterTimeoutUnknown",
                    "processId",
                    "timeoutSeconds",
                    "retryAfterSeconds",
                    "retryAfterMs",
                    "retryAfter",
                    "availableTokens",
                    "availableEvents"
                },
                recovery = new
                {
                    field = "recovery",
                    optional = true,
                    properties = new
                    {
                        hint = new { type = "string", optional = true },
                        suggestedAction = new { type = "string", optional = true },
                        requiresReconnect = new { type = "boolean", optional = true },
                        stateAfterTimeoutUnknown = new { type = "boolean", optional = true },
                        processId = new { type = "integer", optional = true },
                        timeoutSeconds = new { type = "integer", optional = true },
                        retryAfterSeconds = new { type = "integer", optional = true },
                        retryAfterMs = new { type = "integer", optional = true },
                        retryAfter = new { type = "string", optional = true },
                        availableTokens = new { type = "integer", optional = true },
                        availableEvents = new { type = "string[]", optional = true }
                    }
                }
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
                toolListOutputSchema = "advertised",
                toolListOutputSchemaReason = "High-value tools expose an exact closed output schema in tools/list. Other tools intentionally use the generic structured payload schema. wpf://contracts/response remains the detailed narrative contract and versioning source.",
                outputSchemaPublication = new
                {
                    canonicalLocation = "tools/list",
                    reason = "Native tools/list outputSchema is authoritative for schema-driven clients: high-value tools expose exact closed schemas, while other tools use the generic structured payload schema. This resource remains the stable detailed contract for WPF-specific structuredContent fields, semantics, and compatibility versioning."
                },
                versioning = new
                {
                    currentVersionField = "responseContractVersion",
                    additiveChangesRequireVersionBump = false,
                    breakingChangesRequireVersionBump = true,
                    breakingChanges = new[]
                    {
                        "remove-field",
                        "rename-field",
                        "change-field-type",
                        "change-required-field-semantics"
                    }
                },
                deprecatedAliases = ResponseContractVersion.DeprecatedAliases
            },
            schemaMetadata = ResponseContractSchemaMetadata.GetSchemaMetadata(),
            parameterVocabularies = ResponseContractParameterVocabularies.GetParameterVocabularies(),
            parameterConstraints = ResponseContractParameterConstraints.GetParameterConstraints(),
            toolManifest = new
            {
                resourceUri = ToolManifestResourceUri,
                generatedFrom = nameof(ModelContextProtocol.Server.McpServerToolAttribute),
                usage = "Canonical source-generated tool surface for runtime tools/list parity, documentation coverage, and policy capability classification."
            },
            registeredToolCoverage = ResponseContractToolCoverage.GetRegisteredToolCoverage(ResponseContractResourceUri),
            highValueTools = ResponseContractToolEntries.GetHighValueTools(ResponseContractResourceUri)
        };

        return JsonSerializer.Serialize(contract, JsonResourceSerializerOptions);
    }
}
