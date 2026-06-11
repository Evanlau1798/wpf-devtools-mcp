using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class BindingMismatchToolContractTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public void GetBindingMismatches_ShouldExposeOptionalScopeParameters()
    {
        AssertOptionalParameter(nameof(BindingMcpTools.GetBindingMismatches), "processId", typeof(int?), null);
        AssertOptionalParameter(nameof(BindingMcpTools.GetBindingMismatches), "elementId", typeof(string), null);
        AssertOptionalParameter(nameof(BindingMcpTools.GetBindingMismatches), "recursive", typeof(bool), false);
        AssertOptionalParameter(nameof(BindingMcpTools.GetBindingMismatches), "includeFramework", typeof(bool), false);
    }

    private static void AssertOptionalParameter(
        string methodName,
        string parameterName,
        Type parameterType,
        object? defaultValue)
    {
        var method = typeof(BindingMcpTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var parameter = method!.GetParameters().SingleOrDefault(item => item.Name == parameterName);
        parameter.Should().NotBeNull();
        parameter!.ParameterType.Should().Be(parameterType);
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().Be(defaultValue);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithPathMismatch_ShouldSuggestBindings()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                mismatchCount = 1,
                mismatches = new[]
                {
                    new
                    {
                        elementId = "TextBox_3",
                        propertyName = "Text",
                        bindingPath = "MissingName",
                        diagnosis = "PathMismatch"
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "TextBox_3")),
            CancellationToken.None,
            toolName: "get_binding_mismatches");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("TextBox_3");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithTypeMismatch_ShouldSuggestDpValueSource()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                mismatchCount = 1,
                mismatches = new[]
                {
                    new
                    {
                        elementId = "TextBox_4",
                        propertyName = "Text",
                        bindingPath = "Age",
                        diagnosis = "TypeMismatchWithConverter"
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "TextBox_4")),
            CancellationToken.None,
            toolName: "get_binding_mismatches");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_dp_value_source");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("TextBox_4");
        nextSteps[0].GetProperty("params").GetProperty("propertyName").GetString().Should().Be("Text");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithoutElementId_ShouldFallbackToEmptyNextSteps()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                mismatchCount = 1,
                mismatches = new[]
                {
                    new
                    {
                        elementId = (string?)null,
                        propertyName = "Text",
                        diagnosis = "PathMismatch"
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "get_binding_mismatches");

        result.StructuredContent!.Value.GetProperty("nextSteps").GetArrayLength().Should().Be(0);
    }
}
