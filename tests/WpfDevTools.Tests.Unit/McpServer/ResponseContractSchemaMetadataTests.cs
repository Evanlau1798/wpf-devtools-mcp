using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Shared.Configuration;

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
            .Should().Contain("tools/list outputSchema");
        AssertArrayContains(
            metadata.GetProperty("constraintFields"),
            "type",
            "defaultValue",
            "minimum",
            "maximum",
            "maxItems",
            "maxLength",
            "allowedValues");
    }

    [Fact]
    public void ResponseContractResource_ShouldExposeNumericParameterConstraints()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var constraints = document.RootElement.GetProperty("parameterConstraints");

        AssertNumericConstraint(constraints, "wait_for_dp_change", "timeoutMs", 5000, 1, 25000);
        AssertNumericConstraint(constraints, "wait_for_dp_change", "pollIntervalMs", 200, 50, 5000);
        AssertNumericConstraint(constraints, "wait_for_dp_change_after_mutation", "timeoutMs", 5000, 1, 25000);
        AssertNumericConstraint(constraints, "connect", "processId", null, 1, int.MaxValue);
        AssertNumericConstraint(constraints, "get_visual_tree", "depth", null, 0, 100);
        AssertNumericConstraint(constraints, "get_visual_tree", "maxNodes", TreeTraversalDefaults.DefaultMaxNodes, 1, 10000);
        AssertNumericConstraint(constraints, "get_visual_tree", "maxChildrenPerNode", TreeTraversalDefaults.DefaultMaxChildrenPerNode, 1, 1000);
        AssertNumericConstraint(constraints, "get_logical_tree", "maxNodes", TreeTraversalDefaults.DefaultMaxNodes, 1, 10000);
        AssertNumericConstraint(constraints, "get_logical_tree", "maxChildrenPerNode", TreeTraversalDefaults.DefaultMaxChildrenPerNode, 1, 1000);
        AssertNumericConstraint(constraints, "get_template_tree", "maxNodes", TreeTraversalDefaults.DefaultMaxNodes, 1, 10000);
        AssertNumericConstraint(constraints, "get_template_tree", "maxChildrenPerNode", TreeTraversalDefaults.DefaultMaxChildrenPerNode, 1, 1000);
        AssertNumericConstraint(constraints, "find_elements", "maxTraversalNodes", TreeTraversalDefaults.DefaultMaxNodes, 1, 10000);
        AssertNumericConstraint(constraints, "get_namescope", "maxNodes", TreeTraversalDefaults.DefaultNameScopeMaxNodes, 1, 10000);
        AssertNumericConstraint(constraints, "trace_routed_events", "maxEvents", null, 1, null);
        AssertNumericConstraint(constraints, "drain_events", "maxEvents", null, 1, null);
        AssertNumericConstraint(constraints, "get_binding_errors", "maxErrors", null, 1, null);
        AssertNumericConstraint(constraints, "get_ui_summary", "depth", null, 0, 100);
        AssertNumericConstraint(constraints, "element_screenshot", "maxWidth", null, 1, int.MaxValue);
        AssertNumericConstraint(constraints, "element_screenshot", "maxHeight", null, 1, int.MaxValue);
    }

    [Fact]
    public void ResponseContractResource_ShouldExposeStateSnapshotArrayAndStringConstraints()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var constraints = document.RootElement.GetProperty("parameterConstraints");

        AssertArrayConstraint(constraints, "capture_state_snapshot", "propertyNames", 100);
        AssertArrayConstraint(constraints, "capture_state_snapshot", "viewModelPropertyNames", 100);
        AssertStringConstraint(constraints, "capture_state_snapshot", "propertyNames[]", 256);
        AssertStringConstraint(constraints, "capture_state_snapshot", "viewModelPropertyNames[]", 256);
    }

    [Fact]
    public void ResponseContractResource_ShouldExposeEnumParameterConstraints()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var constraints = document.RootElement.GetProperty("parameterConstraints");

        AssertEnumConstraint(constraints, "get_processes", "windowFilter", "visible", "all", "foreground");
        AssertEnumConstraint(constraints, "connect", "selectionStrategy", "single_only", "largest_working_set");
        AssertEnumConstraint(constraints, "connect", "windowFilter", "visible", "all", "foreground");
        AssertEnumConstraint(constraints, "get_ui_summary", "depthMode", "semantic", "visual");
        AssertEnumConstraint(constraints, "find_elements", "typeMatchMode", "exact", "assignable");
        AssertEnumConstraint(constraints, "element_screenshot", "outputMode", "metadata", "file", "base64");
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

    private static void AssertEnumConstraint(
        JsonElement constraints,
        string toolName,
        string parameterName,
        params string[] expectedValues)
    {
        var constraint = constraints.EnumerateArray().Single(entry =>
            entry.GetProperty("tool").GetString() == toolName &&
            entry.GetProperty("parameter").GetString() == parameterName);

        constraint.GetProperty("type").GetString().Should().Be("string");
        constraint.GetProperty("defaultValue").GetString().Should().Be(expectedValues[0]);
        var values = constraint.GetProperty("allowedValues")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .ToArray();
        values.Should().BeEquivalentTo(expectedValues);
    }

    private static void AssertArrayConstraint(
        JsonElement constraints,
        string toolName,
        string parameterName,
        int maxItems)
    {
        var constraint = constraints.EnumerateArray().Single(entry =>
            entry.GetProperty("tool").GetString() == toolName &&
            entry.GetProperty("parameter").GetString() == parameterName);

        constraint.GetProperty("type").GetString().Should().Be("array");
        constraint.GetProperty("maxItems").GetInt32().Should().Be(maxItems);
    }

    private static void AssertStringConstraint(
        JsonElement constraints,
        string toolName,
        string parameterName,
        int maxLength)
    {
        var constraint = constraints.EnumerateArray().Single(entry =>
            entry.GetProperty("tool").GetString() == toolName &&
            entry.GetProperty("parameter").GetString() == parameterName);

        constraint.GetProperty("type").GetString().Should().Be("string");
        constraint.GetProperty("maxLength").GetInt32().Should().Be(maxLength);
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
