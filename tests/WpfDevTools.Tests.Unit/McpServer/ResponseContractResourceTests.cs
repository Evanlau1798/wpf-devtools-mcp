using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ResponseContractResourceTests
{
    [Fact]
    public void ResponseContractResource_ShouldExposeMachineReadableFieldsForToolResults()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var root = document.RootElement;

        root.GetProperty("resourceUri").GetString().Should().Be("wpf://contracts/response");
        root.GetProperty("schemaVersion").GetString().Should().Be(ServerMetadata.GetSchemaVersion());
        root.GetProperty("responseContractVersion").GetString().Should().Be(ResponseContractVersion.Current);
        root.GetProperty("toolCallResult").GetProperty("structuredContentField").GetString().Should().Be("result.structuredContent");
        root.GetProperty("toolCallResult").GetProperty("textFallbackField").GetString().Should().Be("result.content[0].text");
        root.GetProperty("toolCallResult").GetProperty("annotationsField").GetString().Should().Be("result.content[0].annotations");
        root.GetProperty("toolPayload").GetProperty("canonicalField").GetString().Should().Be("structuredContent");
        AssertArrayContains(
            root.GetProperty("toolPayload").GetProperty("additiveFields"),
            "pendingEvents",
            "pendingEventCount",
            "droppedEventCount",
            "cleanupIncomplete",
            "cleanupFailureMessage",
            "cleanupFailureType",
            "pendingEventsOrigin",
            "pendingEventsMayIncludePriorContext");

        var pendingEventsAdditiveContract = root.GetProperty("pendingEventsAdditiveContract");
        AssertArrayContains(
            pendingEventsAdditiveContract.GetProperty("topLevelFields"),
            "pendingEvents",
            "pendingEventCount",
            "droppedEventCount",
            "cleanupIncomplete",
            "cleanupFailureMessage",
            "cleanupFailureType",
            "pendingEventsOrigin",
            "pendingEventsMayIncludePriorContext");
        pendingEventsAdditiveContract.GetProperty("piggybackScope").GetString().Should().Contain("default piggyback drain behavior");
        AssertArrayContains(pendingEventsAdditiveContract.GetProperty("illustrativeTools"), "get_binding_errors");
        pendingEventsAdditiveContract.GetProperty("pendingEventsOriginField").GetString().Should().Be("pendingEventsOrigin");
        pendingEventsAdditiveContract.GetProperty("pendingEventsMayIncludePriorContextField").GetString().Should().Be("pendingEventsMayIncludePriorContext");
        root.GetProperty("navigation").GetProperty("field").GetString().Should().Be("navigation");
        root.GetProperty("nextSteps").GetProperty("field").GetString().Should().Be("nextSteps");
    }

    [Fact]
    public void ResponseContractResource_ShouldDescribeNavigationOptOutAndCompatibilityAliases()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var root = document.RootElement;

        var optOut = root.GetProperty("navigation").GetProperty("optOut");
        optOut.GetProperty("tool").GetString().Should().Be("get_binding_errors");
        optOut.GetProperty("parameter").GetString().Should().Be("navigation");
        optOut.GetProperty("falseValueOmits")[0].GetString().Should().Be("navigation");
        optOut.GetProperty("falseValueOmits")[1].GetString().Should().Be("nextSteps");

        var kindValues = root.GetProperty("nextSteps").GetProperty("entry").GetProperty("kind").GetProperty("allowedValues");
        kindValues.GetArrayLength().Should().Be(4);
        kindValues[0].GetProperty("name").GetString().Should().Be(nameof(ToolNextStepKind.Diagnostic));
        kindValues[0].GetProperty("value").GetInt32().Should().Be((int)ToolNextStepKind.Diagnostic);

        var aliases = root.GetProperty("compatibility").GetProperty("deprecatedAliases");
        aliases.GetArrayLength().Should().Be(ResponseContractVersion.DeprecatedAliases.Count);
        aliases[0].GetString().Should().Be("currentValue -> effectiveValue");
        root.GetProperty("compatibility").GetProperty("toolListOutputSchema").GetString().Should().Be("omitted");
    }

    [Fact]
    public void ResponseContractResource_ShouldDistinguishEnvelopeFieldsAndListHighValueToolContracts()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var root = document.RootElement;

        var toolCallResult = root.GetProperty("toolCallResult");
        toolCallResult.GetProperty("automationPreferredField").GetString().Should().Be("result.structuredContent");
        toolCallResult.GetProperty("textFallbackSemantics").GetString().Should().Be("compact-summary-only");
        toolCallResult.GetProperty("textFallbackIsFullPayload").GetBoolean().Should().BeFalse();
        toolCallResult.GetProperty("textFallbackDefaultMode").GetString().Should().Be("compact");
        toolCallResult.GetProperty("textFallbackModeEnvironmentVariable").GetString()
            .Should().Be(McpServerConfiguration.TextFallbackModeEnvVar);
        AssertArrayContains(toolCallResult.GetProperty("textFallbackModes"), "compact", "full");

        var highValueTools = root.GetProperty("highValueTools");
        highValueTools.GetArrayLength().Should().BeGreaterThanOrEqualTo(7);

        AssertHighValueToolContract(
            highValueTools,
            "connect",
            "connect-result",
            topLevelFields: [
                "processId",
                "autoDiscovered",
                "requiresElevationToConnect",
                "suggestedAction"
            ],
            requestParameters: [
                "processId",
                "selectionStrategy",
                "windowFilter"
            ],
            nestedResponsePaths: [
                "processes[].canConnectFromCurrentServer",
                "processes[].connectionWarning"
            ]);

        AssertTopLevelFieldsDoNotContain(highValueTools, "connect", "selectionStrategy", "windowFilter");

        AssertHighValueToolContract(
            highValueTools,
            "get_processes",
            "process-list",
            topLevelFields: [
                "processes",
                "message"
            ],
            requestParameters: [
                "nameFilter",
                "windowFilter"
            ],
            nestedResponsePaths: [
                "processes[].canConnectFromCurrentServer",
                "processes[].connectionWarning"
            ]);

        AssertTopLevelFieldsDoNotContain(highValueTools, "get_processes", "windowFilter", "canConnectFromCurrentServer", "connectionWarning");

        AssertHighValueToolContract(
            highValueTools,
            "get_bindings",
            "binding-inspection",
            topLevelFields: [
                "bindings",
                "results",
                "resultCount"
            ],
            requestParameters: [
                "recursive",
                "statusFilter"
            ],
            nestedResponsePaths: [
                "bindings[].bindingType",
                "bindings[].bindingPaths",
                "bindings[].currentValue"
            ]);

        AssertTopLevelFieldsDoNotContain(highValueTools, "get_bindings", "statusFilter");

        AssertHighValueToolContract(
            highValueTools,
            "get_binding_errors",
            "binding-errors",
            topLevelFields: [
                "errorCount",
                "errors",
                "pendingEvents",
                "pendingEventCount",
                "droppedEventCount",
                "cleanupIncomplete",
                "cleanupFailureMessage",
                "cleanupFailureType",
                "pendingEventsOrigin",
                "pendingEventsMayIncludePriorContext",
                "navigation"
            ],
            requestParameters: [
                "maxErrors",
                "sinceTimestamp",
                "compact",
                "navigation"
            ],
            nestedResponsePaths: [
                "errors[].timestamp",
                "errors[].sourceKind",
                "errors[].bindingPath",
                "pendingEvents[].eventType",
                "pendingEvents[].elementId",
                "pendingEvents[].propertyName"
            ]);

        AssertHighValueToolContract(
            highValueTools,
            "drain_events",
            "pending-runtime-events",
            topLevelFields: [
                "pendingEventCount",
                "droppedEventCount",
                "cleanupIncomplete",
                "cleanupFailureMessage",
                "cleanupFailureType",
                "pendingEvents"
            ],
            requestParameters: [
                "maxEvents",
                "eventTypes",
                "elementId",
                "sinceTimestamp"
            ],
            nestedResponsePaths: [
                "pendingEvents[].eventType",
                "pendingEvents[].elementId",
                "pendingEvents[].propertyName",
                "pendingEvents[].eventName",
                "pendingEvents[].timestampUtc"
            ]);

        var drainEventsContract = highValueTools
            .EnumerateArray()
            .Single(entry => entry.GetProperty("tool").GetString() == "drain_events");
        AssertArrayContains(
            drainEventsContract.GetProperty("errorDataFields"),
            "errorData.replayPreserved",
            "errorData.bufferedReplayEventCount");
        AssertArrayContains(
            drainEventsContract.GetProperty("recoveryFields"),
            "recovery.hint",
            "recovery.suggestedAction");
        drainEventsContract.GetProperty("semantics").GetProperty("replayPreservedOnLiveFailure").GetBoolean().Should().BeTrue();

        AssertHighValueToolContract(
            highValueTools,
            "get_ui_summary",
            "ui-summary",
            topLevelFields: [
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
            ],
            requestParameters: [
                "elementId",
                "depth",
                "depthMode",
                "summaryOnly"
            ],
            nestedResponsePaths: [
                "nodes[].elementId",
                "nodes[].elementType",
                "nodes[].annotations"
            ]);

        AssertTopLevelFieldsDoNotContain(highValueTools, "get_ui_summary", "summaryOnly");

        AssertHighValueToolContract(
            highValueTools,
            "get_element_snapshot",
            "element-snapshot",
            topLevelFields: [
                "elementId",
                "elementType",
                "elementName",
                "dataContextType",
                "properties",
                "bindings",
                "validationErrors",
                "style",
                "layout"
            ],
            requestParameters: [
                "elementId",
                "includeProperties"
            ]);

        AssertHighValueToolContract(
            highValueTools,
            "get_form_summary",
            "form-summary",
            topLevelFields: [
                "formScope",
                "scopeVisibility",
                "isCurrentlyVisible",
                "inputs",
                "commands",
                "summary"
            ],
            requestParameters: [
                "elementId",
                "includeFramework"
            ],
            nestedResponsePaths: [
                "summary.totalInputs",
                "summary.emptyInputs",
                "summary.errorCount",
                "summary.validationSubmittable",
                "summary.interactionSubmittable",
                "summary.isSubmittable"
            ]);

        AssertTopLevelFieldsDoNotContain(highValueTools, "get_form_summary", "validationSubmittable", "interactionSubmittable", "isSubmittable");

        AssertHighValueToolContract(
            highValueTools,
            "element_screenshot",
            "element-screenshot",
            topLevelFields: [
                "width",
                "height",
                "format",
                "rendered",
                "byteLength",
                "screenshotId",
                "outputMode",
                "fileName",
                "localPathRedacted",
                "sha256",
                "base64Image"
            ],
            requestParameters: [
                "elementId",
                "outputMode",
                "maxWidth",
                "maxHeight"
            ]);

        var screenshotContract = highValueTools
            .EnumerateArray()
            .Single(entry => entry.GetProperty("tool").GetString() == "element_screenshot");
        var outputVariants = screenshotContract.GetProperty("outputVariants");
        AssertOutputVariant(
            outputVariants,
            "metadata",
            rendered: false,
            fields: ["success", "width", "height", "format", "rendered", "byteLength"]);
        AssertOutputVariant(
            outputVariants,
            "file",
            rendered: true,
            fields: ["success", "screenshotId", "outputMode", "fileName", "localPathRedacted", "sha256", "width", "height", "format", "rendered", "byteLength"]);
        AssertOutputVariant(
            outputVariants,
            "base64",
            rendered: true,
            fields: ["success", "base64Image", "width", "height", "format", "rendered", "byteLength"]);
    }

    [Fact]
    public void ResponseContractResource_ShouldPublishCompatibilityStrategyForOutputContracts()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var root = document.RootElement;

        var compatibility = root.GetProperty("compatibility");
        compatibility.GetProperty("toolListOutputSchema").GetString().Should().Be("omitted");
        compatibility.GetProperty("outputSchemaPublication").GetProperty("canonicalLocation").GetString().Should().Be("highValueTools");

        var versioning = compatibility.GetProperty("versioning");
        versioning.GetProperty("currentVersionField").GetString().Should().Be("responseContractVersion");
        versioning.GetProperty("additiveChangesRequireVersionBump").GetBoolean().Should().BeFalse();
        versioning.GetProperty("breakingChangesRequireVersionBump").GetBoolean().Should().BeTrue();
        AssertArrayContains(
            versioning.GetProperty("breakingChanges"),
            "remove-field",
            "rename-field",
            "change-field-type",
            "change-required-field-semantics");
    }

    [Fact]
    public void ResponseContractResource_ShouldDescribeCanonicalErrorRecoveryAndClosedParameterVocabularies()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var root = document.RootElement;

        var errorPayload = root.GetProperty("errorPayload");
        errorPayload.GetProperty("canonicalRecoveryField").GetString().Should().Be("recovery");
        errorPayload.GetProperty("structuredContextField").GetString().Should().Be("errorData");
        AssertArrayContains(
            errorPayload.GetProperty("compatibilityProjectionFields"),
            "hint",
            "suggestedAction",
            "requiresReconnect",
            "processId",
            "timeoutSeconds",
            "retryAfterSeconds",
            "retryAfter",
            "availableTokens",
            "availableEvents");

        var recovery = errorPayload.GetProperty("recovery");
        recovery.GetProperty("field").GetString().Should().Be("recovery");
        recovery.GetProperty("properties").GetProperty("suggestedAction").GetProperty("type").GetString().Should().Be("string");
        recovery.GetProperty("properties").GetProperty("requiresReconnect").GetProperty("type").GetString().Should().Be("boolean");
        recovery.GetProperty("properties").GetProperty("retryAfterSeconds").GetProperty("type").GetString().Should().Be("integer");

        var parameterVocabularies = root.GetProperty("parameterVocabularies");
        parameterVocabularies.GetArrayLength().Should().BeGreaterThanOrEqualTo(5);

        AssertParameterVocabulary(
            parameterVocabularies,
            "windowFilter",
            "visible",
            ["visible", "all", "foreground"],
            ["connect", "get_processes"]);

        AssertParameterVocabulary(
            parameterVocabularies,
            "selectionStrategy",
            "single_only",
            ["single_only", "largest_working_set"],
            ["connect"]);

        AssertParameterVocabulary(
            parameterVocabularies,
            "depthMode",
            "semantic",
            ["semantic", "visual"],
            ["get_ui_summary"]);

        AssertParameterVocabulary(
            parameterVocabularies,
            "detail",
            "compact",
            ["compact", "minimal", "verbose"],
            [
                "click_element",
                "execute_command",
                "modify_viewmodel",
                "set_dp_value",
                "clear_dp_value",
                "fire_routed_event",
                "override_style_setter"
            ]);

        AssertParameterVocabulary(
            parameterVocabularies,
            "outputMode",
            "metadata",
            ["base64", "metadata", "file"],
            ["element_screenshot"]);
    }

    private static void AssertHighValueToolContract(
        JsonElement highValueTools,
        string toolName,
        string contractName,
        string[] topLevelFields,
        string[]? requestParameters = null,
        string[]? nestedResponsePaths = null)
    {
        var toolContract = highValueTools
            .EnumerateArray()
            .Single(entry => entry.GetProperty("tool").GetString() == toolName);

        toolContract.GetProperty("contractName").GetString().Should().Be(contractName);
        toolContract.GetProperty("canonicalPayloadField").GetString().Should().Be("result.structuredContent");
        toolContract.GetProperty("textFallbackField").GetString().Should().Be("result.content[0].text");
        toolContract.GetProperty("contractResource").GetString().Should().Be("wpf://contracts/response");

        AssertArrayContains(toolContract.GetProperty("topLevelFields"), topLevelFields);

        if (requestParameters is not null)
        {
            AssertArrayContains(toolContract.GetProperty("requestParameters"), requestParameters);
        }

        if (nestedResponsePaths is not null)
        {
            AssertArrayContains(toolContract.GetProperty("nestedResponsePaths"), nestedResponsePaths);
        }
    }

    private static void AssertTopLevelFieldsDoNotContain(
        JsonElement highValueTools,
        string toolName,
        params string[] excludedFields)
    {
        var toolContract = highValueTools
            .EnumerateArray()
            .Single(entry => entry.GetProperty("tool").GetString() == toolName);

        var fields = toolContract.GetProperty("topLevelFields")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();

        foreach (var excludedField in excludedFields)
        {
            fields.Should().NotContain(excludedField);
        }
    }

    private static void AssertOutputVariant(
        JsonElement outputVariants,
        string outputMode,
        bool rendered,
        string[] fields)
    {
        var variant = outputVariants
            .EnumerateArray()
            .Single(entry => entry.GetProperty("outputMode").GetString() == outputMode);

        variant.GetProperty("rendered").GetBoolean().Should().Be(rendered);
        AssertArrayContains(variant.GetProperty("fields"), fields);
    }

    private static void AssertArrayContains(JsonElement arrayElement, params string[] expectedValues)
    {
        var values = arrayElement
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();

        foreach (var expectedValue in expectedValues)
        {
            values.Should().Contain(expectedValue);
        }
    }

    private static void AssertParameterVocabulary(
        JsonElement parameterVocabularies,
        string parameterName,
        string defaultValue,
        string[] allowedValues,
        string[] tools)
    {
        var vocabulary = parameterVocabularies
            .EnumerateArray()
            .Single(entry => entry.GetProperty("parameter").GetString() == parameterName);

        vocabulary.GetProperty("defaultValue").GetString().Should().Be(defaultValue);
        AssertArrayContains(vocabulary.GetProperty("allowedValues"), allowedValues);
        AssertArrayContains(vocabulary.GetProperty("tools"), tools);
    }
}
