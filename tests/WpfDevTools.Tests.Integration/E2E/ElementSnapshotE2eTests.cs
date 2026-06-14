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

    [Fact]
    public async Task GetElementSnapshot_WithIncludeProperties_ShouldAppendRequestedProperty()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var findResult = await _fixture.Client.CallToolAsync(
            "find_elements",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementName = "EnabledCheckBox"
            });
        var runtimeElementId = findResult.GetProperty("results")[0].GetProperty("elementId").GetString();

        var defaultSnapshot = await _fixture.Client.CallToolAsync(
            "get_element_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = runtimeElementId
            });
        var extendedSnapshot = await _fixture.Client.CallToolAsync(
            "get_element_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = runtimeElementId,
                includeProperties = new[] { "IsChecked" }
            });

        defaultSnapshot.GetProperty("success").GetBoolean().Should().BeTrue();
        extendedSnapshot.GetProperty("success").GetBoolean().Should().BeTrue();
        defaultSnapshot.GetProperty("properties").TryGetProperty("IsChecked", out _).Should().BeFalse();
        extendedSnapshot.GetProperty("properties").GetProperty("IsChecked").GetProperty("currentValue").GetString().Should().NotBeNull();
    }

    [Fact]
    public async Task GetElementSnapshot_WithBooleanIncludeProperties_ShouldReturnDefaultPropertySnapshot()
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
                elementId = runtimeElementId,
                includeProperties = true
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("elementId").GetString().Should().Be(runtimeElementId);
        result.GetProperty("elementType").GetString().Should().Be("TextBox");
        result.GetProperty("properties").TryGetProperty("Text", out _).Should().BeTrue();
        result.GetProperty("properties").TryGetProperty("IsEnabled", out _).Should().BeTrue();
    }
}
