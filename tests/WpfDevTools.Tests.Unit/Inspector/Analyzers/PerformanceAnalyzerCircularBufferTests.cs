using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using System.Diagnostics;
using System.Reflection;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class PerformanceAnalyzerCircularBufferTests
{
    [Fact]
    public void FrameTimeCollection_ShouldUseCircularBuffer()
    {
        // Arrange - use reflection to access internal frame times collection
        var analyzerType = typeof(PerformanceAnalyzer);
        var frameTimesField = analyzerType.GetField("_frameTimes",
            BindingFlags.NonPublic | BindingFlags.Static);

        frameTimesField.Should().NotBeNull("_frameTimes field should exist");

        // Act & Assert - verify it's not a List (which would have O(n) RemoveAt)
        var frameTimesValue = frameTimesField!.GetValue(null);
        frameTimesValue.Should().NotBeNull();

        // Should NOT be a List<double> (which has O(n) RemoveAt(0))
        frameTimesValue.Should().NotBeOfType<List<double>>(
            "List.RemoveAt(0) causes O(n) performance issue when called 60 times/second");
    }

    [Fact]
    public void FrameTimeCollection_ShouldMaintainFixedCapacity()
    {
        // This test verifies that the frame time storage doesn't grow unbounded
        // and maintains a fixed capacity (60 frames = 1 second at 60 FPS)

        // Arrange
        PerformanceAnalyzer.ResetMonitoring();

        // Act - simulate adding many frame times
        var analyzerType = typeof(PerformanceAnalyzer);
        var onRenderingMethod = analyzerType.GetMethod("OnRendering",
            BindingFlags.NonPublic | BindingFlags.Static);

        onRenderingMethod.Should().NotBeNull();

        // Simulate 120 frames (should only keep last 60)
        for (int i = 0; i < 120; i++)
        {
            onRenderingMethod!.Invoke(null, new object?[] { null, EventArgs.Empty });
        }

        // Assert - verify capacity is maintained
        var frameTimesField = analyzerType.GetField("_frameTimes",
            BindingFlags.NonPublic | BindingFlags.Static);
        var frameTimesValue = frameTimesField!.GetValue(null);

        // Should have exactly 60 samples (MaxFrameSamples)
        if (frameTimesValue is ICollection<double> collection)
        {
            collection.Count.Should().BeLessThanOrEqualTo(60,
                "should maintain fixed capacity of 60 frames");
        }
    }
}
