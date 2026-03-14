using System.Collections.Concurrent;
using FluentAssertions;
using WpfDevTools.Inspector.Events;

namespace WpfDevTools.Tests.Unit.Inspector.Events;

public sealed class WatchEventBufferTests
{
    [Fact]
    public void Enqueue_WhenCapacityExceeded_ShouldDropOldestEventAndTrackDropCount()
    {
        var buffer = new WatchEventBuffer(capacity: 2, new WatchEventDeduplicator());

        buffer.Enqueue(CreateRoutedEvent("event:1", "Button_1", "Click"));
        buffer.Enqueue(CreateRoutedEvent("event:2", "Button_2", "Click"));
        buffer.Enqueue(CreateRoutedEvent("event:3", "Button_3", "Click"));

        buffer.PendingCount.Should().Be(2);
        buffer.DroppedCount.Should().Be(1);
        buffer.GetSnapshot().Select(item => item.SourceKey).Should().Equal("event:2", "event:3");
    }

    [Fact]
    public void Enqueue_WhenRepeatedDpChangesUseSameSourceKey_ShouldKeepLatestRecordOnly()
    {
        var buffer = new WatchEventBuffer(capacity: 4, new WatchEventDeduplicator());

        buffer.Enqueue(CreateDpChange("dp:TextBox_1:Width", "TextBox_1", "Width", "100"));
        buffer.Enqueue(CreateDpChange("dp:TextBox_1:Width", "TextBox_1", "Width", "200"));

        var events = buffer.GetSnapshot();
        events.Should().HaveCount(1);
        events[0].NewValue.Should().Be("200");
        events[0].PropertyName.Should().Be("Width");
    }

    [Fact]
    public void Drain_ShouldReturnRequestedEventsInOrderAndRemoveThemFromBuffer()
    {
        var buffer = new WatchEventBuffer(capacity: 4, new WatchEventDeduplicator());

        buffer.Enqueue(CreateRoutedEvent("event:1", "Button_1", "Click"));
        buffer.Enqueue(CreateRoutedEvent("event:2", "Button_2", "Loaded"));
        buffer.Enqueue(CreateRoutedEvent("event:3", "Button_3", "MouseDown"));

        var drained = buffer.Drain(maxEvents: 2);

        drained.Select(item => item.SourceKey).Should().Equal("event:1", "event:2");
        buffer.PendingCount.Should().Be(1);
        buffer.GetSnapshot().Select(item => item.SourceKey).Should().Equal("event:3");
    }

    [Fact]
    public async Task EnqueueAndDrain_WithConcurrentAccess_ShouldRemainThreadSafe()
    {
        var buffer = new WatchEventBuffer(capacity: 2048, new WatchEventDeduplicator());
        const int producerCount = 4;
        const int eventsPerProducer = 200;
        var totalProduced = producerCount * eventsPerProducer;
        var drainedEvents = new ConcurrentBag<WatchEventRecord>();
        var producersCompleted = 0;

        var producers = Enumerable.Range(0, producerCount)
            .Select(index => Task.Run(() =>
            {
                for (var i = 0; i < eventsPerProducer; i++)
                {
                    buffer.Enqueue(CreateRoutedEvent($"event:{index}:{i}", $"Button_{index}_{i}", "Click"));
                }

                Interlocked.Increment(ref producersCompleted);
            }))
            .ToArray();

        var consumer = Task.Run(async () =>
        {
            while (Volatile.Read(ref producersCompleted) < producerCount || buffer.PendingCount > 0)
            {
                foreach (var record in buffer.Drain(maxEvents: 25))
                {
                    drainedEvents.Add(record);
                }

                await Task.Delay(1);
            }
        });

        await Task.WhenAll(producers);
        await consumer;

        drainedEvents.Count.Should().Be(totalProduced);
        buffer.PendingCount.Should().Be(0);
        buffer.DroppedCount.Should().Be(0);
    }

    private static WatchEventRecord CreateDpChange(
        string sourceKey,
        string elementId,
        string propertyName,
        string newValue) =>
        new(
            EventType: "DpChange",
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceKey: sourceKey,
            ElementId: elementId,
            PropertyName: propertyName,
            EventName: null,
            NewValue: newValue,
            ValueType: "Double",
            SenderType: null,
            SenderName: null,
            RoutingStrategy: null,
            Handled: null,
            OriginalSourceType: null);

    private static WatchEventRecord CreateRoutedEvent(
        string sourceKey,
        string elementId,
        string eventName) =>
        new(
            EventType: "RoutedEvent",
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceKey: sourceKey,
            ElementId: elementId,
            PropertyName: null,
            EventName: eventName,
            NewValue: null,
            ValueType: null,
            SenderType: "Button",
            SenderName: "SaveButton",
            RoutingStrategy: "Bubble",
            Handled: false,
            OriginalSourceType: "Button");
}
