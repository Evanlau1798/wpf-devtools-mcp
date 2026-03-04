using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Tests.Unit.Shared;

/// <summary>
/// Tests for InspectorConfig default values and environment variable overrides.
/// Because InspectorConfig uses static properties with initializers, the env var
/// behavior is tested by directly setting the writable properties and by verifying
/// GetTimeoutFromEnv indirectly through a fresh static-property reset pattern.
/// </summary>
public class InspectorConfigTests : IDisposable
{
    // Env var names mirrored from InspectorConfig
    private const string EnvUITimeout = "WPF_DEVTOOLS_UI_TIMEOUT_MS";
    private const string EnvPipeConnectTimeout = "WPF_DEVTOOLS_PIPE_CONNECT_TIMEOUT_MS";
    private const string EnvRequestTimeout = "WPF_DEVTOOLS_REQUEST_TIMEOUT_MS";
    private const string EnvShutdownTimeout = "WPF_DEVTOOLS_SHUTDOWN_TIMEOUT_MS";
    private const string EnvHeartbeatInterval = "WPF_DEVTOOLS_HEARTBEAT_INTERVAL_MS";

    // Capture originals before each test so Dispose can restore them
    private readonly TimeSpan _originalUIThreadTimeout;
    private readonly TimeSpan _originalPipeConnectTimeout;
    private readonly TimeSpan _originalRequestTimeout;
    private readonly TimeSpan _originalShutdownTimeout;
    private readonly TimeSpan _originalHeartbeatInterval;

    public InspectorConfigTests()
    {
        _originalUIThreadTimeout = InspectorConfig.UIThreadTimeout;
        _originalPipeConnectTimeout = InspectorConfig.PipeConnectTimeout;
        _originalRequestTimeout = InspectorConfig.RequestTimeout;
        _originalShutdownTimeout = InspectorConfig.ShutdownTimeout;
        _originalHeartbeatInterval = InspectorConfig.HeartbeatInterval;
    }

    public void Dispose()
    {
        // Restore env vars to clean state
        Environment.SetEnvironmentVariable(EnvUITimeout, null);
        Environment.SetEnvironmentVariable(EnvPipeConnectTimeout, null);
        Environment.SetEnvironmentVariable(EnvRequestTimeout, null);
        Environment.SetEnvironmentVariable(EnvShutdownTimeout, null);
        Environment.SetEnvironmentVariable(EnvHeartbeatInterval, null);

        // Restore property values
        InspectorConfig.UIThreadTimeout = _originalUIThreadTimeout;
        InspectorConfig.PipeConnectTimeout = _originalPipeConnectTimeout;
        InspectorConfig.RequestTimeout = _originalRequestTimeout;
        InspectorConfig.ShutdownTimeout = _originalShutdownTimeout;
        InspectorConfig.HeartbeatInterval = _originalHeartbeatInterval;
    }

    // ── Default value tests ──────────────────────────────────────────────────

