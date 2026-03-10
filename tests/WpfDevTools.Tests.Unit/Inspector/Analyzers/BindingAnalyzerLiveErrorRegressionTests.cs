using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class BindingAnalyzerLiveErrorRegressionTests
{
    [StaFact]
    public void LiveBindingScan_WithNonVisualLogicalChild_ShouldNotThrow()
    {
        var root = new StackPanel();
        var textBlock = new TextBlock();
        textBlock.Inlines.Add(new Run("inline text"));
        textBlock.SetBinding(FrameworkElement.TagProperty, new System.Windows.Data.Binding("MissingProperty"));
        root.Children.Add(textBlock);

        // DependencyObjectTraversal (used by live binding scan) must tolerate
        // logical children that are not Visual (e.g., Run inside TextBlock.Inlines).
        var act = () => DependencyObjectTraversal.EnumerateDescendantsAndSelf(root).ToList();

        act.Should().NotThrow("live binding scans should tolerate logical children that are not visual elements");
    }
}
