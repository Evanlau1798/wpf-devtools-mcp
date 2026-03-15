using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// E2E tests for MCP interaction tools (click_element, simulate_keyboard, element_screenshot).
/// Validates user interaction simulation through the full MCP protocol pipeline.
/// </summary>
[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class InteractionE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public InteractionE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ElementScreenshot_OnRoot_ShouldReturnBase64Image()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "element_screenshot",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"Screenshot result keys: {string.Join(", ", E2eTestHelpers.EnumeratePropertyNames(result))}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("base64Image", out var image).Should().BeTrue(
            "screenshot should include base64-encoded PNG data");
        image.GetString().Should().NotBeNullOrEmpty();

        if (result.TryGetProperty("width", out var width) &&
            result.TryGetProperty("height", out var height))
        {
            width.GetInt32().Should().BeGreaterThan(0);
            height.GetInt32().Should().BeGreaterThan(0);
            _output.WriteLine($"Screenshot size: {width.GetInt32()}x{height.GetInt32()}");
        }
    }

    [Fact]
    public async Task ClickElement_OnButton_ShouldSucceed()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var buttonElementId = await E2eTestHelpers.FindElementByTypeAsync(
            _fixture.Client, _fixture.TestAppProcessId, "Button");

        buttonElementId.Should().NotBeNull(
            "TestApp should contain at least one Button element in its visual tree");

        var result = await _fixture.Client.CallToolAsync(
            "click_element",
            new { processId = _fixture.TestAppProcessId, elementId = buttonElementId });

        _output.WriteLine($"click_element result: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue(
            "clicking a button should succeed");
    }

    [Fact]
    public async Task SetDpValue_AfterWatchRegistration_ShouldPiggybackPendingDpEvents()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var buttonElementId = await E2eTestHelpers.FindElementByTypeAsync(
            _fixture.Client, _fixture.TestAppProcessId, "Button");

        var watch = await _fixture.Client.CallToolAsync(
            "watch_dp_changes",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonElementId,
                propertyName = "Width"
            });
        watch.GetProperty("success").GetBoolean().Should().BeTrue();

        var mutation = await _fixture.Client.CallToolAsync(
            "set_dp_value",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonElementId,
                propertyName = "Width",
                value = 222
            });

        mutation.GetProperty("success").GetBoolean().Should().BeTrue();
        mutation.GetProperty("pendingEventCount").GetInt32().Should().BeGreaterThan(0);
        var pendingDpEvent = mutation.GetProperty("pendingEvents").EnumerateArray().Single(item =>
            item.GetProperty("eventType").GetString() == "DpChange"
            && item.GetProperty("elementId").GetString() == buttonElementId
            && item.GetProperty("propertyName").GetString() == "Width");
        pendingDpEvent.TryGetProperty("sourceKey", out _).Should().BeFalse();

        var cleanup = await _fixture.Client.CallToolAsync(
            "clear_dp_value",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonElementId,
                propertyName = "Width"
            });
        cleanup.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ClearDpValue_AfterSetValueOnBindingBackedProperty_ShouldRestoreCapturedBinding()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxElementId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client, _fixture.TestAppProcessId, "NameTextBox");
        textBoxElementId.Should().NotBeNull("TestApp should expose NameTextBox through the root namescope");

        var viewModelBefore = await _fixture.Client.CallToolAsync(
            "get_viewmodel",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxElementId
            });
        viewModelBefore.GetProperty("success").GetBoolean().Should().BeTrue();
        var baselineName = viewModelBefore.GetProperty("properties").EnumerateArray()
            .Single(property => property.GetProperty("name").GetString() == "Name")
            .GetProperty("value").GetString();

        var beforeValueSource = await _fixture.Client.CallToolAsync(
            "get_dp_value_source",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxElementId,
                propertyName = "Text"
            });
        beforeValueSource.GetProperty("success").GetBoolean().Should().BeTrue();
        var baselineValue = beforeValueSource.GetProperty("currentValue").GetString();
        var overrideValue = $"Codex Override {Guid.NewGuid():N}";
        var snapshot = await _fixture.Client.CallToolAsync(
            "capture_state_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxElementId,
                propertyNames = new[] { "Text" },
                viewModelPropertyNames = new[] { "Name" },
                includeFocus = true
            });
        snapshot.GetProperty("success").GetBoolean().Should().BeTrue();
        snapshot.GetProperty("snapshotSummary").GetProperty("capturedFocus").GetBoolean().Should().BeTrue();
        var snapshotId = snapshot.GetProperty("snapshotId").GetString();

        try
        {
            var setResult = await _fixture.Client.CallToolAsync(
                "set_dp_value",
                new
                {
                    processId = _fixture.TestAppProcessId,
                    elementId = textBoxElementId,
                    propertyName = "Text",
                    value = overrideValue
                });
            setResult.GetProperty("success").GetBoolean().Should().BeTrue();
            setResult.GetProperty("replacedExpression").GetBoolean().Should().BeTrue();
            setResult.GetProperty("capturedRollbackExpression").GetBoolean().Should().BeTrue();

            var clearResult = await _fixture.Client.CallToolAsync(
                "clear_dp_value",
                new
                {
                    processId = _fixture.TestAppProcessId,
                    elementId = textBoxElementId,
                    propertyName = "Text"
                });

            clearResult.GetProperty("success").GetBoolean().Should().BeTrue();
            clearResult.GetProperty("restoredExpression").GetBoolean().Should().BeTrue();
            clearResult.GetProperty("expressionKind").GetString().Should().Be("Binding");

            var valueSource = await _fixture.Client.CallToolAsync(
                "get_dp_value_source",
                new
                {
                    processId = _fixture.TestAppProcessId,
                    elementId = textBoxElementId,
                    propertyName = "Text"
                });

            valueSource.GetProperty("success").GetBoolean().Should().BeTrue();
            valueSource.GetProperty("isExpression").GetBoolean().Should().BeTrue();

            var reboundName = $"Rebound {Guid.NewGuid():N}";
            var reboundMutation = await _fixture.Client.CallToolAsync(
                "modify_viewmodel",
                new
                {
                    processId = _fixture.TestAppProcessId,
                    elementId = textBoxElementId,
                    propertyName = "Name",
                    value = reboundName
                });
            reboundMutation.GetProperty("success").GetBoolean().Should().BeTrue();

            var reboundValueSource = await _fixture.Client.CallToolAsync(
                "get_dp_value_source",
                new
                {
                    processId = _fixture.TestAppProcessId,
                    elementId = textBoxElementId,
                    propertyName = "Text"
                });
            reboundValueSource.GetProperty("success").GetBoolean().Should().BeTrue();
            reboundValueSource.GetProperty("isExpression").GetBoolean().Should().BeTrue();
            reboundValueSource.GetProperty("currentValue").GetString().Should().Be(reboundName);
        }
        finally
        {
            var restoreResult = await _fixture.Client.CallToolAsync(
                "restore_state_snapshot",
                new
                {
                    processId = _fixture.TestAppProcessId,
                    snapshotId
                });
            restoreResult.GetProperty("success").GetBoolean().Should().BeTrue();

            var valueSource = await _fixture.Client.CallToolAsync(
                "get_dp_value_source",
                new
                {
                    processId = _fixture.TestAppProcessId,
                    elementId = textBoxElementId,
                    propertyName = "Text"
                });

            valueSource.GetProperty("success").GetBoolean().Should().BeTrue();
            valueSource.GetProperty("isExpression").GetBoolean().Should().BeTrue();
            valueSource.GetProperty("currentValue").GetString().Should().Be(baselineValue);

            var viewModelAfterRestore = await _fixture.Client.CallToolAsync(
                "get_viewmodel",
                new
                {
                    processId = _fixture.TestAppProcessId,
                    elementId = textBoxElementId
                });
            viewModelAfterRestore.GetProperty("success").GetBoolean().Should().BeTrue();
            viewModelAfterRestore.GetProperty("properties").EnumerateArray()
                .Single(property => property.GetProperty("name").GetString() == "Name")
                .GetProperty("value").GetString()
                .Should().Be(baselineName);
        }
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldRestoreVisibilityBindingBackedDependencyProperty()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var panelElementId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client, _fixture.TestAppProcessId, "GhostPanel");
        panelElementId.Should().NotBeNull("TestApp should expose GhostPanel through the root namescope");

        var baseline = await _fixture.Client.CallToolAsync(
            "get_dp_value_source",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = panelElementId,
                propertyName = "Visibility"
            });
        baseline.GetProperty("success").GetBoolean().Should().BeTrue();
        baseline.GetProperty("isExpression").GetBoolean().Should().BeTrue();
        baseline.GetProperty("currentValue").GetString().Should().Be("Collapsed");

        var snapshot = await _fixture.Client.CallToolAsync(
            "capture_state_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = panelElementId,
                propertyNames = new[] { "Visibility" }
            });
        snapshot.GetProperty("success").GetBoolean().Should().BeTrue();
        var snapshotId = snapshot.GetProperty("snapshotId").GetString();

        var setResult = await _fixture.Client.CallToolAsync(
            "set_dp_value",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = panelElementId,
                propertyName = "Visibility",
                value = "Visible"
            });
        setResult.GetProperty("success").GetBoolean().Should().BeTrue();
        setResult.GetProperty("replacedExpression").GetBoolean().Should().BeTrue();

        var restoreResult = await _fixture.Client.CallToolAsync(
            "restore_state_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                snapshotId
            });
        restoreResult.GetProperty("success").GetBoolean().Should().BeTrue();
        restoreResult.GetProperty("restoredDependencyPropertyCount").GetInt32().Should().Be(1);
        restoreResult.GetProperty("skippedDependencyPropertyCount").GetInt32().Should().Be(0);

        var restored = await _fixture.Client.CallToolAsync(
            "get_dp_value_source",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = panelElementId,
                propertyName = "Visibility"
            });
        restored.GetProperty("success").GetBoolean().Should().BeTrue();
        restored.GetProperty("isExpression").GetBoolean().Should().BeTrue();
        restored.GetProperty("currentValue").GetString().Should().Be("Collapsed");
    }

    [Fact]
    public async Task ClickElement_AfterSnapshotCapture_ShouldExposeMutationSessionContextRef()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var buttonElementId = await E2eTestHelpers.FindElementByTypeAsync(
            _fixture.Client, _fixture.TestAppProcessId, "Button");

        var capture = await _fixture.Client.CallToolAsync(
            "capture_state_snapshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonElementId,
                propertyNames = new[] { "IsEnabled" }
            });
        capture.GetProperty("success").GetBoolean().Should().BeTrue();

        var click = await _fixture.Client.CallToolAsync(
            "click_element",
            new { processId = _fixture.TestAppProcessId, elementId = buttonElementId });

        click.GetProperty("success").GetBoolean().Should().BeTrue();
        var navigation = click.GetProperty("navigation");
        navigation.GetProperty("contextRefs")[0].GetProperty("type").GetString().Should().Be("mutation-session");
        navigation.GetProperty("prefetchTools").EnumerateArray().Select(item => item.GetString()).Should().Contain("restore_state_snapshot");
    }

    [Fact]
    public async Task SimulateKeyboard_OnTextBox_ShouldSucceed()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var textBoxId = await E2eTestHelpers.FindElementByTypeAsync(
            _fixture.Client, _fixture.TestAppProcessId, "TextBox");

        textBoxId.Should().NotBeNull(
            "TestApp should contain at least one TextBox element in its visual tree");

        var result = await _fixture.Client.CallToolAsync(
            "simulate_keyboard",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = textBoxId,
                key = "A",
                eventType = "KeyDown"
            });

        _output.WriteLine($"simulate_keyboard result: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue(
            "keyboard simulation should succeed on a TextBox");
    }

    [Fact]
    public async Task GetLayoutInfo_ShouldReturnElementDimensions()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_layout_info",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"Layout info: {E2eTestHelpers.Truncate(result.GetRawText(), 500)}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
