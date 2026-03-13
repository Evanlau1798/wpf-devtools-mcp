using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public class ToolCallHelperTimeoutContractTests
{
    [Fact]
    public async Task ExecuteAndWrapAsync_WhenToolTimesOut_ShouldReturnStableTimeoutContract()
    {
        Func<JsonElement?, CancellationToken, Task<object>> slowTool = async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new { success = true };
        };

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            slowTool,
            null,
            CancellationToken.None,
            timeoutSeconds: 1,
            toolName: "test_timeout_tool");

        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("success").GetBoolean().Should().BeFalse();
        structured.GetProperty("errorCode").GetString().Should().Be("Timeout");
        structured.GetProperty("timeoutSeconds").GetInt32().Should().Be(1);
        structured.GetProperty("toolName").GetString().Should().Be("test_timeout_tool");
        structured.GetProperty("suggestedAction").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
