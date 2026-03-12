using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class UiSummaryAnalyzerTests
{
    [StaFact]
    public void GetUiSummary_ShouldSuppressLayoutNodesAndKeepSemanticControls()
    {
        var finder = new ElementFinder();
        var analyzer = new UiSummaryAnalyzer(finder);
        var root = new StackPanel
        {
            Name = "RootPanel",
            Children =
            {
                new Grid
                {
                    Children =
                    {
                        new TextBox { Name = "NameBox", Text = "Ada" },
                        new Button { Name = "SaveButton", Content = "Save", IsEnabled = false }
                    }
                },
                new TextBlock { Name = "StatusText", Text = "Ready" }
            }
        };
        var elementId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(analyzer.GetUiSummary(elementId, depth: 3));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("semanticNodeCount").GetInt32().Should().Be(3);
        result.GetProperty("summaryText").GetString().Should().Contain("TextBox NameBox");
        result.GetProperty("summaryText").GetString().Should().Contain("Button SaveButton");
        result.GetProperty("summaryText").GetString().Should().NotContain("Grid");
    }

    [StaFact]
    public void GetUiSummary_ShouldRespectDepthLimit()
    {
        var finder = new ElementFinder();
        var analyzer = new UiSummaryAnalyzer(finder);
        var nested = new StackPanel
        {
            Name = "OuterPanel",
            Children =
            {
                new Button { Name = "TopButton", Content = "Top" },
                new Border
                {
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new TextBox { Name = "DeepBox", Text = "Too deep" }
                        }
                    }
                }
            }
        };
        var elementId = finder.GenerateElementId(nested);

        var result = JsonSerializer.SerializeToElement(analyzer.GetUiSummary(elementId, depth: 1));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summaryText").GetString().Should().Contain("TopButton");
        result.GetProperty("summaryText").GetString().Should().NotContain("DeepBox");
    }

    [StaFact]
    public void GetUiSummary_WhenRootElementIsSemantic_ShouldIncludeRootNode()
    {
        var finder = new ElementFinder();
        var analyzer = new UiSummaryAnalyzer(finder);
        var root = new Button
        {
            Name = "SaveButton",
            Content = "Save",
            IsEnabled = false
        };
        var elementId = finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(analyzer.GetUiSummary(elementId, depth: 0));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("semanticNodeCount").GetInt32().Should().Be(1);
        result.GetProperty("summaryText").GetString().Should().Contain("Button SaveButton");
        result.GetProperty("nodes")[0].GetProperty("elementId").GetString().Should().Be(elementId);
    }

    [StaFact]
    public void GetUiSummary_WhenSemanticContentIsLogicalOnly_ShouldIncludeInactiveTabContent()
    {
        var finder = new ElementFinder();
        var analyzer = new UiSummaryAnalyzer(finder);
        var hiddenTabBox = new TextBox
        {
            Name = "HiddenTabBox",
            Text = "Hidden"
        };
        var tabs = new TabControl
        {
            Name = "MainTabs",
            Items =
            {
                new TabItem
                {
                    Header = "Visible",
                    Content = new TextBlock
                    {
                        Name = "VisibleStatus",
                        Text = "Ready"
                    }
                },
                new TabItem
                {
                    Header = "Hidden",
                    Content = hiddenTabBox
                }
            }
        };
        tabs.SelectedIndex = 0;
        var elementId = finder.GenerateElementId(tabs);

        var result = JsonSerializer.SerializeToElement(analyzer.GetUiSummary(elementId, depth: 3));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summaryText").GetString().Should().Contain("HiddenTabBox");
        result.GetProperty("semanticNodeCount").GetInt32().Should().BeGreaterThan(1);
    }
}
