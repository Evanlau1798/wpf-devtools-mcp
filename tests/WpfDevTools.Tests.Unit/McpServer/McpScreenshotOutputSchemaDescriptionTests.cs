using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpScreenshotOutputSchemaDescriptionTests
{
    [Fact]
    public void ElementScreenshotSchema_ShouldExplainMetadataPixelSemantics()
    {
        var tool = new Tool
        {
            Name = "element_screenshot",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        McpToolOutputSchemas.Apply(tool);

        var properties = tool.OutputSchema!.Value.GetProperty("properties");
        properties.GetProperty("outputMode").GetProperty("description").GetString()
            .Should().Contain("metadata mode intentionally returns no pixel bytes");
        properties.GetProperty("rendered").GetProperty("description").GetString()
            .Should().Contain("false for metadata mode");
        properties.GetProperty("byteLength").GetProperty("description").GetString()
            .Should().Contain("zero for metadata mode");
    }
}
