using FluentAssertions;
using WpfDevTools.Mcp.Server.Diagnostics;
using WpfDevTools.Mcp.Server.State;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class SceneStateDiffCalculatorTests
{
    [Fact]
    public void Calculate_ShouldReportPropertyViewModelAndDiagnosticsChanges()
    {
        var snapshot = new StoredStateSnapshot(
            "snapshot_1",
            "Before save",
            "NameTextBox",
            [new StoredDependencyPropertySnapshot("NameTextBox", "Text", true, "Alice", "Alice", "LocalValue")],
            [new StoredViewModelPropertySnapshot("NameTextBox", "Name", "String", "Alice", true, null)],
            new StoredFocusSnapshot("Logical", "NameTextBox"),
            [new StoredBindingErrorSnapshot("TextBox_1", null, null, "Text", "InvalidPropertyName", "Binding path error")],
            true,
            [new StoredValidationErrorSnapshot("TextBox", "NameTextBox", "Name is required", false, null)],
            true,
            DateTimeOffset.UtcNow.AddSeconds(-2));

        var current = new CurrentSceneState(
            [new CurrentDependencyPropertyState("NameTextBox", "Text", "Bob", "LocalValue")],
            [new CurrentViewModelPropertyState("NameTextBox", "Name", "Bob")],
            new CurrentFocusState("Logical", "SaveButton"),
            [new StoredBindingErrorSnapshot("TextBox_2", null, null, "Text", "Name", "Null DataContext")],
            [new StoredValidationErrorSnapshot("TextBox", "AgeTextBox", "Age must be greater than 0", false, null)]);

        var result = SceneStateDiffCalculator.Calculate(snapshot, current, "click_element(SaveButton)", DateTimeOffset.UtcNow);

        result.Success.Should().BeTrue();
        result.Trigger.Should().Be("click_element(SaveButton)");
        result.PropertyChanges.Should().ContainSingle(change =>
            change.PropertyName == "Text" &&
            change.BeforeValue == "Alice" &&
            change.AfterValue == "Bob");
        result.ViewModelChanges.Should().ContainSingle(change =>
            change.PropertyName == "Name" &&
            change.BeforeValue == "Alice" &&
            change.AfterValue == "Bob");
        result.NewBindingErrors.Should().ContainSingle(error => error.ElementId == "TextBox_2");
        result.ResolvedBindingErrors.Should().ContainSingle(error => error.ElementId == "TextBox_1");
        result.ValidationChanges.Should().ContainSingle(change =>
            change.ChangeType == "Added" &&
            change.ElementName == "AgeTextBox");
        result.ValidationChanges.Should().ContainSingle(change =>
            change.ChangeType == "Removed" &&
            change.ElementName == "NameTextBox");
        result.FocusChange!.BeforeFocusedElementId.Should().Be("NameTextBox");
        result.FocusChange.AfterFocusedElementId.Should().Be("SaveButton");
    }
}
