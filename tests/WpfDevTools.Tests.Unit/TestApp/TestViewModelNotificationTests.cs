using FluentAssertions;
using WpfDevTools.Tests.TestApp;
using Xunit;

namespace WpfDevTools.Tests.Unit.TestApp;

public sealed class TestViewModelNotificationTests
{
    [Fact]
    public void SaveCommand_ShouldRecordNonBlockingStatusMessage()
    {
        var viewModel = new TestViewModel
        {
            Name = "Alice",
            Age = 25
        };

        viewModel.SaveCommand.Execute(null);

        viewModel.LastActionMessage.Should().Be("Saved: Alice, 25");
    }

    [Fact]
    public void ClearCommand_ShouldReplacePreviousStatusMessage()
    {
        var viewModel = new TestViewModel
        {
            Name = "Alice",
            Age = 25
        };

        viewModel.SaveCommand.Execute(null);
        viewModel.ClearCommand.Execute(null);

        viewModel.Name.Should().BeEmpty();
        viewModel.Age.Should().Be(0);
        viewModel.LastActionMessage.Should().Be("Form cleared");
    }
}
