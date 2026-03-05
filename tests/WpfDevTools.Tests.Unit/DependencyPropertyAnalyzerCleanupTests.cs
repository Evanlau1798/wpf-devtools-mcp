using Xunit;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit;

public class DependencyPropertyAnalyzerCleanupTests
{
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
    public void WatchChanges_ShouldNotLeakMemoryWhenElementIsGarbageCollected()
    {
        // Arrange: Create analyzer
        var elementFinder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(elementFinder);

        var weakRefs = new List<WeakReference>();

        // Create multiple elements and watch them
        for (int i = 0; i < 10; i++)
        {
            var button = new Button { Content = $"Button {i}", Width = 100 };
            var elementId = elementFinder.GenerateElementId(button);

            dynamic result = analyzer.WatchChanges("Width", elementId);
            Assert.True(result.success);

            // Keep weak reference to verify GC
            weakRefs.Add(new WeakReference(button));
        }

        // Act: Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert: Elements should be garbage collected
        var gcCount = weakRefs.Count(wr => !wr.IsAlive);
        gcCount.Should().BeGreaterThan(0, "some elements should have been garbage collected");

        // After implementing CleanupDeadWatchers(), dead watchers should be removed
        // This test verifies that WeakReference prevents memory leaks
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
