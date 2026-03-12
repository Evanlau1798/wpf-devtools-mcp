using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class ElementSnapshotE2eTests
{
    private readonly McpE2eFixture _fixture;

    public ElementSnapshotE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetElementSnapshot_ShouldAggregateCommonDiagnosticsForNamedControl()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var findResult = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementName = "NameTextBox"
            });
        var runtimeElementId = findResult.GetProperty("results")[0].GetProperty("elementId").GetString();

        var result = await _fixture.Client.CallToolAsync(
            "get_element_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = runtimeElementId
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("elementId").GetString().Should().Be(runtimeElementId);
        result.GetProperty("elementType").GetString().Should().Be("TextBox");
        result.TryGetProperty("bindings", out _).Should().BeTrue();
        result.TryGetProperty("validationErrors", out _).Should().BeTrue();
        result.TryGetProperty("layout", out _).Should().BeTrue();
        result.TryGetProperty("properties", out _).Should().BeTrue();
    }
}
