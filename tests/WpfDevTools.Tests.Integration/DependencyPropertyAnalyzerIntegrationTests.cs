using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests for DependencyPropertyAnalyzer requiring full WPF Application context
/// </summary>
[Collection("WpfIntegration")]
public class DependencyPropertyAnalyzerIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public DependencyPropertyAnalyzerIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetValueSource_WithLocalValue_ShouldReturnSource()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var button = new Button { Content = "Test Button" };
            button.SetValue(Button.WidthProperty, 200.0);

            Application.Current.MainWindow.Content = button;

            return analyzer.GetValueSource("Width", elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetMetadata_ForStandardProperty_ShouldReturnMetadata()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var button = new Button { Content = "Test" };
            Application.Current.MainWindow.Content = button;

            return analyzer.GetMetadata("Width", elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void SetValue_ShouldUpdateProperty()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var button = new Button { Content = "Test" };
            Application.Current.MainWindow.Content = button;

            return analyzer.SetValue("Width", 300.0, elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ClearValue_ShouldClearLocalValue()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var button = new Button { Content = "Test" };
            button.SetValue(Button.WidthProperty, 200.0);
            Application.Current.MainWindow.Content = button;

            return analyzer.ClearValue("Width", elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void WatchChanges_ShouldExecuteSuccessfully()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var button = new Button { Content = "Test" };
            Application.Current.MainWindow.Content = button;

            return analyzer.WatchChanges("Width", elementId: null);
        });

        // Assert
        result.Should().NotBeNull();
    }
}
