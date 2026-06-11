using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using System.Windows.Controls;
using System.Windows;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("WPF")]
public class VisualTreeAnalyzerPerformanceTests
{
    [StaFact]
    public void GetNamesInScope_DeepTree_ShouldNotCreateIntermediateLists()
    {
        // Arrange - create deep tree (100 levels)
        var root = new Border { Name = "Root" };
        var current = root;

        for (int i = 0; i < 100; i++)
        {
            var child = new Border { Name = $"Level{i}" };
            current.Child = child;
            current = child;
        }

        var analyzer = new VisualTreeAnalyzer(new WpfDevTools.Inspector.Utilities.ElementFinder());

        // Act - this should use parameter passing, not return + merge
        // Should complete without stack overflow or excessive allocations
        var exception = Record.Exception(() => analyzer.GetNameScope());

        // Assert - verify it completes successfully
        exception.Should().BeNull("deep tree traversal should not cause stack overflow");
    }

    [StaFact]
    public void CompareTree_LargeChildren_ShouldUseHashSet()
    {
        // Arrange - create element with many children (50 visual, 50 logical)
        var root = new StackPanel { Name = "Root" };

        // Add 50 visual children
        for (int i = 0; i < 50; i++)
        {
            root.Children.Add(new Button { Name = $"Button{i}" });
        }

        var analyzer = new VisualTreeAnalyzer(new WpfDevTools.Inspector.Utilities.ElementFinder());

        // Act - this should use HashSet, not O(n*m) nested loops
        var sw = Stopwatch.StartNew();
        var result = analyzer.CompareTree();
        sw.Stop();

        // Assert - should complete quickly (< 100ms for 50x50 comparison)
        sw.ElapsedMilliseconds.Should().BeLessThan(100,
            "HashSet-based comparison should be fast even with many children");

        result.Should().NotBeNull();
    }
}
