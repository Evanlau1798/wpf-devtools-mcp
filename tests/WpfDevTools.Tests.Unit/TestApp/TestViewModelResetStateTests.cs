using FluentAssertions;
using WpfDevTools.Tests.TestApp;

namespace WpfDevTools.Tests.Unit.TestApp;

public sealed class TestViewModelResetStateTests
{
    [Fact]
    public void ResetStateCommand_ShouldRestoreMutatedStateToDefaults()
    {
        var viewModel = new TestViewModel
        {
            FirstName = "Grace",
            LastName = "Hopper",
            Name = "Reset Candidate",
            SearchText = "Pending search",
            Age = 42,
            IsEnabled = false,
            IsGhostVisible = true,
            UseBrokenDetailContext = true
        };
        viewModel.RecordActionMessage("Mutated");

        viewModel.ResetStateCommand.Execute(null);

        viewModel.FirstName.Should().Be("Ada");
        viewModel.LastName.Should().Be("Lovelace");
        viewModel.Name.Should().BeEmpty();
        viewModel.SearchText.Should().BeEmpty();
        viewModel.Age.Should().Be(0);
        viewModel.IsEnabled.Should().BeTrue();
        viewModel.IsGhostVisible.Should().BeFalse();
        viewModel.UseBrokenDetailContext.Should().BeFalse();
        viewModel.LastActionMessage.Should().Be("Ready");
    }

    [Fact]
    public void ResetStateCommand_ShouldRestoreBaselineCommandState()
    {
        var viewModel = new TestViewModel
        {
            Name = "Ready User",
            Age = 32
        };

        viewModel.CanSave.Should().BeTrue();
        viewModel.SaveCommand.CanExecute(null).Should().BeTrue();

        viewModel.ResetStateCommand.Execute(null);

        viewModel.CanSave.Should().BeFalse();
        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();
        viewModel.LastActionMessage.Should().Be("Ready");
    }
}