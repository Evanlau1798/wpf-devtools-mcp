using System.Linq;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed partial class InteractionE2eTests
{
    [Fact]
    public async Task SetDpValue_AfterWatchRegistration_ShouldPiggybackPendingDpEvents()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateBasicControlsTabAsync();

        var buttonElementId = await E2eTestHelpers.FindElementByNameAsync(
            _fixture.Client, _fixture.TestAppProcessId, "SaveButton");
        buttonElementId.Should().NotBeNull("TestApp should expose SaveButton through the root namescope");

        var watch = await _fixture.Client.CallToolAsync(
            "watch_dp_changes",
            new
            {
                processId = _fixture.TestAppProcessId,
                elementId = buttonElementId,
                propertyName = "Width"
            });
        watch.GetProperty("success").GetBoolean().Should().BeTrue();

        try
        {
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
        }
        finally
        {
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
    }

    [Fact]
    public async Task ClearDpValue_AfterSetValueOnBindingBackedProperty_ShouldRestoreCapturedBinding()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateBasicControlsTabAsync();

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
            restoreResult.GetProperty("success").GetBoolean().Should().BeTrue(
                "restore_state_snapshot should restore captured Text binding and view-model state: {0}",
                restoreResult.GetRawText());

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
        await ActivateBasicControlsTabAsync();

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
        snapshotId.Should().NotBeNull();

        await E2eTestHelpers.RunWithRestoredSnapshotAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            snapshotId!,
            async () =>
            {
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

                var restoreResult = await E2eTestHelpers.RestoreStateSnapshotAsync(
                    _fixture.Client,
                    _fixture.TestAppProcessId,
                    snapshotId!,
                    removeAfterRestore: false);
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
            });
    }

    [Fact]
    public async Task ClearDpValue_ShouldRestoreVisibilityBindingBackedDependencyProperty()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);
        await ActivateBasicControlsTabAsync();

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
        snapshotId.Should().NotBeNull();

        await E2eTestHelpers.RunWithRestoredSnapshotAsync(
            _fixture.Client,
            _fixture.TestAppProcessId,
            snapshotId!,
            async () =>
            {
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
                setResult.GetProperty("capturedRollbackExpression").GetBoolean().Should().BeTrue();

                var clearResult = await _fixture.Client.CallToolAsync(
                    "clear_dp_value",
                    new
                    {
                        processId = _fixture.TestAppProcessId,
                        elementId = panelElementId,
                        propertyName = "Visibility"
                    });
                clearResult.GetProperty("success").GetBoolean().Should().BeTrue();
                clearResult.GetProperty("restoredExpression").GetBoolean().Should().BeTrue();
                clearResult.GetProperty("expressionKind").GetString().Should().Be("Binding");

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
            });
    }
}
