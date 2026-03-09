using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Host.Handlers;

/// <summary>
/// Contract regression tests ensuring response shapes for event-related
/// operations remain stable across refactors.
/// </summary>
public sealed class EventHandlersContractTests
{
    [StaFact]
    public void FireRoutedEvent_ClickOnButtonWithCommand_ShouldReturnUsedOnClickFlag()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var commandExecuted = false;
        var button = new System.Windows.Controls.Button
        {
            Command = new TestRelayCommand(() => commandExecuted = true)
        };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.FireRoutedEvent(elementId, "Click", null);
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        // Assert
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("usedOnClick").GetBoolean().Should().BeTrue(
            "fire_routed_event('Click') on ButtonBase must include usedOnClick: true");
        commandExecuted.Should().BeTrue(
            "OnClick() path should execute the attached ICommand");
    }

    [StaFact]
    public void FireRoutedEvent_NonClickEvent_ShouldNotReturnUsedOnClickFlag()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new System.Windows.Controls.Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.FireRoutedEvent(elementId, "LostFocus", null);
        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        // Assert
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.TryGetProperty("usedOnClick", out _).Should().BeFalse(
            "Non-Click events must not include usedOnClick in the response");
    }

    private sealed class TestRelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public TestRelayCommand(Action execute) => _execute = execute;
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
