using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class VisibilityDiagnosisE2eTests
{
    private readonly McpE2eFixture _fixture;

    public VisibilityDiagnosisE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DiagnoseVisibility_ShouldReportAncestorVisibilityBlocker()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await SelectLayoutTransformsTabAsync();
        var runtimeElementId = await FindElementIdAsync("HiddenByAncestorText");

        var result = await _fixture.Client.CallToolAsync(
            "diagnose_visibility",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = runtimeElementId
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isUserVisible").GetBoolean().Should().BeFalse();
        result.GetProperty("rootCause").GetString().Should().Contain("HiddenByAncestorPanel");
    }

    [Fact]
    public async Task DiagnoseVisibility_ShouldReportClippingBlocker()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await SelectLayoutTransformsTabAsync();
        var runtimeElementId = await FindElementIdAsync("ClippingTextSample");

        var result = await _fixture.Client.CallToolAsync(
            "diagnose_visibility",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = runtimeElementId
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isUserVisible").GetBoolean().Should().BeFalse();
        result.GetProperty("rootCause").GetString().Should().MatchRegex("(?i)clip");
    }

    private async Task SelectLayoutTransformsTabAsync()
    {
        var layoutTabId = await FindElementIdAsync("LayoutTransformsTab");
        await _fixture.Client.CallToolAsync(
            "click_element",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = layoutTabId
            });
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
