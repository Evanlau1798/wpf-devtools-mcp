using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Tests.Unit.Shared;

/// <summary>
/// Tests for InspectorConfig default values.
/// Properties are read-only after static initialization (loaded from env vars or defaults).
/// </summary>
public class InspectorConfigTests
{
    // ── Default value tests ──────────────────────────────────────────────────

    [Fact]
    public void UIThreadTimeout_DefaultValue_ShouldBeFiveSeconds()
    {
        InspectorConfig.UIThreadTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PipeConnectTimeout_DefaultValue_ShouldBeFiveSeconds()
    {
        InspectorConfig.PipeConnectTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RequestTimeout_DefaultValue_ShouldBeThirtySeconds()
    {
        InspectorConfig.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ShutdownTimeout_DefaultValue_ShouldBeFiveSeconds()
    {
        InspectorConfig.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void HeartbeatInterval_DefaultValue_ShouldBeTenSeconds()
    {
        InspectorConfig.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(10));
    }

    // ── Invariant tests ─────────────────────────────────────────────────────

    [Fact]
    public void AllProperties_HavePositiveDefaultDurations()
    {
        InspectorConfig.UIThreadTimeout.Should().BePositive();
        InspectorConfig.PipeConnectTimeout.Should().BePositive();
        InspectorConfig.RequestTimeout.Should().BePositive();
        InspectorConfig.ShutdownTimeout.Should().BePositive();
        InspectorConfig.HeartbeatInterval.Should().BePositive();
    }

    [Fact]
    public void RequestTimeout_ShouldBeLargerThanUIThreadTimeout_ByDefault()
    {
        InspectorConfig.RequestTimeout.Should().BeGreaterThan(InspectorConfig.UIThreadTimeout);
    }

    [Fact]
    public void HeartbeatInterval_ShouldBeLargerThanUIThreadTimeout_ByDefault()
    {
        InspectorConfig.HeartbeatInterval.Should().BeGreaterThan(InspectorConfig.UIThreadTimeout);
    }

    // ── Timeout clamping tests ───────────────────────────────────────────────

    [Fact]
    public void ParseTimeout_ExtremelyLargeValue_ClampedToFiveMinutes()
    {
        var result = InspectorConfig.ParseTimeout("999999999", TimeSpan.FromSeconds(5));
        result.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ParseTimeout_ValidValue_ReturnsExact()
    {
        var result = InspectorConfig.ParseTimeout("3000", TimeSpan.FromSeconds(5));
        result.Should().Be(TimeSpan.FromMilliseconds(3000));
    }

    [Fact]
    public void ParseTimeout_NullOrEmpty_ReturnsDefault()
    {
        var defaultValue = TimeSpan.FromSeconds(5);
        InspectorConfig.ParseTimeout(null, defaultValue).Should().Be(defaultValue);
        InspectorConfig.ParseTimeout("", defaultValue).Should().Be(defaultValue);
        InspectorConfig.ParseTimeout("  ", defaultValue).Should().Be(defaultValue);
    }

    [Fact]
    public void ParseTimeout_NegativeOrZero_ReturnsDefault()
    {
        var defaultValue = TimeSpan.FromSeconds(5);
        InspectorConfig.ParseTimeout("0", defaultValue).Should().Be(defaultValue);
        InspectorConfig.ParseTimeout("-100", defaultValue).Should().Be(defaultValue);
    }
}
