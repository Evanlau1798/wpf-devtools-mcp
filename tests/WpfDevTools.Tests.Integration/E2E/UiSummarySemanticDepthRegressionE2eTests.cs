using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class UiSummarySemanticDepthRegressionE2eTests
{
    private readonly McpE2eFixture _fixture;

    public UiSummarySemanticDepthRegressionE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetUiSummary_DefaultDepthMode_ShouldUseSemanticOnWrapperHeavyModernTab()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var modernTabId = await FindElementIdAsync("ModernThemeTab");
        var defaultResult = await _fixture.Client.CallToolAsync(
            "get_ui_summary",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = modernTabId,
                depth = 3
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

        defaultResult.GetProperty("success").GetBoolean().Should().BeTrue();
        semanticResult.GetProperty("success").GetBoolean().Should().BeTrue();
        defaultResult.GetProperty("depthMode").GetString().Should().Be("semantic");
        defaultResult.GetProperty("summaryText").GetString().Should().Contain("CurrentThemeModeText");
        semanticResult.GetProperty("summaryText").GetString().Should().Contain("CurrentThemeModeText");
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
