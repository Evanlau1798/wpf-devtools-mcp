using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class LayoutAnalyzerHighlightCacheTests : IDisposable
{
    public void Dispose()
    {
        LayoutAnalyzer.ClearHighlightsForTests();
    }

    [Fact]
    public void RegisterHighlight_WhenCacheExceedsLimit_ShouldEvictOldestEntries()
    {
        var now = DateTimeOffset.UtcNow;
        var maxHighlights = LayoutAnalyzer.MaxTrackedHighlightsForTests;
        var removedKeys = new List<string>();

        for (var index = 0; index < maxHighlights + 5; index++)
        {
            var key = $"highlight-{index}";
            LayoutAnalyzer.RegisterHighlightForTests(
                key,
                now.AddMilliseconds(index),
                now.AddMinutes(5),
                () => removedKeys.Add(key));
        }

        var keys = LayoutAnalyzer.GetTrackedHighlightKeysForTests();

        LayoutAnalyzer.GetTrackedHighlightCountForTests().Should().Be(maxHighlights);
        keys.Should().NotContain("highlight-0");
        keys.Should().NotContain("highlight-4");
        keys.Should().Contain($"highlight-{maxHighlights + 4}");
        removedKeys.Should().Contain(["highlight-0", "highlight-1", "highlight-2", "highlight-3", "highlight-4"]);
    }

    [Fact]
    public void CleanupHighlights_WhenEntriesExpire_ShouldRemoveExpiredEntries()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredRemoved = false;
        var activeRemoved = false;

        LayoutAnalyzer.TrackHighlightForTests("expired", now.AddMinutes(-2), now.AddMilliseconds(-1), () => expiredRemoved = true);
        LayoutAnalyzer.TrackHighlightForTests("active", now, now.AddMinutes(5), () => activeRemoved = true);

        LayoutAnalyzer.CleanupHighlightsForTests(now);

        var keys = LayoutAnalyzer.GetTrackedHighlightKeysForTests();
        keys.Should().ContainSingle().Which.Should().Be("active");
        expiredRemoved.Should().BeTrue();
        activeRemoved.Should().BeFalse();
    }

    [Fact]
    public void RemoveHighlight_WhenEntryWasReplaced_ShouldNotRemoveNewEntry()
    {
        var now = DateTimeOffset.UtcNow;
        var firstRemovedCount = 0;
        var secondRemovedCount = 0;

        var removeFirst = LayoutAnalyzer.RegisterHighlightForTests(
            "same-element",
            now,
            now.AddMinutes(5),
            () => firstRemovedCount++);
        var removeSecond = LayoutAnalyzer.RegisterHighlightForTests(
            "same-element",
            now.AddMilliseconds(1),
            now.AddMinutes(5),
            () => secondRemovedCount++);

        removeFirst();

        LayoutAnalyzer.GetTrackedHighlightKeysForTests().Should().ContainSingle().Which.Should().Be("same-element");
        firstRemovedCount.Should().Be(1);
        secondRemovedCount.Should().Be(0);

        removeSecond();

        LayoutAnalyzer.GetTrackedHighlightCountForTests().Should().Be(0);
        secondRemovedCount.Should().Be(1);
    }

    [Fact]
    public void GetEffectiveHighlightDuration_WhenDurationExceedsRetention_ShouldCapDuration()
    {
        var duration = LayoutAnalyzer.GetEffectiveHighlightDurationForTests(int.MaxValue);

        duration.Should().Be(TimeSpan.FromMinutes(5));
    }
}
