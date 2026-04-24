using Xunit;
using FluentAssertions;
using System.Globalization;
using System.Text.Json;
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
            var buttonId = elementFinder.GenerateElementId(button);

            Application.Current.MainWindow.Content = button;

            return analyzer.GetValueSource("Width", buttonId);
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("baseValueSource").GetString().Should().Be("LocalValue");
        json.GetProperty("currentValue").GetString().Should().Be("200");
        json.GetProperty("hadLocalValue").GetBoolean().Should().BeTrue();
        json.GetProperty("localValue").GetString().Should().Be("200");
    }

    [Fact]
    public void GetMetadata_ForButtonSpecificProperty_ShouldReturnMetadata()
    {
        // Arrange & Act
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var button = new Button { Content = "Test" };
            var buttonId = elementFinder.GenerateElementId(button);
            Application.Current.MainWindow.Content = button;

            return analyzer.GetMetadata("IsDefault", buttonId);
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("propertyName").GetString().Should().Be("IsDefault");
        json.GetProperty("propertyType").GetString().Should().Be("Boolean");
        json.GetProperty("ownerType").GetString().Should().Be("Button");
        json.GetProperty("defaultValue").GetString().Should().Be("False");
        json.GetProperty("isReadOnly").GetBoolean().Should().BeFalse();
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
            var buttonId = elementFinder.GenerateElementId(button);
            Application.Current.MainWindow.Content = button;

            var setResult = analyzer.SetValue("Width", 300.0, buttonId);
            return new
            {
                result = setResult,
                actualWidth = button.Width
            };
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("actualWidth").GetDouble().Should().Be(300.0);
        var setResultJson = json.GetProperty("result");
        setResultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        setResultJson.GetProperty("propertyName").GetString().Should().Be("Width");
        setResultJson.GetProperty("newValue").GetString().Should().Be("300");
        setResultJson.GetProperty("baseValueSource").GetString().Should().Be("Local");
        setResultJson.GetProperty("valueType").GetString().Should().Be("Double");
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
            var buttonId = elementFinder.GenerateElementId(button);
            Application.Current.MainWindow.Content = button;

            var clearResult = analyzer.ClearValue("Width", buttonId);
            return new
            {
                result = clearResult,
                actualWidth = double.IsNaN(button.Width)
                    ? "NaN"
                    : button.Width.ToString(CultureInfo.InvariantCulture),
                hasLocalValue = button.ReadLocalValue(Button.WidthProperty) != DependencyProperty.UnsetValue
            };
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("actualWidth").GetString().Should().Be("NaN");
        json.GetProperty("hasLocalValue").GetBoolean().Should().BeFalse();
        var clearResultJson = json.GetProperty("result");
        clearResultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        clearResultJson.GetProperty("propertyName").GetString().Should().Be("Width");
        clearResultJson.GetProperty("hadLocalValue").GetBoolean().Should().BeTrue();
        clearResultJson.GetProperty("baseValueSource").GetString().Should().Be("Default");
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
            var buttonId = elementFinder.GenerateElementId(button);
            Application.Current.MainWindow.Content = button;

            analyzer.ClearChangeLog();
            var watchResult = analyzer.WatchChanges("Width", buttonId);
            button.Width = 240.0;
            var changeLog = analyzer.GetChangeLog();
            var unwatchResult = analyzer.UnwatchChanges("Width", buttonId);
            analyzer.ClearChangeLog();

            return new
            {
                buttonId,
                watchResult,
                changeLog,
                unwatchResult
            };
        });

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        var watchResultJson = json.GetProperty("watchResult");
        watchResultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        watchResultJson.GetProperty("propertyName").GetString().Should().Be("Width");

        var changeLogJson = json.GetProperty("changeLog");
        changeLogJson.GetProperty("success").GetBoolean().Should().BeTrue();
        changeLogJson.GetProperty("changeCount").GetInt32().Should().BeGreaterThan(0);
        var buttonId = json.GetProperty("buttonId").GetString();
        var matchingChange = changeLogJson
            .GetProperty("changes")
            .EnumerateArray()
            .Any(change =>
                change.GetProperty("elementId").GetString() == buttonId
                && change.GetProperty("propertyName").GetString() == "Width"
                && change.GetProperty("newValue").GetString() == "240");
        matchingChange.Should().BeTrue();

        var unwatchResultJson = json.GetProperty("unwatchResult");
        unwatchResultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        unwatchResultJson.GetProperty("propertyName").GetString().Should().Be("Width");
    }
}
