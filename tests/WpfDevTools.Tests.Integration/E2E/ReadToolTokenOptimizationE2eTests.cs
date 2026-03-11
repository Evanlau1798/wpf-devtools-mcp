using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public class ReadToolTokenOptimizationE2eTests
{
    private readonly McpE2eFixture _fixture;

    public ReadToolTokenOptimizationE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
        E2eTestHelpers.AssertFixtureReady(_fixture);
    }

    [Fact]
    public async Task GetViewModel_WithPropertyNames_ShouldReturnRequestedSubsetOnly()
    {
        var result = await _fixture.Client.CallToolAsync(
            "get_viewmodel",
            new
            {
                propertyNames = new[] { "Name", "CanSave" }
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var properties = result.GetProperty("properties").EnumerateArray().ToList();
        properties.Should().HaveCount(2);
        properties.Select(property => property.GetProperty("name").GetString())
            .Should().Equal("Name", "CanSave");
    }
}
