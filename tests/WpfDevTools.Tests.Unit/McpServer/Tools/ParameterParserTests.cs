using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Shared.Utilities;

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
    public void ParseStringParam_WithEmptyString_ShouldReturnEmptyString()
    {
        var args = JsonSerializer.SerializeToElement(new { name = "" });
        var result = ParameterParser.ParseStringParam(args, "name");
        result.Should().Be("");
    }

    [Fact]
    public void ParseStringParam_WithNumberValue_ShouldReturnNull()
    {
        var args = JsonSerializer.SerializeToElement(new { value = 123 });

        // Act - GetString() on a number returns null
        var result = ParameterParser.ParseStringParam(args, "value");

        // Assert
        result.Should().BeNull();
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
    public void ParseIntParam_WithZero_ShouldReturnZero()
    {
        var args = JsonSerializer.SerializeToElement(new { count = 0 });
        var result = ParameterParser.ParseIntParam(args, "count");
        result.Should().Be(0);
    }

    [Fact]
    public void ParseIntParam_WithNegativeValue_ShouldReturnNegative()
    {
        var args = JsonSerializer.SerializeToElement(new { count = -10 });
        var result = ParameterParser.ParseIntParam(args, "count");
        result.Should().Be(-10);
    }

    [Fact]
    public void ParseIntParam_WithMaxValue_ShouldReturnMaxValue()
    {
        var args = JsonSerializer.SerializeToElement(new { count = int.MaxValue });
        var result = ParameterParser.ParseIntParam(args, "count");
        result.Should().Be(int.MaxValue);
    }

    [Fact]
    public void ParseIntParam_WithMinValue_ShouldReturnMinValue()
    {
        var args = JsonSerializer.SerializeToElement(new { count = int.MinValue });
        var result = ParameterParser.ParseIntParam(args, "count");
        result.Should().Be(int.MinValue);
    }

    [Fact]
    public void ParseIntParam_WithStringValue_ShouldReturnNullIfNotParseable()
    {
        var args = JsonSerializer.SerializeToElement(new { count = "not a number" });

        // Act - should return null when string cannot be parsed as int
        var result = ParameterParser.ParseIntParam(args, "count");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseIntParam_WithDoubleValue_ShouldReturnNull()
    {
        var args = JsonSerializer.SerializeToElement(new { count = 42.7 });

        // Act - GetInt32() throws FormatException for doubles, so should return null
        var result = ParameterParser.ParseIntParam(args, "count");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseIntParam_WithOverflowValue_ShouldReturnNull()
    {
        // Arrange - create JSON with value larger than int.MaxValue
        var json = $"{{\"count\": {(long)int.MaxValue + 1}}}";
        var args = JsonSerializer.Deserialize<JsonElement>(json);

        // Act - should return null when value overflows
        var result = ParameterParser.ParseIntParam(args, "count");

        // Assert
        result.Should().BeNull();
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

    [Fact]
    public void ParseBoolParam_WithFalse_ShouldReturnFalse()
    {
        var args = JsonSerializer.SerializeToElement(new { enabled = false });
        var result = ParameterParser.ParseBoolParam(args, "enabled");
        result.Should().BeFalse();
    }

    [Fact]
    public void ParseBoolParam_WithStringValue_ShouldReturnParsedValue()
    {
        var args = JsonSerializer.SerializeToElement(new { enabled = "true" });

        // Act - should parse string "true" as boolean
        var result = ParameterParser.ParseBoolParam(args, "enabled");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ParseBoolParam_WithNumberValue_ShouldReturnNull()
    {
        var args = JsonSerializer.SerializeToElement(new { enabled = 1 });

        // Act - should return null for number values
        var result = ParameterParser.ParseBoolParam(args, "enabled");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseStringParam_WithNullValue_ShouldReturnNull()
    {
        var json = "{\"name\": null}";
        var args = JsonSerializer.Deserialize<JsonElement>(json);
        var result = ParameterParser.ParseStringParam(args, "name");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseIntParam_WithNullValue_ShouldReturnNull()
    {
        var json = "{\"count\": null}";
        var args = JsonSerializer.Deserialize<JsonElement>(json);

        // Act - should return null for null values
        var result = ParameterParser.ParseIntParam(args, "count");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseBoolParam_WithNullValue_ShouldReturnNull()
    {
        var json = "{\"enabled\": null}";
        var args = JsonSerializer.Deserialize<JsonElement>(json);

        // Act - should return null for null values
        var result = ParameterParser.ParseBoolParam(args, "enabled");

        // Assert
        result.Should().BeNull();
    }}
