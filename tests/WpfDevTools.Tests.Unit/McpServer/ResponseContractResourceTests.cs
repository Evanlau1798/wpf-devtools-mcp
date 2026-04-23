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
                "errors[].bindingPath"
            ]);

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
}