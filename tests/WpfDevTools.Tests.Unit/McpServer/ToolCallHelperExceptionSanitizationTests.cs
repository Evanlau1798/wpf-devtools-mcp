using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public class ToolCallHelperExceptionSanitizationTests
{
    [Fact]
    public async Task ExecuteAndWrapAsync_WhenInvalidOperationOccurs_ShouldNotLeakRawExceptionMessage()
    {
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new InvalidOperationException("secret pipe path and internal state");

        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("OperationError");
        structured.GetProperty("error").GetString().Should().Be("Operation failed");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenArgumentExceptionOccurs_ShouldNotLeakRawExceptionMessage()
    {
        Func<JsonElement?, CancellationToken, Task<object>> faultyTool = (args, ct) =>
            throw new ArgumentException("unexpected raw parameter details", "processId");

        var result = await ToolCallHelper.ExecuteAndWrapAsync(faultyTool, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        structured.GetProperty("error").GetString().Should().Be("Invalid argument");
    }
}
