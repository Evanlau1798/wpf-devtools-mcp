using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Linq;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class EventAnalyzerOverflowTests
{
    [StaFact]
    public void GetEventTrace_WhenTraceExceedsCapacity_ShouldKeepNewestEventsForActiveAndCompletedSnapshots()
    {
        const int maxTraceEntries = 10000;
        const int overflowEventCount = 5;
        const int totalEventCount = maxTraceEntries + overflowEventCount;
        var expectedSenderNames = Enumerable.Range(overflowEventCount, maxTraceEntries)
            .Select(index => $"Trace{index}")
            .ToArray();

        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button { Name = "Trace0", Content = "Overflow" };
        var elementId = finder.GenerateElementId(button);

        var startOutcome = analyzer.StartTraceRoutedEvents(elementId, "Click", duration: 60000, scheduleAutoStop: false);

        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(startOutcome.Result))
            .GetProperty("success").GetBoolean().Should().BeTrue();
        startOutcome.Session.Should().NotBeNull();

        button.Dispatcher.Invoke(() =>
        {
            for (var index = 0; index < totalEventCount; index++)
            {
                button.Name = $"Trace{index}";
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            }
        });

        var activeTrace = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventTrace(startOutcome.Session)));

        AssertSnapshotWindow(activeTrace, expectedSenderNames);
        activeTrace.GetProperty("handlerInvocationCount").GetInt32().Should().Be(totalEventCount);

        analyzer.CleanupTraceSession(startOutcome.Session!, out var cleanupException).Should().BeTrue();
        cleanupException.Should().BeNull();

        var completedTrace = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventTrace(startOutcome.Session)));
        var publicCompletedTrace = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventTrace()));
        var filteredCompletedTrace = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.GetEventTrace("Click")));

        AssertSnapshotWindow(completedTrace, expectedSenderNames);
        AssertSnapshotWindow(publicCompletedTrace, expectedSenderNames);
        AssertSnapshotWindow(filteredCompletedTrace, expectedSenderNames);
        completedTrace.GetProperty("handlerInvocationCount").GetInt32().Should().Be(totalEventCount);
        publicCompletedTrace.GetProperty("handlerInvocationCount").GetInt32().Should().Be(totalEventCount);
        filteredCompletedTrace.GetProperty("handlerInvocationCount").GetInt32().Should().Be(totalEventCount);
    }

    private static void AssertSnapshotWindow(JsonElement tracePayload, string[] expectedSenderNames)
    {
        tracePayload.GetProperty("eventCount").GetInt32().Should().Be(expectedSenderNames.Length);

        var events = tracePayload.GetProperty("events");
        events.GetArrayLength().Should().Be(expectedSenderNames.Length);
        events.EnumerateArray()
            .Select(@event => @event.GetProperty("senderName").GetString())
            .Should().Equal(expectedSenderNames);
    }
}