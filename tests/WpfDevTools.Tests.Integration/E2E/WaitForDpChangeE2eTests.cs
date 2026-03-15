using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class WaitForDpChangeE2eTests
{
    private readonly McpE2eFixture _fixture;

    public WaitForDpChangeE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WaitForDpChange_ShouldObserveModifyViewmodelChangeOnSameSession()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "NameTextBox");
        textBoxId.Should().NotBeNull("TestApp should expose NameTextBox");

        var expectedValue = $"wait-e2e-{Guid.NewGuid():N}";

        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "Name",
                value = string.Empty,
                navigation = false
            });

        var waitTask = _fixture.Client.CallToolAsync(
            "wait_for_dp_change",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                propertyName = "Text",
                expectedValue,
                timeoutMs = 4000,
                pollIntervalMs = 100,
                navigation = false
            },
            timeoutMs: 10000);

        await Task.Delay(250);

        var mutateTask = _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "Name",
                value = expectedValue,
                navigation = false
            },
            timeoutMs: 10000);

        var waitResult = await waitTask;
        var mutateResult = await mutateTask;

        mutateResult.GetProperty("success").GetBoolean().Should().BeTrue();
        waitResult.GetProperty("success").GetBoolean().Should().BeTrue();
        waitResult.GetProperty("changed").GetBoolean().Should().BeTrue();
        waitResult.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        waitResult.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        waitResult.GetProperty("currentValue").GetString().Should().Be(expectedValue);
    }
}
