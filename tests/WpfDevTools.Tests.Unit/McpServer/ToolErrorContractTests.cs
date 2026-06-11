using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

public class ToolErrorContractTests
{
    [Fact]
    public void ParseCommonParams_WhenProcessIdMissing_ShouldReturnStructuredErrorCodeAndHint()
    {
        var (_, _, error) = PipeConnectedToolBase.ParseCommonParams(ToJsonElement(new { elementId = "Button_1" }));

        error.Should().NotBeNull();
        var json = JsonSerializer.SerializeToElement(error);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("MissingRequiredParameter");
        json.GetProperty("hint").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task GetDpValueSourceTool_WhenNotConnected_ShouldReturnMachineRecoverableError()
    {
        var tool = new GetDpValueSourceTool(new SessionManager());

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId = 12345, propertyName = "Width" }),
            CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("NotConnected");
        json.GetProperty("hint").GetString().Should().Contain("connect");
    }
}
