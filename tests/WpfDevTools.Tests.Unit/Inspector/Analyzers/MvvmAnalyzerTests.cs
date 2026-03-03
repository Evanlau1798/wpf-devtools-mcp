using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class MvvmAnalyzerTests
{
    [Fact]
    public void GetViewModel_WithNullElementId_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);

        // Act
        var result = analyzer.GetViewModel(null);

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void ModifyViewModel_WithValidProperty_ShouldModifyValue()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new MvvmAnalyzer(finder);

        var viewModel = new TestViewModel { Name = "Original" };
        var button = new Button { DataContext = viewModel };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.ModifyViewModel(elementId, "Name", "Modified");

        // Assert
        result.Should().NotBeNull();
        viewModel.Name.Should().Be("Modified");
    }

    private class TestViewModel
    {
        public string Name { get; set; } = string.Empty;
    }
}
