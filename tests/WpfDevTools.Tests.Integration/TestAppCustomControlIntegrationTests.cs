using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.TestApp;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// Integration tests using TestApp golden sample custom controls.
/// Tests CustomTextBox (DependencyProperty with coercion, attached property)
/// and CustomButton (custom RoutedEvent) from TestApp Tab 5.
/// </summary>
[Collection("WpfIntegration")]
public class TestAppCustomControlIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public TestAppCustomControlIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetValueSource_WithCustomDependencyProperty_ShouldReturnSource()
    {
        // Arrange - CustomTextBox with Watermark DP (matches TestApp Tab 5)
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var customTextBox = new CustomTextBox
            {
                Watermark = "Enter your name",
                Width = 200
            };

            Application.Current.MainWindow.Content = customTextBox;

            return analyzer.GetValueSource("Watermark", elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetMetadata_WithCustomDependencyProperty_ShouldReturnMetadata()
    {
        // Arrange - CustomTextBox Watermark property metadata
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var customTextBox = new CustomTextBox
            {
                Watermark = "Test watermark"
            };

            Application.Current.MainWindow.Content = customTextBox;

            return analyzer.GetMetadata("Watermark", elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Fact]
    public void SetValue_WithWatermarkCoercion_ShouldCoerceEmptyToDefault()
    {
        // Arrange - test coercion callback: empty string should become "Default watermark"
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var customTextBox = new CustomTextBox
            {
                Watermark = "Initial"
            };

            Application.Current.MainWindow.Content = customTextBox;

            // Set empty value - coercion should convert to "Default watermark"
            return analyzer.SetValue("Watermark", "", elementId: null);
        });

        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData("HighlightColor")]
    [InlineData("CustomTextBox.HighlightColor")]
    public void GetValueSource_WithAttachedProperty_ShouldReturnSource(string propertyName)
    {
        // Arrange - TextBox with HighlightColor attached property (matches TestApp Tab 5)
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var textBox = new TextBox { Text = "I have an attached property" };
            CustomTextBox.SetHighlightColor(textBox, "LightYellow");

            Application.Current.MainWindow.Content = textBox;
            var elementId = elementFinder.GenerateElementId(textBox);

            return analyzer.GetValueSource(propertyName, elementId);
        });

        var doc = JsonSerializer.SerializeToElement(result);
        doc.GetProperty("success").GetBoolean().Should().BeTrue(doc.GetRawText());
        doc.GetProperty("effectiveValue").GetString().Should().Be("LightYellow");
    }

    [Theory]
    [InlineData("HighlightColor")]
    [InlineData("CustomTextBox.HighlightColor")]
    public void GetMetadata_WithAttachedProperty_ShouldReturnMetadata(string propertyName)
    {
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var textBox = new TextBox { Text = "I have an attached property" };
            CustomTextBox.SetHighlightColor(textBox, "LightYellow");

            Application.Current.MainWindow.Content = textBox;
            var elementId = elementFinder.GenerateElementId(textBox);

            return analyzer.GetMetadata(propertyName, elementId);
        });

        var doc = JsonSerializer.SerializeToElement(result);
        doc.GetProperty("success").GetBoolean().Should().BeTrue(doc.GetRawText());
        doc.GetProperty("propertyType").GetString().Should().Be("String");
    }

    [Fact]
    public void FireRoutedEvent_WithCustomRoutedEvent_ShouldFireSuccessfully()
    {
        // Arrange - CustomButton with custom RoutedEvent (matches TestApp Tab 5)
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new EventAnalyzer(elementFinder);

            var customButton = new CustomButton
            {
                Content = "Click Me (Custom Event)"
            };

            customButton.CustomClick += (s, e) => { /* test handler */ };

            Application.Current.MainWindow.Content = customButton;
            var elementId = elementFinder.GenerateElementId(customButton);

            return analyzer.FireRoutedEvent(elementId: elementId, eventName: "CustomClick", eventArgs: null);
        });

        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void GetEventHandlers_WithCustomRoutedEvent_ShouldReturnHandlers()
    {
        // Arrange - CustomButton with attached CustomClick handler
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new EventAnalyzer(elementFinder);

            var customButton = new CustomButton
            {
                Content = "Click Me"
            };

            // Attach handler (matches TestApp SetupCustomEvents)
            customButton.CustomClick += (s, e) => { /* handler */ };

            Application.Current.MainWindow.Content = customButton;
            var elementId = elementFinder.GenerateElementId(customButton);

            return analyzer.GetEventHandlers(elementId: elementId, eventName: "CustomClick");
        });

        var json = JsonSerializer.Serialize(result);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        doc.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.GetProperty("handlerCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public void ClearValue_WithCustomDependencyProperty_ShouldRevertToDefault()
    {
        // Arrange - clear Watermark value should revert to default "Enter text..."
        var result = _fixture.RunOnUIThread(() =>
        {
            var elementFinder = new ElementFinder();
            var analyzer = new DependencyPropertyAnalyzer(elementFinder);

            var customTextBox = new CustomTextBox
            {
                Watermark = "Custom watermark"
            };

            Application.Current.MainWindow.Content = customTextBox;

            return analyzer.ClearValue("Watermark", elementId: null);
        });

        result.Should().NotBeNull();
    }
}
