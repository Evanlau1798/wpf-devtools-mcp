using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class UiSummaryE2eTests
{
    private readonly McpE2eFixture _fixture;

    public UiSummaryE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetUiSummary_ShouldReturnSemanticSummaryForBasicControls()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var rootId = await FindElementIdAsync("BasicControlsStackPanel");
        var result = await _fixture.Client.CallToolAsync(
            "get_ui_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = rootId,
                depth = 4
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summaryText").GetString().Should().Contain("NameTextBox");
        result.GetProperty("summaryText").GetString().Should().Contain("SaveButton");
        result.GetProperty("summaryText").GetString().Should().NotContain("BasicControlsStackPanel");
    }

    [Fact]
    public async Task GetUiSummary_WhenScopedToSemanticRoot_ShouldIncludeRootNode()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var saveButtonId = await FindElementIdAsync("SaveButton");
        var result = await _fixture.Client.CallToolAsync(
            "get_ui_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = saveButtonId,
                depth = 0
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("semanticNodeCount").GetInt32().Should().Be(1);
        result.GetProperty("summaryText").GetString().Should().Contain("Button SaveButton");
    }

    [Fact]
    public async Task GetUiSummary_WithSemanticDepthMode_ShouldReachModernTabControlsWithLowerDepth()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var modernTabId = await FindElementIdAsync("ModernThemeTab");
        var visualResult = await _fixture.Client.CallToolAsync(
            "get_ui_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = modernTabId,
                depth = 3,
                depthMode = "visual"
            });
        var semanticResult = await _fixture.Client.CallToolAsync(
            "get_ui_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = modernTabId,
                depth = 3,
                depthMode = "semantic"
            });

        visualResult.GetProperty("success").GetBoolean().Should().BeTrue();
        semanticResult.GetProperty("success").GetBoolean().Should().BeTrue();
        visualResult.GetProperty("summaryText").GetString().Should().NotContain("CurrentThemeModeText");
        semanticResult.GetProperty("summaryText").GetString().Should().Contain("CurrentThemeModeText");
        semanticResult.GetProperty("depthMode").GetString().Should().Be("semantic");
    }

    private async Task<string?> FindElementIdAsync(string elementName)
    {
        var result = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementName
            });

        return result.GetProperty("results")[0].GetProperty("elementId").GetString();
    }
}
