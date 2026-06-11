using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolOutputSchemaBindingTests
{
    [Fact]
    public void GetBindingsOutputSchema_ShouldExposeRuntimeBindingEntryFields()
    {
        var bindingProperties = GetArrayItemProperties("get_bindings", "bindings");

        AssertFields(bindingProperties,
            "propertyName",
            "bindingType",
            "bindingPaths",
            "converter",
            "currentValue");

        var batchBindingProperties = GetNestedArrayItemProperties("get_bindings", ["results", "bindings"]);
        AssertFields(batchBindingProperties,
            "propertyName",
            "bindingType",
            "bindingPaths",
            "converter",
            "currentValue");
    }

    [Fact]
    public void GetBindingErrorsOutputSchema_ShouldExposeRuntimeDiagnosticFields()
    {
        var errorProperties = GetArrayItemProperties("get_binding_errors", "errors");

        AssertFields(errorProperties,
            "diagnosticKind",
            "sourceKind",
            "severity",
            "timestamp",
            "eventType",
            "sourceId",
            "suggestedElementId",
            "matchConfidence",
            "propertyName",
            "bindingPath");
    }

    private static void AssertFields(JsonElement properties, params string[] expectedFields)
    {
        foreach (var expectedField in expectedFields)
        {
            properties.TryGetProperty(expectedField, out _).Should().BeTrue(
                $"binding outputSchema should expose runtime field '{expectedField}'");
        }
    }

    private static JsonElement GetArrayItemProperties(string toolName, string fieldName)
        => CreateToolSchema(toolName)
            .GetProperty("properties")
            .GetProperty(fieldName)
            .GetProperty("items")
            .GetProperty("properties");

    private static JsonElement GetNestedArrayItemProperties(string toolName, string[] path)
    {
        var properties = CreateToolSchema(toolName).GetProperty("properties");
        for (var index = 0; index < path.Length; index++)
        {
            properties = index == path.Length - 1
                ? properties.GetProperty(path[index]).GetProperty("items").GetProperty("properties")
                : properties.GetProperty(path[index]).GetProperty("items").GetProperty("properties");
        }

        return properties;
    }

    private static JsonElement CreateToolSchema(string toolName)
    {
        var tool = new Tool
        {
            Name = toolName,
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };
        McpToolOutputSchemas.Apply(tool);
        return tool.OutputSchema!.Value;
    }
}
