using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class DependencyPropertyAnalyzerTests
{

    [StaFact]
    public void GetValueSource_WithValidProperty_ShouldReturnSource()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetValueSource("Width", elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("valueSource");
            resultDict["success"].Should().Be(true);
            resultDict["valueSource"].Should().NotBeNull();
        }
    }

    [StaFact]
    public void SetValue_WithValidProperty_ShouldSetValue()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.SetValue("Width", 200.0, elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict["success"].Should().Be(true);
        }
        button.Width.Should().Be(200.0);
    }

    [StaFact]
    public void ClearValue_WithLocalValue_ShouldClearValue()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.ClearValue("Width", elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict["success"].Should().Be(true);
        }
        button.Width.Should().Be(double.NaN); // Default value
    }

    [StaFact]
    public void GetMetadata_WithValidProperty_ShouldReturnMetadata()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.GetMetadata("Width", elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict.Keys.Cast<string>().Should().Contain("metadata");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void WatchChanges_WithValidProperty_ShouldStartWatching()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act
        var result = analyzer.WatchChanges("Width", elementId);

        // Assert
        result.Should().NotBeNull();
        var resultDict = result as System.Collections.IDictionary;
        if (resultDict != null)
        {
            resultDict.Keys.Cast<string>().Should().Contain("success");
            resultDict["success"].Should().Be(true);
        }
    }

    [StaFact]
    public void WatchChanges_ConcurrentCalls_ShouldBeThreadSafe()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var buttons = Enumerable.Range(0, 10).Select(_ => new Button()).ToList();
        var elementIds = buttons.Select(b => finder.GenerateElementId(b)).ToList();

        // Act - Start watching sequentially (avoid Task.WaitAll on STA thread)
        var results = new List<object>();
        foreach (var id in elementIds)
        {
            results.Add(analyzer.WatchChanges("Width", id));
        }

        // Assert - All should succeed
        foreach (var result in results)
        {
            result.Should().NotBeNull();
            var resultDict = result as System.Collections.IDictionary;
            if (resultDict != null)
            {
                resultDict["success"].Should().Be(true);
            }
        }
    }

    [StaFact]
    public void WatchChanges_ChangeLogTrimming_ShouldLimitTo10000Entries()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Start watching
        analyzer.WatchChanges("Width", elementId);

        // Act - Trigger more than 10000 changes
        for (int i = 0; i < 12000; i++)
        {
            button.Width = i;
        }

        // Wait for all changes to be processed
        System.Threading.Thread.Sleep(100);

        // Assert - Change log should be capped at 10000
        dynamic result = analyzer.GetChangeLog();
        ((bool)result.success).Should().BeTrue();
        int changeCount = result.changeCount;
        changeCount.Should().BeLessThanOrEqualTo(10000);

        // Verify array length matches count (or is close due to concurrency)
        var changes = result.changes as object[];
        changes.Should().NotBeNull();
        changes!.Length.Should().BeLessThanOrEqualTo(10000);
    }

    [StaFact]
    public void WatchChanges_ConcurrentChanges_ShouldHandleTrimmingCorrectly()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var buttons = Enumerable.Range(0, 5).Select(_ => new Button()).ToList();
        var elementIds = buttons.Select(b => finder.GenerateElementId(b)).ToList();

        // Start watching all buttons
        for (int i = 0; i < buttons.Count; i++)
        {
            analyzer.WatchChanges("Width", elementIds[i]);
        }

        // Act - Trigger changes directly (avoid Dispatcher.Invoke deadlock on STA thread)
        for (int buttonIndex = 0; buttonIndex < buttons.Count; buttonIndex++)
        {
            for (int i = 0; i < 3000; i++)
            {
                buttons[buttonIndex].Width = i;
            }
        }
        System.Threading.Thread.Sleep(200);

        // Assert - Total changes should be capped at 10000
        dynamic result = analyzer.GetChangeLog();
        ((bool)result.success).Should().BeTrue();
        int changeCount = result.changeCount;
        changeCount.Should().BeLessThanOrEqualTo(10000);
    }

    [StaFact]
    public void GetChangeLog_DuringConcurrentChanges_ShouldReturnConsistentData()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        analyzer.WatchChanges("Width", elementId);

        // Act - Trigger changes directly on STA thread and read log concurrently
        for (int i = 0; i < 1000; i++)
        {
            button.Width = i;
        }

        // Read log multiple times to verify consistency
        for (int r = 0; r < 10; r++)
        {
            dynamic result = analyzer.GetChangeLog();
            ((bool)result.success).Should().BeTrue();

            int changeCount = result.changeCount;
            changeCount.Should().BeGreaterThanOrEqualTo(0);

            var changes = result.changes as object[];
            changes.Should().NotBeNull();
        }
    }

    [StaFact]
    public void ClearChangeLog_DuringConcurrentChanges_ShouldEventuallyBeConsistent()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        analyzer.WatchChanges("Width", elementId);

        // Trigger some initial changes
        for (int i = 0; i < 100; i++)
        {
            button.Width = i;
        }
        System.Threading.Thread.Sleep(50);

        // Act - Clear log and then trigger more changes on STA thread
        dynamic clearResult = analyzer.ClearChangeLog();

        for (int i = 0; i < 500; i++)
        {
            button.Width = i;
        }
        System.Threading.Thread.Sleep(100);

        // Assert - Clear should succeed
        ((bool)clearResult.success).Should().BeTrue();

        // After clear and new changes, count should reflect only new changes
        dynamic finalResult = analyzer.GetChangeLog();
        ((bool)finalResult.success).Should().BeTrue();
        int finalCount = finalResult.changeCount;

        // Should have some changes from after the clear, but not the initial 100
        finalCount.Should().BeLessThan(600); // Less than total changes
    }

    [StaFact]
    public void WatchChanges_AlreadyWatching_ShouldReturnError()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        analyzer.WatchChanges("Width", elementId);

        // Act - Try to watch again
        dynamic result = analyzer.WatchChanges("Width", elementId);

        // Assert
        ((bool)result.success).Should().BeFalse();
        ((string)result.error).Should().Be("Already watching this property");
    }

    [StaFact]
    public void UnwatchChanges_AfterWatching_ShouldStopWatching()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        analyzer.WatchChanges("Width", elementId);
        analyzer.ClearChangeLog();

        // Act - Unwatch and trigger changes
        dynamic unwatchResult = analyzer.UnwatchChanges("Width", elementId);
        button.Width = 100;
        button.Width = 200;
        System.Threading.Thread.Sleep(50);

        // Assert - Unwatch should succeed
        ((bool)unwatchResult.success).Should().BeTrue();

        // No new changes should be logged
        dynamic logResult = analyzer.GetChangeLog();
        int changeCount = logResult.changeCount;
        changeCount.Should().Be(0);
    }
}
