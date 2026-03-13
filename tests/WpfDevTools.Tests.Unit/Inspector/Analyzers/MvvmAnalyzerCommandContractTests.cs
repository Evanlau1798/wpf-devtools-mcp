using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Navigation;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("ToolCallHelperState")]
public sealed class MvvmAnalyzerCommandContractTests : IDisposable
{
    public void Dispose()
    {
        ToolCallHelper.ResetCacheForTesting();
    }

    [StaFact]
    public void ExecuteCommand_ShouldReturnCanExecuteMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);
        var viewModel = new CommandViewModel();
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.ExecuteCommand(elementId, nameof(CommandViewModel.SaveCommand), null)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("commandName").GetString().Should().Be(nameof(CommandViewModel.SaveCommand));
        result.GetProperty("executed").GetBoolean().Should().BeTrue();
        result.GetProperty("canExecute").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ModifyViewModel_Navigation_ShouldSuggestBindingsForScopedElement()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                propertyName = "Name",
                oldValue = "Alice",
                newValue = "Bob"
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "NameTextBox"), ("propertyName", "Name"), ("value", "Bob")),
            CancellationToken.None,
            toolName: "modify_viewmodel");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("NameTextBox");
    }

    [Fact]
    public async Task ModifyViewModel_Navigation_WithActiveSnapshot_ShouldPreferStateDiff()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                propertyName = "Name",
                oldValue = "Alice",
                newValue = "Bob"
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "NameTextBox"), ("propertyName", "Name"), ("value", "Bob")),
            CancellationToken.None,
            navigationState: new NavigationSessionState("snapshot_123", null),
            toolName: "modify_viewmodel");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_state_diff");
        nextSteps[0].GetProperty("params").GetProperty("snapshotId").GetString().Should().Be("snapshot_123");
        nextSteps[0].GetProperty("expectedOutcome").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private sealed class CommandViewModel
    {
        public bool Executed { get; private set; }

        public System.Windows.Input.ICommand SaveCommand => new RelayCommand(
            _ => Executed = true,
            _ => true);
    }

    private sealed class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool> _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);
    }
}
