using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class ElementSearchHandlerTests
{
    [StaFact]
    public async Task HandleAsync_WithQuery_ShouldMatchSemanticFields()
    {
        using var finder = new ElementFinder();
        var handler = new ElementSearchHandlers(new ElementSearchAnalyzer(finder));
        var window = new Window();
        try
        {
            window.Content = new StackPanel
            {
                Children =
                {
                    new Button { Name = "PrimaryAction", Content = "Apply Changes" },
                    new TextBox { Name = "EditorTextBox", Text = "Draft" }
                }
            };
            window.Show();
            window.UpdateLayout();
            var rootId = finder.GenerateElementId(window);
            var parameters = ToJsonElement(new
            {
                elementId = rootId,
                query = "Apply",
                typeName = "Button",
                maxResults = 5
            });

            var result = await handler.HandleAsync("find_elements", parameters, CancellationToken.None);
            var json = JsonSerializer.SerializeToElement(result);

            json.GetProperty("success").GetBoolean().Should().BeTrue(JsonSerializer.Serialize(json));
            json.GetProperty("resultCount").GetInt32().Should().Be(1);
            var match = json.GetProperty("results")[0];
            match.GetProperty("elementType").GetString().Should().Be("Button");
            match.GetProperty("elementName").GetString().Should().Be("PrimaryAction");
            match.GetProperty("matchedProperty").GetString().Should().Be("Content");
            match.GetProperty("matchedValue").GetString().Should().Be("Apply Changes");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public async Task HandleAsync_WithMaxTraversalNodes_ShouldForwardTraversalBudgetToAnalyzer()
    {
        using var finder = new ElementFinder();
        var handler = new ElementSearchHandlers(new ElementSearchAnalyzer(finder));
        var window = new Window();
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
            var rootId = finder.GenerateElementId(window);
            var parameters = ToJsonElement(new
            {
                elementId = rootId,
                propertyName = "DefinitelyMissing",
                maxTraversalNodes = 2
            });

            var result = await handler.HandleAsync("find_elements", parameters, CancellationToken.None);
            var json = JsonSerializer.SerializeToElement(result);

            json.GetProperty("success").GetBoolean().Should().BeTrue(JsonSerializer.Serialize(json));
            json.GetProperty("traversalTruncated").GetBoolean().Should().BeTrue();
            json.GetProperty("traversalNodeCount").GetInt32().Should().Be(2);
            json.GetProperty("maxTraversalNodes").GetInt32().Should().Be(2);
        }
        finally
        {
            window.Close();
        }
    }
}
