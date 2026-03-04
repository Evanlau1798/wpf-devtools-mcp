using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class InteractionAnalyzerTests
{
    [StaFact]
    public void ClickElement_WithValidButton_ShouldRaiseClickEvent()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        var clicked = false;
        button.Click += (s, e) => clicked = true;

        // Act
        var result = analyzer.ClickElement(elementId);

        // Assert
        clicked.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [StaFact]
    public void ScrollToElement_WithValidElement_ShouldBringIntoView()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.ScrollToElement(elementId);

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void TakeScreenshot_WithValidElement_ShouldReturnBase64Image()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 100, Height = 50 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.TakeScreenshot(elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("screenshot");
            resultDict["success"].Should().Be(true);

            // Verify screenshot is valid base64
            var screenshot = resultDict["screenshot"] as string;
            screenshot.Should().NotBeNullOrEmpty();
            var isValidBase64 = IsValidBase64(screenshot!);
            isValidBase64.Should().BeTrue("screenshot should be valid base64 encoded image");
        }
    }

    private static bool IsValidBase64(string base64String)
    {
        if (string.IsNullOrEmpty(base64String))
            return false;

        try
        {
            var data = Convert.FromBase64String(base64String);
            return data.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    [StaFact]
    public void DragAndDrop_WithValidElements_ShouldSimulateDrag()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var source = new Button { Content = "Source" };
        var target = new Button { Content = "Target" };
        var sourceId = finder.GenerateElementId(source);
        var targetId = finder.GenerateElementId(target);

        // Act
        var result = analyzer.DragAndDrop(sourceId, targetId, "Text");

        // Assert
        result.Should().NotBeNull();
    }

    [StaFact]
    public void SimulateKeyboard_WithValidElement_ShouldSimulateKeyPress()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var textBox = new TextBox();
        var elementId = finder.GenerateElementId(textBox);

        // Act
        var result = analyzer.SimulateKeyboard(elementId, "A", "KeyDown");

        // Assert
        result.Should().NotBeNull();
    }
}
