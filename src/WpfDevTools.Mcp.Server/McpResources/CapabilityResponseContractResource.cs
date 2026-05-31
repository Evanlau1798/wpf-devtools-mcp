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
                    "pendingEventsMayIncludePriorContext"
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
                    "pendingEventsMayIncludePriorContext"
                },
                piggybackScope = "any successful pipe-backed tool response that keeps default piggyback drain behavior",
                illustrativeTools = new[] { "get_binding_errors" },
                pendingEventsOriginField = "pendingEventsOrigin",
                pendingEventsOriginValues = new[] { "piggybackSharedBuffer" },
                pendingEventsMayIncludePriorContextField = "pendingEventsMayIncludePriorContext"
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
