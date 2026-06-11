using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ToolCallHelperBoundaryTests
{
    [Fact]
    public async Task ExecuteAndWrapAsync_WithOversizedElementId_ShouldReturnInvalidArgumentWithoutInvokingTool()
    {
        var invoked = false;
        var args = JsonSerializer.SerializeToElement(new
        {
            processId = 12345,
            elementId = new string('E', 257)
        });

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult<object>(new { success = true });
            },
            args,
            CancellationToken.None);

        invoked.Should().BeFalse();
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        structured.GetProperty("error").GetString().Should().Contain("elementId");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithOversizedGenericStringArgument_ShouldReturnInvalidArgumentWithoutInvokingTool()
    {
        var invoked = false;
        var args = JsonSerializer.SerializeToElement(new
        {
            processId = 12345,
            trigger = new string('T', 257)
        });

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult<object>(new { success = true });
            },
            args,
            CancellationToken.None);

        invoked.Should().BeFalse();
        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        structured.GetProperty("error").GetString().Should().Contain("trigger");
    }
}
