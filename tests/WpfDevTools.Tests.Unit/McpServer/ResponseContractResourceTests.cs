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
}