using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class TraceEventRingBufferTests
{
    [Fact]
    public void GetSnapshot_WhenCapacityExceeded_ShouldKeepMostRecentEventsInOrder()
    {
        var buffer = new TraceEventRingBuffer(3);

        buffer.Add("one");
        buffer.Add("two");
        buffer.Add("three");
        buffer.Add("four");
        buffer.Add("five");

        buffer.Count.Should().Be(3);
        buffer.GetSnapshot().Should().Equal("three", "four", "five");
    }

    [Fact]
    public void Clear_ShouldResetCountAndAllowReuse()
    {
        var buffer = new TraceEventRingBuffer(2);

        buffer.Add("one");
        buffer.Add("two");

        buffer.Clear();
        buffer.Add("three");

        buffer.Count.Should().Be(1);
        buffer.GetSnapshot().Should().Equal("three");
    }
}