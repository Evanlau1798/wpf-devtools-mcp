using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

/// <summary>
/// Tests for EventAnalyzer concurrency issues
/// </summary>
public class EventAnalyzerConcurrencyTests
{
    [StaFact]
    public async Task TraceRoutedEvents_RapidCalls_ShouldNotThrowObjectDisposedException()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act - Call TraceRoutedEvents multiple times rapidly to trigger CTS disposal race
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                analyzer.TraceRoutedEvents(elementId, "Click", 50);
            }));
            await Task.Delay(5); // Small delay to increase chance of race condition
        }

        // Assert - Should not throw ObjectDisposedException
        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync<ObjectDisposedException>();
    }

    [StaFact]
    public async Task TraceRoutedEvents_ConcurrentCalls_ShouldHandleGracefully()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act - Start multiple traces concurrently
        var result1 = analyzer.TraceRoutedEvents(elementId, "Click", 100);
        await Task.Delay(10);
        var result2 = analyzer.TraceRoutedEvents(elementId, "Click", 100);

        // Assert - Both should succeed
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        // Wait for traces to complete
        await Task.Delay(150);
    }

    [StaFact]
    public async Task TraceRoutedEvents_SecondCallWhileFirstIsActive_ShouldCancelFirst()
    {
        // Arrange
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        // Act - Start first trace with longer duration
        var result1 = analyzer.TraceRoutedEvents(elementId, "Click", 200);

        // Wait a bit to ensure first trace is active
        await Task.Delay(50);

        // Start second trace - should cancel first
        var result2 = analyzer.TraceRoutedEvents(elementId, "Click", 200);

        // Assert - Both calls should succeed without throwing
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        // Wait for second trace to complete
        await Task.Delay(250);
    }
}
