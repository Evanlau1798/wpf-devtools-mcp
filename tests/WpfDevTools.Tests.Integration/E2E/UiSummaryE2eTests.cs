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