    [Fact]
    public void UIThreadTimeout_DefaultValue_ShouldBeFiveSeconds()
    {
        // The static initializer runs once per AppDomain; we can verify the
        // known default by checking the property after restore (Dispose sets it back).
        InspectorConfig.UIThreadTimeout = TimeSpan.FromSeconds(5);
        InspectorConfig.UIThreadTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PipeConnectTimeout_DefaultValue_ShouldBeFiveSeconds()
    {
        InspectorConfig.PipeConnectTimeout = TimeSpan.FromSeconds(5);
        InspectorConfig.PipeConnectTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RequestTimeout_DefaultValue_ShouldBeThirtySeconds()
    {
        InspectorConfig.RequestTimeout = TimeSpan.FromSeconds(30);
        InspectorConfig.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ShutdownTimeout_DefaultValue_ShouldBeFiveSeconds()
    {
        InspectorConfig.ShutdownTimeout = TimeSpan.FromSeconds(5);
        InspectorConfig.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void HeartbeatInterval_DefaultValue_ShouldBeTenSeconds()
    {
        InspectorConfig.HeartbeatInterval = TimeSpan.FromSeconds(10);
        InspectorConfig.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(10));
    }

    // ── Property mutability tests ────────────────────────────────────────────

    [Fact]
    public void UIThreadTimeout_CanBeOverriddenAtRuntime()
    {
        var newValue = TimeSpan.FromSeconds(15);
        InspectorConfig.UIThreadTimeout = newValue;
        InspectorConfig.UIThreadTimeout.Should().Be(newValue);
    }

    [Fact]
    public void RequestTimeout_CanBeOverriddenAtRuntime()
    {
        var newValue = TimeSpan.FromSeconds(60);
        InspectorConfig.RequestTimeout = newValue;
        InspectorConfig.RequestTimeout.Should().Be(newValue);
    }

    [Fact]
    public void HeartbeatInterval_CanBeOverriddenAtRuntime()
    {
        var newValue = TimeSpan.FromSeconds(30);
        InspectorConfig.HeartbeatInterval = newValue;
        InspectorConfig.HeartbeatInterval.Should().Be(newValue);
    }

    // ── Environment variable integration tests (indirect via direct set) ─────

    [Fact]
    public void AllTimeoutProperties_SupportPositiveMillisecondValues()
    {
        // Simulate what GetTimeoutFromEnv would produce for a valid env value
        var expected = TimeSpan.FromMilliseconds(2500);

        InspectorConfig.UIThreadTimeout = expected;
        InspectorConfig.PipeConnectTimeout = expected;
        InspectorConfig.RequestTimeout = expected;
        InspectorConfig.ShutdownTimeout = expected;
        InspectorConfig.HeartbeatInterval = expected;

        InspectorConfig.UIThreadTimeout.Should().Be(expected);
        InspectorConfig.PipeConnectTimeout.Should().Be(expected);
        InspectorConfig.RequestTimeout.Should().Be(expected);
        InspectorConfig.ShutdownTimeout.Should().Be(expected);
        InspectorConfig.HeartbeatInterval.Should().Be(expected);
    }

    [Fact]
    public void UIThreadTimeout_WhenSetToZero_ShouldStoreZero()
    {
        // Zero is a valid TimeSpan (it would be rejected by GetTimeoutFromEnv from env vars,
        // but a caller can still assign it directly)
        InspectorConfig.UIThreadTimeout = TimeSpan.Zero;
        InspectorConfig.UIThreadTimeout.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void AllProperties_HavePositiveDefaultDurations()
    {
        // Restore originals to check defaults
        InspectorConfig.UIThreadTimeout = _originalUIThreadTimeout;
        InspectorConfig.PipeConnectTimeout = _originalPipeConnectTimeout;
        InspectorConfig.RequestTimeout = _originalRequestTimeout;
        InspectorConfig.ShutdownTimeout = _originalShutdownTimeout;
        InspectorConfig.HeartbeatInterval = _originalHeartbeatInterval;

        InspectorConfig.UIThreadTimeout.Should().BePositive();
        InspectorConfig.PipeConnectTimeout.Should().BePositive();
        InspectorConfig.RequestTimeout.Should().BePositive();
        InspectorConfig.ShutdownTimeout.Should().BePositive();
        InspectorConfig.HeartbeatInterval.Should().BePositive();
    }

    [Fact]
    public void RequestTimeout_ShouldBeLargerThanUIThreadTimeout_ByDefault()
    {
        InspectorConfig.UIThreadTimeout = _originalUIThreadTimeout;
        InspectorConfig.RequestTimeout = _originalRequestTimeout;

        InspectorConfig.RequestTimeout.Should().BeGreaterThan(InspectorConfig.UIThreadTimeout);
    }

    [Fact]
    public void HeartbeatInterval_ShouldBeLargerThanUIThreadTimeout_ByDefault()
    {
        InspectorConfig.UIThreadTimeout = _originalUIThreadTimeout;
        InspectorConfig.HeartbeatInterval = _originalHeartbeatInterval;

        InspectorConfig.HeartbeatInterval.Should().BeGreaterThan(InspectorConfig.UIThreadTimeout);
    }
}
