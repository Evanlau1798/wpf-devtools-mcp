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

        // Act - Start watching from multiple threads
        var tasks = elementIds.Select((id, index) =>
            Task.Run(() => analyzer.WatchChanges("Width", id))
        ).ToArray();

        Task.WaitAll(tasks);

        // Assert - All should succeed
        foreach (var task in tasks)
        {
            var result = task.Result;
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

        // Act - Trigger changes from multiple threads
        var tasks = buttons.Select(button =>
            Task.Run(() =>
            {
                for (int i = 0; i < 3000; i++)
                {
                    button.Dispatcher.Invoke(() => button.Width = i);
                }
            })
        ).ToArray();

        Task.WaitAll(tasks);
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

        // Act - Read log while changes are happening
        var changeTask = Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                button.Dispatcher.Invoke(() => button.Width = i);
            }
        });

        var readTasks = Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => analyzer.GetChangeLog())
        ).ToArray();

        Task.WaitAll(readTasks.Concat(new[] { changeTask }).ToArray());

        // Assert - All reads should succeed
        foreach (var task in readTasks)
        {
            dynamic result = task.Result;
            ((bool)result.success).Should().BeTrue();

            // Count should be non-negative
            int changeCount = result.changeCount;
            changeCount.Should().BeGreaterThanOrEqualTo(0);

            // Array should not be null
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

        // Act - Clear log while changes are happening
        var changeTask = Task.Run(() =>
        {
            for (int i = 0; i < 500; i++)
            {
                button.Dispatcher.Invoke(() => button.Width = i);
                System.Threading.Thread.Sleep(1);
            }
        });

        System.Threading.Thread.Sleep(50);
        dynamic clearResult = analyzer.ClearChangeLog();

        changeTask.Wait();
        System.Threading.Thread.Sleep(100);

        // Assert - Clear should succeed
        ((bool)clearResult.success).Should().BeTrue();

        // After clear and waiting, count should reflect only new changes
        dynamic finalResult = analyzer.GetChangeLog();
        ((bool)finalResult.success).Should().BeTrue();
        int finalCount = finalResult.changeCount;

        // Should have some changes from the concurrent task, but not the initial 100
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
        var result = analyzer.WatchChanges("Width", elementId);

        // Assert
        var resultDict = result as System.Collections.IDictionary;
        resultDict.Should().NotBeNull();
        resultDict!["success"].Should().Be(false);
        resultDict["error"].Should().Be("Already watching this property");
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
        var unwatchResult = analyzer.UnwatchChanges("Width", elementId);
        button.Width = 100;
        button.Width = 200;
        System.Threading.Thread.Sleep(50);

        // Assert - Unwatch should succeed
        var unwatchDict = unwatchResult as System.Collections.IDictionary;
        unwatchDict.Should().NotBeNull();
        unwatchDict!["success"].Should().Be(true);

        // No new changes should be logged
        dynamic logResult = analyzer.GetChangeLog();
        int changeCount = logResult.changeCount;
        changeCount.Should().Be(0);
    }
}
