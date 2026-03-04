using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector.Host;

namespace WpfDevTools.Tests.Unit.Inspector;

public class ParameterHelpersTests
{
    [Fact]
    public void GetStringParam_WithNullParams_ShouldReturnNull()
    {
        var result = ParameterHelpers.GetStringParam(null, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void GetStringParam_WithMissingKey_ShouldReturnNull()
    {
        var json = JsonSerializer.SerializeToElement(new { other = "value" });
        var result = ParameterHelpers.GetStringParam(json, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void GetStringParam_WithValidKey_ShouldReturnValue()
    {
        var json = JsonSerializer.SerializeToElement(new { name = "test" });
        var result = ParameterHelpers.GetStringParam(json, "name");
        result.Should().Be("test");
    }

    [Fact]
    public void GetStringParam_WithEmptyString_ShouldReturnEmptyString()
    {
        var json = JsonSerializer.SerializeToElement(new { name = "" });
        var result = ParameterHelpers.GetStringParam(json, "name");
        result.Should().Be("");
    }

    [Fact]
    public void GetIntParam_WithNullParams_ShouldReturnNull()
    {
        var result = ParameterHelpers.GetIntParam(null, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void GetIntParam_WithMissingKey_ShouldReturnNull()
    {
        var json = JsonSerializer.SerializeToElement(new { other = 5 });
        var result = ParameterHelpers.GetIntParam(json, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void GetIntParam_WithValidKey_ShouldReturnValue()
    {
        var json = JsonSerializer.SerializeToElement(new { count = 42 });
        var result = ParameterHelpers.GetIntParam(json, "count");
        result.Should().Be(42);
    }

    [Fact]
    public void GetIntParam_WithZero_ShouldReturnZero()
    {
        var json = JsonSerializer.SerializeToElement(new { count = 0 });
        var result = ParameterHelpers.GetIntParam(json, "count");
        result.Should().Be(0);
    }

    [Fact]
    public void GetIntParam_WithNegativeValue_ShouldReturnNegative()
    {
        var json = JsonSerializer.SerializeToElement(new { count = -10 });
        var result = ParameterHelpers.GetIntParam(json, "count");
        result.Should().Be(-10);
    }

    [Fact]
    public void GetBoolParam_WithNullParams_ShouldReturnNull()
    {
        var result = ParameterHelpers.GetBoolParam(null, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void GetBoolParam_WithMissingKey_ShouldReturnNull()
    {
        var json = JsonSerializer.SerializeToElement(new { other = true });
        var result = ParameterHelpers.GetBoolParam(json, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void GetBoolParam_WithValidKey_ShouldReturnValue()
    {
        var json = JsonSerializer.SerializeToElement(new { enabled = true });
        var result = ParameterHelpers.GetBoolParam(json, "enabled");
        result.Should().BeTrue();
    }

    [Fact]
    public void GetBoolParam_WithFalse_ShouldReturnFalse()
    {
        var json = JsonSerializer.SerializeToElement(new { enabled = false });
        var result = ParameterHelpers.GetBoolParam(json, "enabled");
        result.Should().BeFalse();
    }

    [Fact]
    public void GetObjectParam_WithNullParams_ShouldReturnDefault()
    {
        var result = ParameterHelpers.GetObjectParam<TestObject>(null, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void GetObjectParam_WithMissingKey_ShouldReturnDefault()
    {
        var json = JsonSerializer.SerializeToElement(new { other = new { value = 1 } });
        var result = ParameterHelpers.GetObjectParam<TestObject>(json, "key");
        result.Should().BeNull();
    }

    [Fact]
    public void GetObjectParam_WithValidKey_ShouldReturnDeserializedObject()
    {
        var json = JsonSerializer.SerializeToElement(new { data = new { Name = "test", Value = 42 } });
        var result = ParameterHelpers.GetObjectParam<TestObject>(json, "data");
        result.Should().NotBeNull();
        result!.Name.Should().Be("test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void GetObjectParam_WithComplexObject_ShouldDeserializeCorrectly()
    {
        var json = JsonSerializer.SerializeToElement(new
        {
            data = new
            {
                Name = "complex",
                Value = 100,
                Nested = new { Id = 5 }
            }
        });
        var result = ParameterHelpers.GetObjectParam<ComplexTestObject>(json, "data");
        result.Should().NotBeNull();
        result!.Name.Should().Be("complex");
        result.Value.Should().Be(100);
        result.Nested.Should().NotBeNull();
        result.Nested!.Id.Should().Be(5);
    }

    private class TestObject
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    private class ComplexTestObject
    {
        public string? Name { get; set; }
        public int Value { get; set; }
        public NestedObject? Nested { get; set; }
    }

    private class NestedObject
    {
        public int Id { get; set; }
    }
}
