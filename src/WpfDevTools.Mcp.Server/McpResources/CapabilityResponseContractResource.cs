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
                textFallbackDefaultMode = "compact",
                textFallbackModes = new[] { "compact", "full" },
                textFallbackModeEnvironmentVariable = McpServerConfiguration.TextFallbackModeEnvVar,
                textFallbackFullModeSemantics = "full-json-compatibility-for-legacy-text-only-clients",
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
                toolListOutputSchema = "omitted",
                toolListOutputSchemaReason = "Claude tools/list compatibility while structuredContent remains canonical",
                deprecatedAliases = ResponseContractVersion.DeprecatedAliases
            },
            schemaMetadata = ResponseContractSchemaMetadata.GetSchemaMetadata(),
            parameterVocabularies = ResponseContractParameterVocabularies.GetParameterVocabularies(),
            parameterConstraints = ResponseContractParameterConstraints.GetParameterConstraints(),
            registeredToolCoverage = ResponseContractToolCoverage.GetRegisteredToolCoverage(ResponseContractResourceUri),
            highValueTools = ResponseContractToolEntries.GetHighValueTools(ResponseContractResourceUri)
        };

        return JsonSerializer.Serialize(contract, JsonResourceSerializerOptions);
    }
}
