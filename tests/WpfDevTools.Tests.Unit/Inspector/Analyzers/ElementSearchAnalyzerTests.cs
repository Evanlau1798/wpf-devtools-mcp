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

    [StaFact]
    public void FindElements_WithMaxTraversalNodes_ShouldTruncateBeforeScanningFullSubtree()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            var panel = new StackPanel();
            for (var i = 0; i < 8; i++)
            {
                panel.Children.Add(new Button { Name = $"Button{i}" });
            }

            window.Content = panel;
            window.Show();
            window.UpdateLayout();
            var rootId = finder.GenerateElementId(panel);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElementsWithTraversalBudget(
                rootElementId: rootId,
                propertyName: "DefinitelyMissing",
                maxTraversalNodes: 3));

            result.GetProperty("success").GetBoolean().Should().BeTrue(JsonSerializer.Serialize(result));
            result.GetProperty("resultCount").GetInt32().Should().Be(0);
            result.GetProperty("truncated").GetBoolean().Should().BeTrue();
            result.GetProperty("traversalTruncated").GetBoolean().Should().BeTrue();
            result.GetProperty("traversalNodeCount").GetInt32().Should().Be(3);
            result.GetProperty("maxTraversalNodes").GetInt32().Should().Be(3);
            result.GetProperty("truncationReason").GetString().Should().Be("maxTraversalNodes");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithRepeatedMissingDependencyProperty_ShouldCacheNegativeLookupByType()
    {
        var finder = new ElementFinder();
        var dependencyPropertyLookupCount = 0;
        var analyzer = new ElementSearchAnalyzer(
            finder,
            dependencyPropertyResolver: (_, _) =>
            {
                dependencyPropertyLookupCount++;
                return null;
            });
        var window = CreateWindow();
        try
        {
            window.Content = new StackPanel
            {
                Children =
                {
                    new Button { Name = "First" },
                    new Button { Name = "Second" },
                    new Button { Name = "Third" }
                }
            };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                typeName: "Button",
                propertyName: "DefinitelyMissing"));

            result.GetProperty("success").GetBoolean().Should().BeTrue(JsonSerializer.Serialize(result));
            result.GetProperty("resultCount").GetInt32().Should().Be(0);
            dependencyPropertyLookupCount.Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithManyUniqueMissingDependencyProperties_ShouldBoundLookupCache()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(
            finder,
            dependencyPropertyResolver: (_, _) => null);
        var button = new Button { Name = "CacheProbe" };
        var window = CreateWindow();
        try
        {
            window.Content = button;
            window.Show();
            window.UpdateLayout();
            var buttonId = finder.GenerateElementId(button);

            for (var i = 0; i < ElementSearchAnalyzer.MaxDependencyPropertyCacheEntries + 20; i++)
            {
                _ = analyzer.FindElements(
                    rootElementId: buttonId,
                    propertyName: $"Missing{i}");
            }

            analyzer.DependencyPropertyCacheEntryCount.Should()
                .BeLessThanOrEqualTo(ElementSearchAnalyzer.MaxDependencyPropertyCacheEntries);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithOverlongPropertyName_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new Button { Name = "SaveButton" };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);
            var propertyName = new string('A', ElementSearchAnalyzer.MaxSearchPropertyNameLength + 1);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                propertyName: propertyName));

            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
            result.GetProperty("error").GetString().Should().Contain("propertyName");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithNonPositiveMaxTraversalNodes_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new Button { Name = "SaveButton" };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElementsWithTraversalBudget(
                rootElementId: windowId,
                typeName: "Button",
                maxTraversalNodes: 0));

            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
            result.GetProperty("error").GetString().Should().Contain("maxTraversalNodes");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithContainsMatchMode_ShouldUseCaseInsensitiveSubstringMatching()
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
                    new TextBlock { Name = "ErrorStatusText", Text = "Problem" },
                    new TextBlock { Name = "ReadyText", Text = "Ready" }
                }
            };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                elementName: "error",
                matchMode: "contains"));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("resultCount").GetInt32().Should().Be(1);
            result.GetProperty("results")[0].GetProperty("elementName").GetString().Should().Be("ErrorStatusText");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithMultipleTypeNames_ShouldMatchAnyRequestedType()
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
                    new Button { Name = "SaveButton" },
                    new CheckBox { Name = "EnabledCheckBox" },
                    new TextBox { Name = "EditorTextBox" }
                }
            };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                typeNames: new[] { "Button", "CheckBox" }));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.GetProperty("resultCount").GetInt32().Should().Be(2);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithInvalidMatchMode_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new Button { Name = "SaveButton" };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                elementName: "Save",
                matchMode: "prefix"));

            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FindElements_WithTypeNameAndTypeNames_ShouldReturnStructuredInvalidArgument()
    {
        var finder = new ElementFinder();
        var analyzer = new ElementSearchAnalyzer(finder);
        var window = CreateWindow();
        try
        {
            window.Content = new Button { Name = "SaveButton" };
            window.Show();
            window.UpdateLayout();
            var windowId = finder.GenerateElementId(window);

            var result = JsonSerializer.SerializeToElement(analyzer.FindElements(
                rootElementId: windowId,
                typeName: "Button",
                typeNames: new[] { "Button", "CheckBox" }));

            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
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
