using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
    public void Enqueue_WithLongPayloadStrings_ShouldBoundValuesAndTrackTruncationMetadata()
    {
        var buffer = new WatchEventBuffer(capacity: 4, new WatchEventDeduplicator());
        var longValue = new string('x', 4096);

        buffer.Enqueue(CreateDpChange(longValue, "TextBox_1", "Text", longValue));

        var record = buffer.GetSnapshot().Should().ContainSingle().Subject;
        record.SourceKey.Length.Should().BeLessThanOrEqualTo(WatchEventBuffer.MaxPayloadStringLength);
        record.NewValue!.Length.Should().BeLessThanOrEqualTo(WatchEventBuffer.MaxPayloadStringLength);
        record.PayloadTruncated.Should().BeTrue();
        record.TruncationMetadata.Should().NotBeNull();
        record.TruncationMetadata!.Reasons.Should().Contain("PayloadStringLength");
        record.TruncationMetadata.OriginalStringLengths["sourceKey"].Should().Be(4096);
        record.SourceKey.Should().EndWith("#" + ComputeHashPrefix(longValue));
        record.NewValue.Should().EndWith("#" + ComputeHashPrefix(longValue));
        GetDedupIndexCount(buffer).Should().BeLessThanOrEqualTo(buffer.PendingCount);
    }

    [Fact]
    public void Source_ShouldHashTruncatedPayloadStringsWithoutMaterializingFullUtf8Array()
    {
        var source = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Inspector/Events/WatchEventBuffer.cs"));
        var computeHashStart = source.IndexOf(
            "private string ComputeHashPrefix(string value)",
            StringComparison.Ordinal);
        var disposeStart = source.IndexOf(
            "public void Dispose()",
            computeHashStart,
            StringComparison.Ordinal);
        var computeHashBody = source[computeHashStart..disposeStart];

        computeHashBody.Should().NotContain("Encoding.UTF8.GetBytes(value)",
            "oversized payload hashing should not allocate a byte[] for the full original string");
        computeHashBody.Should().NotContain(".AsSpan(",
            "the inspector targets net48, so hash streaming should avoid span-only Encoder.Convert overloads");
        computeHashBody.Should().Contain("_hashCharBuffer",
            "hashing should stream bounded char chunks through a net48-compatible reusable char buffer");
        computeHashBody.Should().Contain("Encoder.Convert",
            "hashing should stream bounded char chunks through a reusable encoder buffer");
    }

    [Fact]
    public void EnqueueAndDrain_WithManyUniqueDpChanges_ShouldNotLeaveUnboundedDedupIndex()
    {
        var buffer = new WatchEventBuffer(capacity: 8, new WatchEventDeduplicator());

        for (var i = 0; i < 100; i++)
        {
            buffer.Enqueue(CreateDpChange($"dp:TextBox_{i}:Text", $"TextBox_{i}", "Text", i.ToString()));
        }

        GetDedupIndexCount(buffer).Should().BeLessThanOrEqualTo(8);

        buffer.Drain(maxEvents: 8);

        GetDedupIndexCount(buffer).Should().Be(0);
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

    private static int GetDedupIndexCount(WatchEventBuffer buffer)
    {
        var field = typeof(WatchEventBuffer).GetField(
            "_dedupIndex",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var index = (System.Collections.IDictionary)field!.GetValue(buffer)!;
        return index.Count;
    }

    private static string ComputeHashPrefix(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..8];
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
