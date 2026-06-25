using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("ToolCallHelperState")]
public sealed class InteractionFailureNavigationTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task FocusElement_Navigation_WhenElementNotLoaded_ShouldRecommendVisibilityAndReadiness()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = false,
                errorCode = "ElementNotLoaded",
                error = "Element is not visible in the active visual tree."
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "HiddenButton")),
            CancellationToken.None,
            toolName: "focus_element");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        var tools = nextSteps.EnumerateArray()
            .Select(item => item.GetProperty("tool").GetString())
            .ToArray();

        tools.Should().ContainInOrder("diagnose_visibility", "get_interaction_readiness");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("HiddenButton");
        nextSteps[1].GetProperty("params").GetProperty("elementId").GetString().Should().Be("HiddenButton");
    }

    [Fact]
    public async Task ClickElement_Navigation_WhenRootIsNotClickable_ShouldRecommendReadinessAndScopedSearch()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = false,
                errorCode = "ElementNotClickable",
                error = "Window root is not directly clickable."
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "MainWindow_1")),
            CancellationToken.None,
            toolName: "click_element");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        var tools = nextSteps.EnumerateArray()
            .Select(item => item.GetProperty("tool").GetString())
            .ToArray();

        tools.Should().ContainInOrder("diagnose_visibility", "get_interaction_readiness", "find_elements");
        var searchStep = nextSteps.EnumerateArray()
            .Single(item => item.GetProperty("tool").GetString() == "find_elements");
        var searchParams = searchStep.GetProperty("params");
        searchParams.GetProperty("elementId").GetString().Should().Be("MainWindow_1");
        searchParams.GetProperty("typeNames").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("Button", "TextBox", "ComboBox", "CheckBox", "MenuItem");
        searchParams.GetProperty("maxResults").GetInt32().Should().Be(20);
        searchStep.GetProperty("reason").GetString().Should().Contain("visible actionable descendant");
    }
}
