using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ResponseContractSchemaMetadataTests
{
    [Fact]
    public void ResponseContractResource_ShouldExposeVerifiableSchemaMetadata()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var root = document.RootElement;

        var metadata = root.GetProperty("schemaMetadata");
        metadata.GetProperty("format").GetString().Should().Be("wpf-response-contract-v1");
        metadata.GetProperty("jsonSchemaDialect").GetString().Should().Be("https://json-schema.org/draft/2020-12/schema");
        metadata.GetProperty("perToolSchemaStrategy").GetProperty("inputSchemas").GetString()
            .Should().Contain("tools/list inputSchema");
        metadata.GetProperty("perToolSchemaStrategy").GetProperty("outputSchemas").GetString()
            .Should().Contain("highValueTools");
        AssertArrayContains(
            metadata.GetProperty("constraintFields"),
            "type",
            "defaultValue",
            "minimum",
            "maximum",
            "allowedValues");
    }

    [Fact]
    public void ResponseContractResource_ShouldExposeNumericParameterConstraints()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var constraints = document.RootElement.GetProperty("parameterConstraints");

        AssertNumericConstraint(constraints, "wait_for_dp_change", "timeoutMs", 5000, 1, 30000);
        AssertNumericConstraint(constraints, "wait_for_dp_change", "pollIntervalMs", 200, 50, 5000);
        AssertNumericConstraint(constraints, "wait_for_dp_change_after_mutation", "timeoutMs", 5000, 1, 30000);
        AssertNumericConstraint(constraints, "get_visual_tree", "depth", null, 0, 100);
        AssertNumericConstraint(constraints, "get_visual_tree", "maxNodes", null, 1, 10000);
        AssertNumericConstraint(constraints, "get_visual_tree", "maxChildrenPerNode", null, 1, 1000);
        AssertNumericConstraint(constraints, "drain_events", "maxEvents", null, 1, null);
        AssertNumericConstraint(constraints, "get_binding_errors", "maxErrors", null, 1, null);
    }

    private static void AssertNumericConstraint(
        JsonElement constraints,
        string toolName,
        string parameterName,
        int? defaultValue,
        int? minimum,
        int? maximum)
    {
        var constraint = constraints.EnumerateArray().Single(entry =>
            entry.GetProperty("tool").GetString() == toolName &&
            entry.GetProperty("parameter").GetString() == parameterName);

        constraint.GetProperty("type").GetString().Should().Be("integer");
        AssertNullableInt(constraint.GetProperty("defaultValue"), defaultValue);
        AssertNullableInt(constraint.GetProperty("minimum"), minimum);
        AssertNullableInt(constraint.GetProperty("maximum"), maximum);
    }

    private static void AssertNullableInt(JsonElement element, int? expected)
    {
        if (expected.HasValue)
        {
            element.GetInt32().Should().Be(expected.Value);
            return;
        }

        element.ValueKind.Should().Be(JsonValueKind.Null);
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
