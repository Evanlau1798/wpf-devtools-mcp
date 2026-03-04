using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ParameterParserTests
{
    [Fact]
    public void ParseStringParam_WithNullArguments_ShouldReturnNull()
    {
        var result = ParameterParser.ParseStringParam(null, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseStringParam_WithMissingKey_ShouldReturnNull()
    {
        var args = JsonSerializer.SerializeToElement(new { other = "value" });
        var result = ParameterParser.ParseStringParam(args, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseStringParam_WithValidKey_ShouldReturnValue()
    {
        var args = JsonSerializer.SerializeToElement(new { name = "test" });
        var result = ParameterParser.ParseStringParam(args, "name");
        result.Should().Be("test");
    }

    [Fact]
    public void ParseIntParam_WithNullArguments_ShouldReturnNull()
    {
        var result = ParameterParser.ParseIntParam(null, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseIntParam_WithMissingKey_ShouldReturnNull()
    {
        var args = JsonSerializer.SerializeToElement(new { other = 5 });
        var result = ParameterParser.ParseIntParam(args, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseIntParam_WithValidKey_ShouldReturnValue()
    {
        var args = JsonSerializer.SerializeToElement(new { count = 42 });
        var result = ParameterParser.ParseIntParam(args, "count");
        result.Should().Be(42);
    }

    [Fact]
    public void ParseBoolParam_WithNullArguments_ShouldReturnNull()
    {
        var result = ParameterParser.ParseBoolParam(null, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseBoolParam_WithValidKey_ShouldReturnValue()
    {
        var args = JsonSerializer.SerializeToElement(new { enabled = true });
        var result = ParameterParser.ParseBoolParam(args, "enabled");
        result.Should().BeTrue();
    }
}
