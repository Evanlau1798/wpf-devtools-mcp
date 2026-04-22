using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class WaitForDpChangeE2eTests : SharedStateMcpE2eTestBase
{
    private readonly McpE2eFixture _fixture;

    public WaitForDpChangeE2eTests(McpE2eFixture fixture)
        : base(fixture)
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

    [Fact]
    public async Task WaitForDpChange_ShouldObserveLiveChangeForSharedSearchBindingTargets()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "SearchProbeTextBox");
        textBoxId.Should().NotBeNull("TestApp should expose SearchProbeTextBox");

        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "SearchText",
                value = string.Empty,
                navigation = false
            });

        var expectedValue = $"search-e2e-{Guid.NewGuid():N}";
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
                propertyName = "SearchText",
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

    [Fact]
    public async Task WaitForDpChangeAfterMutation_ShouldObserveSearchTextChangeWithoutParallelClientRequests()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "SearchProbeTextBox");
        textBoxId.Should().NotBeNull("TestApp should expose SearchProbeTextBox");

        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "SearchText",
                value = string.Empty,
                navigation = false
            });

        var expectedValue = $"search-trigger-{Guid.NewGuid():N}";
        var waitResult = await _fixture.Client.CallToolAsync(
            "wait_for_dp_change_after_mutation",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                propertyName = "Text",
                expectedValue,
                timeoutMs = 4000,
                pollIntervalMs = 100,
                triggerMutation = new
                {
                    tool = "modify_viewmodel",
                    args = new
                    {
                        propertyName = "SearchText",
                        value = expectedValue
                    }
                },
                navigation = false
            },
            timeoutMs: 10000);

        waitResult.GetProperty("success").GetBoolean().Should().BeTrue();
        waitResult.GetProperty("changed").GetBoolean().Should().BeTrue();
        waitResult.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        waitResult.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        waitResult.GetProperty("currentValue").GetString().Should().Be(expectedValue);
    }

    [Fact]
    public async Task WaitForDpChangeAfterMutation_WithStringifiedTriggerMutation_ShouldAcceptCompatibilityPayload()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "SearchProbeTextBox");
        textBoxId.Should().NotBeNull("TestApp should expose SearchProbeTextBox");

        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "SearchText",
                value = string.Empty,
                navigation = false
            });

        var expectedValue = $"search-trigger-string-{Guid.NewGuid():N}";
        var waitResult = await _fixture.Client.CallToolAsync(
            "wait_for_dp_change_after_mutation",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                propertyName = "Text",
                expectedValue,
                timeoutMs = 4000,
                pollIntervalMs = 100,
                triggerMutation = System.Text.Json.JsonSerializer.Serialize(new
                {
                    tool = "modify_viewmodel",
                    args = new
                    {
                        propertyName = "SearchText",
                        value = expectedValue
                    }
                }),
                navigation = false
            },
            timeoutMs: 10000);

        waitResult.GetProperty("success").GetBoolean().Should().BeTrue();
        waitResult.GetProperty("changed").GetBoolean().Should().BeTrue();
        waitResult.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        waitResult.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        waitResult.GetProperty("currentValue").GetString().Should().Be(expectedValue);
    }

    [Fact]
    public async Task WaitForDpChange_WithTriggerMutation_ShouldReturnStructuredMigrationError()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "SearchProbeTextBox");
        textBoxId.Should().NotBeNull("TestApp should expose SearchProbeTextBox");

        var response = await _fixture.Client.CallToolAsync(
            "wait_for_dp_change",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                propertyName = "Text",
                expectedValue = $"deprecated-trigger-{Guid.NewGuid():N}",
                timeoutMs = 500,
                pollIntervalMs = 100,
                triggerMutation = new
                {
                    tool = "modify_viewmodel",
                    args = new
                    {
                        propertyName = "SearchText",
                        value = "deprecated"
                    }
                },
                navigation = false
            },
            timeoutMs: 5000);

        response.GetProperty("success").GetBoolean().Should().BeFalse();
        response.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        response.GetProperty("error").GetString().Should().Contain("wait_for_dp_change_after_mutation");
        response.GetProperty("hint").GetString().Should().Contain("wait_for_dp_change_after_mutation");
        response.GetProperty("suggestedAction").GetString().Should().Contain("wait_for_dp_change_after_mutation");
    }

    [Fact]
    public async Task WaitForDpChange_WithStringifiedTriggerMutation_ShouldReturnStructuredMigrationError()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "SearchProbeTextBox");
        textBoxId.Should().NotBeNull("TestApp should expose SearchProbeTextBox");

        var response = await _fixture.Client.CallToolAsync(
            "wait_for_dp_change",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                propertyName = "Text",
                expectedValue = $"deprecated-trigger-string-{Guid.NewGuid():N}",
                timeoutMs = 500,
                pollIntervalMs = 100,
                triggerMutation = System.Text.Json.JsonSerializer.Serialize(new
                {
                    tool = "modify_viewmodel",
                    args = new
                    {
                        propertyName = "SearchText",
                        value = "deprecated"
                    }
                }),
                navigation = false
            },
            timeoutMs: 5000);

        response.GetProperty("success").GetBoolean().Should().BeFalse();
        response.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        response.GetProperty("error").GetString().Should().Contain("wait_for_dp_change_after_mutation");
        response.GetProperty("hint").GetString().Should().Contain("wait_for_dp_change_after_mutation");
        response.GetProperty("suggestedAction").GetString().Should().Contain("wait_for_dp_change_after_mutation");
    }

    [Fact]
    public async Task WaitForDpChange_LegacyTriggerMutationEnvelope_ShouldPreserveStructuredErrorAnnotations()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "SearchProbeTextBox");
        textBoxId.Should().NotBeNull("TestApp should expose SearchProbeTextBox");

        var response = await _fixture.Client.CallToolEnvelopeAsync(
            "wait_for_dp_change",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                propertyName = "Text",
                expectedValue = $"deprecated-envelope-{Guid.NewGuid():N}",
                timeoutMs = 500,
                pollIntervalMs = 100,
                triggerMutation = new
                {
                    tool = "modify_viewmodel",
                    args = new
                    {
                        propertyName = "SearchText",
                        value = "deprecated"
                    }
                },
                navigation = false
            },
            timeoutMs: 5000);

        var result = response.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.TryGetProperty("structuredContent", out var structuredContent).Should().BeTrue();
        structuredContent.GetProperty("success").GetBoolean().Should().BeFalse();
        structuredContent.GetProperty("suggestedAction").GetString().Should().Contain("wait_for_dp_change_after_mutation");

        var content = result.GetProperty("content");
        var textBlock = content.EnumerateArray().Single();
        textBlock.TryGetProperty("annotations", out var annotations).Should().BeTrue();
        annotations.GetProperty("priority").GetDouble().Should().Be(1.0d);
    }

    [Fact]
    public async Task WaitForDpChangeAfterMutation_WhenTriggerBudgetExpires_ShouldReturnReconnectContractThroughMcpServer()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            "SearchProbeTextBox");
        textBoxId.Should().NotBeNull("TestApp should expose SearchProbeTextBox");

        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "SearchText",
                value = string.Empty,
                navigation = false
            });

        var result = await _fixture.Client.CallToolAsync(
            "wait_for_dp_change_after_mutation",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                propertyName = "Text",
                expectedValue = $"timeout-trigger-{Guid.NewGuid():N}",
                timeoutMs = 1,
                pollIntervalMs = 50,
                triggerMutation = new
                {
                    tool = "modify_viewmodel",
                    args = new
                    {
                        propertyName = "SearchText",
                        value = "timeout-trigger"
                    }
                },
                navigation = false
            },
            timeoutMs: 10000);

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("timedOut").GetBoolean().Should().BeTrue();
        result.GetProperty("completionReason").GetString().Should().Be("TriggerMutationTimedOut");
        result.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        result.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();

        var reconnectResult = await _fixture.Client.CallToolAsync(
            "connect",
            new { processId = _fixture.TestAppProcessId },
            timeoutMs: 90000);

        reconnectResult.GetProperty("success").GetBoolean().Should().BeTrue();

        await _fixture.Client.CallToolAsync(
            "modify_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                propertyName = "SearchText",
                value = string.Empty,
                navigation = false
            });
    }
}
