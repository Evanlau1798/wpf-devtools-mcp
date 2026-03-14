using System.Reflection;
using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class McpStdioClientTests
{
    [Fact]
    public void ExtractToolResult_ShouldPreferStructuredContent_WhenAvailable()
    {
        var response = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "result": {
                "content": [
                  {
                    "type": "text",
                    "text": "{\"success\":true,\"nodes\":[{\"elementId\":\"Button_1\"}]}"
                  }
                ],
                "structuredContent": {
                  "success": true,
                  "summaryText": "- Button SaveButton"
                }
              }
            }
            """);

        var method = typeof(McpStdioClient).GetMethod(
            "ExtractToolResult",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        var result = (JsonElement)method!.Invoke(null, [response])!;

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summaryText").GetString().Should().Be("- Button SaveButton");
        result.TryGetProperty("nodes", out _).Should().BeFalse();
    }
}
