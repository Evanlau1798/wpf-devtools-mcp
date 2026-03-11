using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class FindElementsToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnStructuredNotConnectedError()
    {
        var tool = new FindElementsTool(new SessionManager());

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 53001,
            typeName = "Button"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("NotConnected");
    }
}
