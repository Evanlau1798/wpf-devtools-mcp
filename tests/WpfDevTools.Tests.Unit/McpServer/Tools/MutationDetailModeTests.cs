using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class MutationDetailModeTests
{
    [Fact]
    public void Parse_WhenArgumentMissing_ShouldDefaultToCompact()
    {
        var (mode, error) = MutationDetailModeParser.Parse(null);

        mode.Should().Be(MutationDetailMode.Compact);
        error.Should().BeNull();
    }

    [Fact]
    public void Parse_WhenCompactSpecified_ShouldReturnCompact()
    {
        var arguments = ToJsonElement(new { detail = "compact" });

        var (mode, error) = MutationDetailModeParser.Parse(arguments);

        mode.Should().Be(MutationDetailMode.Compact);
        error.Should().BeNull();
    }

    [Fact]
    public void Parse_WhenVerboseSpecified_ShouldReturnFullDetailMode()
    {
        var arguments = ToJsonElement(new { detail = "verbose" });

        var (mode, error) = MutationDetailModeParser.Parse(arguments);

        mode.Should().Be(MutationDetailMode.Standard);
        error.Should().BeNull();
    }

    [Fact]
    public void Parse_WhenStandardSpecified_ShouldRemainVerboseAlias()
    {
        var arguments = ToJsonElement(new { detail = "standard" });

        var (mode, error) = MutationDetailModeParser.Parse(arguments);

        mode.Should().Be(MutationDetailMode.Standard);
        error.Should().BeNull();
    }

    [Fact]
    public void Parse_WhenInvalidValueProvided_ShouldReturnStructuredError()
    {
        var arguments = ToJsonElement(new { detail = "full" });

        var (_, error) = MutationDetailModeParser.Parse(arguments);

        error.Should().NotBeNull();
        var json = JsonSerializer.SerializeToElement(error);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("detail");
    }
}
