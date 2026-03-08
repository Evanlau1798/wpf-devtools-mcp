using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class BindingAnalyzerLiveErrorRegressionTests
{
    [StaFact]
    public void LiveBindingScan_WithNonVisualLogicalChild_ShouldNotThrow()
    {
        var analyzer = new BindingAnalyzer(new ElementFinder());
        var root = new StackPanel();
        var textBlock = new TextBlock();
        textBlock.Inlines.Add(new Run("inline text"));
        textBlock.SetBinding(FrameworkElement.TagProperty, new System.Windows.Data.Binding("MissingProperty"));
        root.Children.Add(textBlock);

        var method = typeof(BindingAnalyzer).GetMethod(
            "CollectLiveBindingErrorsRecursive",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        var visited = new HashSet<DependencyObject>();
        var errors = new List<BindingErrorInfo>();

        var act = () => method!.Invoke(analyzer, new object[] { root, visited, errors });

        act.Should().NotThrow("live binding scans should tolerate logical children that are not visual elements");
    }
}
