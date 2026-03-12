using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class InteractionReadinessE2eTests
{
    private readonly McpE2eFixture _fixture;

    public InteractionReadinessE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetInteractionReadiness_ShouldReportDisabledSaveButtonAsNotReady()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "Name",
                value = ""
            });
        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "Age",
                value = 0
            });

        var saveButtonId = await FindElementIdAsync("SaveButton");
        var result = await _fixture.Client.CallToolAsync(
            "get_interaction_readiness",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = saveButtonId
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isReady").GetBoolean().Should().BeFalse();
        result.GetProperty("blockers").EnumerateArray()
            .Select(item => item.GetProperty("reason").GetString())
            .Should().Contain("CommandCannotExecute");
    }

    [Fact]
    public async Task GetInteractionReadiness_ShouldReportSaveButtonReadyAfterValidInput()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "Name",
                value = "Ready User"
            });
        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "Age",
                value = 32
            });

        var saveButtonId = await FindElementIdAsync("SaveButton");
        var result = await _fixture.Client.CallToolAsync(
            "get_interaction_readiness",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = saveButtonId
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isReady").GetBoolean().Should().BeTrue();
        result.GetProperty("blockers").GetArrayLength().Should().Be(0);
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
