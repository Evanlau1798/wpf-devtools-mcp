using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class SceneSummaryElementHelpersTests
{
    [StaFact]
    public void GetNextTraversalDepth_WithVisualMode_ShouldCountWrapperDepth()
    {
        var wrapper = new Grid();
        var semantic = new TextBox();

        SceneSummaryElementHelpers.GetNextTraversalDepth(wrapper, currentDepth: 0, SceneTraversalDepthMode.Visual)
            .Should()
            .Be(1);
        SceneSummaryElementHelpers.GetNextTraversalDepth(semantic, currentDepth: 1, SceneTraversalDepthMode.Visual)
            .Should()
            .Be(2);
    }

    [StaFact]
    public void GetNextTraversalDepth_WithSemanticMode_ShouldSkipWrapperDepth()
    {
        var wrapper = new Grid();
        var semantic = new TextBox();

        SceneSummaryElementHelpers.GetNextTraversalDepth(wrapper, currentDepth: 0, SceneTraversalDepthMode.Semantic)
            .Should()
            .Be(0);
        SceneSummaryElementHelpers.GetNextTraversalDepth(semantic, currentDepth: 0, SceneTraversalDepthMode.Semantic)
            .Should()
            .Be(1);
    }

    [StaFact]
    public void GetSceneChildren_ShouldDeduplicateVisualAndLogicalChildren()
    {
        var tab = new TabItem
        {
            Header = "Profile",
            Content = new TextBox
            {
                Name = "ProfileBox",
                Text = "Edge"
            }
        };
        var tabs = new TabControl
        {
            Items =
            {
                tab
            }
        };
        var window = new Window
        {
            Content = tabs
        };

        window.Show();
        try
        {
            window.ApplyTemplate();
            tabs.ApplyTemplate();
            window.UpdateLayout();

            SceneSummaryElementHelpers.GetSceneChildren(tabs)
                .Should()
                .OnlyHaveUniqueItems();
        }
        finally
        {
            window.Close();
        }
    }
}
