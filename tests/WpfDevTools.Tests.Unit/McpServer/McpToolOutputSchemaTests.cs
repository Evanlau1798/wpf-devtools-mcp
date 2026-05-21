using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolOutputSchemaTests
{
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
}
