using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class ElementSearchEnhancedQueryE2eTests
{
    private readonly McpE2eFixture _fixture;

    public ElementSearchEnhancedQueryE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
        E2eTestHelpers.AssertFixtureReady(_fixture);
    }

    [Fact]
    public async Task FindElements_ShouldSupportContainsMatchModeForTypeName()
    {
        var result = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                typeName = "Text",
                matchMode = "contains",
                maxResults = 20
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("resultCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FindElements_ShouldSupportContainsMatchModeForElementName()
    {
        var result = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                elementName = "Save",
                matchMode = "contains",
                maxResults = 20
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("resultCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FindElements_ShouldSupportMultipleTypeNames()
    {
        var result = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                typeNames = new[] { "TextBox", "ComboBox" },
                maxResults = 50
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("resultCount").GetInt32().Should().BeGreaterThan(0);
    }
}
