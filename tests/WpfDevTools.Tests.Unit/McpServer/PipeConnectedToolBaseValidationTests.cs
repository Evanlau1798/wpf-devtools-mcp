using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

public class PipeConnectedToolBaseValidationTests
{
    [Fact]
    public void ParseCommonParams_WhenElementIdContainsInvalidCharacters_ShouldReturnError()
    {
        var args = ToJsonElement(new { processId = 12345, elementId = "../Window_1" });

        var (processId, elementId, error) = PipeConnectedToolBase.ParseCommonParams(args);

        processId.Should().Be(-1);
        elementId.Should().Be("../Window_1");
        error.Should().NotBeNull();
        JsonSerializer.Serialize(error).Should().Contain("elementId").And.Contain("invalid");
    }

    [Fact]
    public void ParseCommonParams_WhenElementIdIsEmptyString_ShouldReturnError()
    {
        var args = ToJsonElement(new { processId = 12345, elementId = "" });

        var (processId, elementId, error) = PipeConnectedToolBase.ParseCommonParams(args);

        processId.Should().Be(-1);
        elementId.Should().BeEmpty();
        error.Should().NotBeNull();
        JsonSerializer.Serialize(error).Should().Contain("elementId");
    }
}
