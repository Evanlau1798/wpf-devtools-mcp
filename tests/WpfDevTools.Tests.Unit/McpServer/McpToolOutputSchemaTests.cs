using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolOutputSchemaTests
{
    public static readonly TheoryData<string, string[]> HighValueToolFields = new()
    {
        { "connect", ["processId", "processName", "windowTitle", "autoDiscovered", "candidateCount", "redactedCandidateCount", "suggestedAction"] },
        { "get_processes", ["processes", "redactedTargetCount", "policyEnvVar"] },
        { "get_ui_summary", ["rootElementId", "rootElementType", "rootElementName", "depth", "semanticNodeCount", "summaryText", "nodes"] },
        { "get_element_snapshot", ["elementId", "elementType", "elementName", "dataContextType", "properties", "bindings", "validationErrors", "style", "layout"] },
        { "get_bindings", ["bindings", "results", "resultCount", "successCount", "failureCount"] },
        { "get_binding_errors", ["errorCount", "errors", "navigation", "nextSteps"] },
        { "capture_state_snapshot", ["snapshotId", "snapshotSummary"] },
        { "get_state_diff", ["snapshotId", "trigger", "diff"] },
        { "restore_state_snapshot", ["snapshotId", "restoredDependencyProperties", "restoredViewModelProperties", "skippedDependencyProperties", "skippedViewModelProperties"] },
        { "batch_mutate", ["results", "mutationCount", "successfulMutationCount", "failedMutationCount", "snapshotId", "diff", "rollback", "recovery"] },
        { "element_screenshot", ["elementId", "screenshotId", "resourceUri", "expiresAtUtc", "outputMode", "width", "height", "mimeType", "base64Image"] }
    };

    [Fact]
    public void StructuredPayloadSchema_ShouldPlaceContextRefsUnderNavigationOnly()
    {
        var tool = new Tool
        {
            Name = "schema_probe",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        McpToolOutputSchemas.Apply(tool);

        var properties = tool.OutputSchema!.Value.GetProperty("properties");
        properties.TryGetProperty("contextRefs", out _).Should().BeFalse(
            "contextRefs are emitted as navigation.contextRefs in structuredContent, not as a top-level result field");

        var navigation = properties.GetProperty("navigation");
        navigation.GetProperty("type").GetString().Should().Be("object");
        navigation.GetProperty("properties")
            .TryGetProperty("contextRefs", out var contextRefs)
            .Should().BeTrue();
        contextRefs.GetProperty("type").GetString().Should().Be("array");
    }

    [Theory]
    [MemberData(nameof(HighValueToolFields))]
    public void HighValueTools_ShouldExposeToolSpecificClosedOutputSchemas(string toolName, string[] expectedFields)
    {
        var tool = CreateTool(toolName);

        McpToolOutputSchemas.Apply(tool);

        var schema = tool.OutputSchema!.Value;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse(
            $"{toolName} should advertise a tool-specific schema instead of the shared open schema");
        schema.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("success");

        var properties = schema.GetProperty("properties");
        properties.TryGetProperty("success", out _).Should().BeTrue();
        properties.TryGetProperty("structuredContent", out _).Should().BeFalse();
        foreach (var expectedField in expectedFields)
        {
            properties.TryGetProperty(expectedField, out _).Should().BeTrue(
                $"{toolName} should publish its '{expectedField}' structuredContent field in tools/list outputSchema");
        }
    }

    [Fact]
    public void NonHighValueTools_ShouldKeepSharedOpenOutputSchema()
    {
        var tool = CreateTool("schema_probe");

        McpToolOutputSchemas.Apply(tool);

        tool.OutputSchema!.Value.GetProperty("additionalProperties").GetBoolean().Should().BeTrue();
    }

    private static Tool CreateTool(string name)
        => new()
        {
            Name = name,
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };
}
