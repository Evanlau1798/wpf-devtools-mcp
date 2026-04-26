using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Text.Json;
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

    [StaFact]
    public void GetBindingErrors_WithLiveScanBudgetExceeded_ShouldReturnTruncationMetadata()
    {
        var finder = new ElementFinder();
        var listener = BindingErrorTraceListener.CreateForTesting();
        var analyzer = new BindingAnalyzer(finder, null, listener);

        var first = new TextBox { DataContext = new { Present = "ok" } };
        first.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("MissingFirst"));

        var second = new TextBox { DataContext = new { Present = "ok" } };
        second.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("MissingSecond"));

        var root = new StackPanel();
        root.Children.Add(first);
        root.Children.Add(second);
        finder.GenerateElementId(root);

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetBindingErrors(maxErrors: 1, maxLiveScanNodes: 2));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().BeLessThanOrEqualTo(1);
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();

        var metadata = result.GetProperty("truncationMetadata");
        metadata.GetProperty("liveScan").GetProperty("maxTraversalNodes").GetInt32().Should().Be(2);
        metadata.GetProperty("reasons").EnumerateArray()
            .Select(reason => reason.GetString())
            .Should().Contain("LiveBindingTraversalNodeLimit");
    }
}
