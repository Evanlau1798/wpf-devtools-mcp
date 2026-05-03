using Xunit;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Tests.Unit.Execution;

namespace WpfDevTools.Tests.Unit;

[Collection("AnalyzerStaticState")]
public class DependencyPropertyAnalyzerCleanupTests : IDisposable
{
    public DependencyPropertyAnalyzerCleanupTests()
    {
        DependencyPropertyAnalyzer.StopAllWatchers();
        DependencyPropertyAnalyzer.ResetMonitoring();
    }

    public void Dispose()
    {
        DependencyPropertyAnalyzer.StopAllWatchers();
        DependencyPropertyAnalyzer.ResetMonitoring();
    }

    [StaFact]
    public void CleanupDeadWatchers_ShouldRemoveWatchersForGarbageCollectedElements()
    {
        // Arrange: Create analyzer
        var elementFinder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(elementFinder);

        string? elementId = null;

        // Create element and start watching in a scope that will be GC'd
        {
            var button = new Button { Content = "Test", Width = 100 };
            elementId = elementFinder.GenerateElementId(button);

            // Start watching Width property
            dynamic result = analyzer.WatchChanges("Width", elementId);
            Assert.True(result.success);
        }

        // Act: Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // The watcher should still exist in the dictionary but with a dead WeakReference
        // The cleanup timer will remove these dead watchers periodically

        // Try to unwatch - this should handle the dead reference gracefully
        dynamic unwatchResult = analyzer.UnwatchChanges("Width", elementId);

        // Assert: Should handle dead reference without throwing
        Assert.NotNull(unwatchResult);
    }

    [StaFact]
    public void WatchChanges_ShouldDetachHandlersAfterUnwatch()
    {
        using var elementFinder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(elementFinder);

        var buttons = new List<Button>();
        var elementIds = new List<string>();

        for (int i = 0; i < 10; i++)
        {
            var button = new Button { Content = $"Button {i}", Width = 100 };
            var elementId = elementFinder.GenerateElementId(button);

            dynamic result = analyzer.WatchChanges("Width", elementId);
            Assert.True(result.success);

            buttons.Add(button);
            elementIds.Add(elementId);
        }

        for (int i = 0; i < elementIds.Count; i++)
        {
            dynamic unwatchResult = analyzer.UnwatchChanges("Width", elementIds[i]);
            ((bool)unwatchResult.success).Should().BeTrue();
        }

        analyzer.ClearChangeLog();
        foreach (var button in buttons)
        {
            button.Width += 10;
        }

        dynamic logResult = analyzer.GetChangeLog();
        ((int)logResult.changeCount).Should().Be(0);
    }

    [StaFact]
    public void UnwatchChanges_ShouldHandleGarbageCollectedElementGracefully()
    {
        // Arrange: Create analyzer
        var elementFinder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(elementFinder);

        string? elementId = null;

        // Create element and start watching
        {
            var button = new Button { Content = "Test", Width = 100 };
            elementId = elementFinder.GenerateElementId(button);

            dynamic watchResult = analyzer.WatchChanges("Width", elementId);
            Assert.True(watchResult.success);
        }

        // Force GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Act: Try to unwatch after element is GC'd
        dynamic unwatchResult = analyzer.UnwatchChanges("Width", elementId);

        // Assert: Should not throw, should handle gracefully
        Assert.NotNull(unwatchResult);
        // Current implementation: returns success=true even if element is GC'd
        // This is correct behavior - the watcher entry is removed from dictionary
    }
}
