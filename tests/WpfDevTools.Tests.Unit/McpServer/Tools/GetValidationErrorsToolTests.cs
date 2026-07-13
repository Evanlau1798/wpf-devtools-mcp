using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("ToolCallHelperState")]
public sealed class GetValidationErrorsToolTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }




    [Fact]
    public async Task ExecuteAndWrapAsync_WithScopedValidationErrors_ShouldSuggestBindingsAndViewModel()
    {
        var args = ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "NameTextBox"));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                errorCount = 1,
                errors = new[]
                {
                    new
                    {
                        errorContent = "Name is required",
                        isRuleError = false,
                        ruleType = "DataErrorValidationRule",
                        elementType = "TextBox",
                        elementName = "NameTextBox"
                    }
                }
            }),
            args,
            CancellationToken.None,
            toolName: "get_validation_errors");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(2);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("NameTextBox");
        nextSteps[1].GetProperty("tool").GetString().Should().Be("get_viewmodel");
        nextSteps[1].GetProperty("params").GetProperty("elementId").GetString().Should().Be("NameTextBox");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithoutElementId_ShouldRemainConservative()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                errorCount = 1,
                errors = new[]
                {
                    new
                    {
                        errorContent = "Name is required",
                        isRuleError = false,
                        ruleType = "DataErrorValidationRule",
                        elementType = "TextBox",
                        elementName = "NameTextBox"
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "get_validation_errors");

        result.StructuredContent!.Value.GetProperty("nextSteps").GetArrayLength().Should().Be(0);
    }
}
