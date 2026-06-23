using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class ToolNavigationTruncationTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithTruncatedUiSummary_ShouldRecommendScopedNarrowing()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                rootElementId = "RootShell_1",
                truncated = true,
                truncationReasons = new[] { "SemanticNodeLimit" },
                summaryText = "Large shell"
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "get_ui_summary");

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        var firstStep = navigation.GetProperty("recommended")[0];
        firstStep.GetProperty("tool").GetString().Should().Be("get_namescope");
        firstStep.GetProperty("params").GetProperty("elementId").GetString().Should().Be("RootShell_1");
        firstStep.GetProperty("params").GetProperty("processId").GetInt32().Should().Be(12345);
        firstStep.GetProperty("whyNow").GetString().Should().Contain("truncated");

        navigation.GetProperty("alternatives")
            .EnumerateArray()
            .Select(step => step.GetProperty("tool").GetString())
            .Should()
            .Contain(["get_element_snapshot", "get_visual_tree"]);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithTruncatedFormSummary_ShouldRecommendScopedNarrowing()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                formScope = "ProfileForm_2",
                truncated = true,
                truncationReasons = new[] { "InputLimit" },
                inputs = Array.Empty<object>(),
                commands = Array.Empty<object>()
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "get_form_summary");

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        var firstStep = navigation.GetProperty("recommended")[0];
        firstStep.GetProperty("tool").GetString().Should().Be("get_namescope");
        firstStep.GetProperty("params").GetProperty("elementId").GetString().Should().Be("ProfileForm_2");
        firstStep.GetProperty("params").GetProperty("processId").GetInt32().Should().Be(12345);
        firstStep.GetProperty("whyNow").GetString().Should().Contain("truncated");

        navigation.GetProperty("alternatives")
            .EnumerateArray()
            .Select(step => step.GetProperty("tool").GetString())
            .Should()
            .Contain(["get_ui_summary", "get_visual_tree"]);
    }
}
