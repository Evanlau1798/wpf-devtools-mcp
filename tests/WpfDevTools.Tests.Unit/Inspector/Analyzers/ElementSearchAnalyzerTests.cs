using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class ElementSearchAnalyzerTests
{
    [StaFact]
    public void FindElements_ByType_ShouldReturnMatchingElements()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new StackPanel
            {
                Children =
                {
                    new Button { Name = "PrimaryButton", Content = "Save" },
                    new TextBox { Name = "EditorTextBox", Text = "Hello" }
                }
            };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(rootElementId: windowId, typeName: "Button"));

            result.GetProperty("success").GetBoolean().Should().BeTrue(JsonSerializer.Serialize(result));
            result.GetProperty("resultCount").GetInt32().Should().Be(1);
            result.GetProperty("results")[0].GetProperty("elementType").GetString().Should().Be("Button");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_ByNameAndAutomationId_ShouldReturnMatchedMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            var button = new Button { Name = "SearchButton", Content = "Find" };
            AutomationProperties.SetAutomationId(button, "SearchButton.Auto");
            window.Content = button;
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                elementName: "SearchButton",
                automationId: "SearchButton.Auto"));

            result.GetProperty("success").GetBoolean().Should().BeTrue(JsonSerializer.Serialize(result));
            result.GetProperty("resultCount").GetInt32().Should().Be(1);
            var match = result.GetProperty("results")[0];
            match.GetProperty("elementName").GetString().Should().Be("SearchButton");
            match.GetProperty("automationId").GetString().Should().Be("SearchButton.Auto");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_ByPropertyFilter_ShouldReturnMatchedPropertyDetails()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Name = "StatusText", Text = "Ready" },
                    new TextBlock { Name = "OtherText", Text = "Busy" }
                }
            };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                propertyName: "Text",
                propertyValue: "Ready"));

            result.GetProperty("success").GetBoolean().Should().BeTrue(JsonSerializer.Serialize(result));
            result.GetProperty("resultCount").GetInt32().Should().Be(1);
            var match = result.GetProperty("results")[0];
            match.GetProperty("matchedProperty").GetString().Should().Be("Text");
            match.GetProperty("matchedValue").GetString().Should().Be("Ready");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithMaxResults_ShouldStopEarly()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new StackPanel
            {
                Children =
                {
                    new Button { Name = "Button1", Content = "One" },
                    new Button { Name = "Button2", Content = "Two" },
                    new Button { Name = "Button3", Content = "Three" }
                }
            };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(rootElementId: windowId, typeName: "Button", maxResults: 2));

            result.GetProperty("success").GetBoolean().Should().BeTrue(JsonSerializer.Serialize(result));
            result.GetProperty("resultCount").GetInt32().Should().Be(2);
            result.GetProperty("truncated").GetBoolean().Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateWindow()
    {
        var window = new Window();
        NameScope.SetNameScope(window, new NameScope());
        return window;
    }
}
