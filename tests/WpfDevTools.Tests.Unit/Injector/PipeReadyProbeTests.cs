using FluentAssertions;
using WpfDevTools.Injector;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class PipeReadyProbeTests
{
    [Fact]
    public void WaitForPipeReady_ImmediatelyAvailable_ShouldReturnTrue()
    {
        var probe = new PipeReadyProbe(
            waitNamedPipe: (_, _) => true,
            utcNow: () => DateTime.UtcNow,
            sleep: _ => { });

        var result = probe.WaitForPipeReady(
            "TestPipe", TimeSpan.FromSeconds(5), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public void WaitForPipeReady_NeverAvailable_ShouldReturnFalseAfterTimeout()
    {
        var callCount = 0;
        var startTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var probe = new PipeReadyProbe(
            waitNamedPipe: (_, _) => false,
            utcNow: () => startTime.AddMilliseconds(callCount++ * 200),
            sleep: _ => { });

        var result = probe.WaitForPipeReady(
            "TestPipe", TimeSpan.FromMilliseconds(500), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public void WaitForPipeReady_BecomesAvailableAfterDelay_ShouldReturnTrue()
    {
        var attempts = 0;

        var probe = new PipeReadyProbe(
            waitNamedPipe: (_, _) => ++attempts >= 3,
            utcNow: () => DateTime.UtcNow,
            sleep: _ => { });

        var result = probe.WaitForPipeReady(
            "TestPipe", TimeSpan.FromSeconds(10), CancellationToken.None);

        result.Should().BeTrue();
        attempts.Should().Be(3);
    }

    [Fact]
    public void WaitForPipeReady_Cancelled_ShouldReturnFalse()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var probe = new PipeReadyProbe(
            waitNamedPipe: (_, _) => false,
            utcNow: () => DateTime.UtcNow,
            sleep: _ => { });

        var result = probe.WaitForPipeReady(
            "TestPipe", TimeSpan.FromSeconds(10), cts.Token);

        result.Should().BeFalse();
    }

    [Fact]
    public void WaitForPipeReady_ShouldPassCorrectPipePath()
    {
        string? capturedPipeName = null;

        var probe = new PipeReadyProbe(
            waitNamedPipe: (name, _) => { capturedPipeName = name; return true; },
            utcNow: () => DateTime.UtcNow,
            sleep: _ => { });

        probe.WaitForPipeReady(
            "WpfDevTools_1234", TimeSpan.FromSeconds(1), CancellationToken.None);

        capturedPipeName.Should().Be(@"\\.\pipe\WpfDevTools_1234");
    }

    [Fact]
    public void TryFindReadyPipeByPrefix_WhenRandomizedPipeExists_ShouldReturnRandomizedName()
    {
        var probe = new PipeReadyProbe(
            waitNamedPipe: (name, _) => name.EndsWith("WpfDevTools_1234_abcdef", StringComparison.Ordinal),
            utcNow: () => DateTime.UtcNow,
            sleep: _ => { },
            enumeratePipeNames: () => ["WpfDevTools_1234_abcdef", "WpfDevTools_5678_other"]);

        var result = probe.TryFindReadyPipeByPrefix(
            "WpfDevTools_1234",
            TimeSpan.FromSeconds(1),
            CancellationToken.None,
            out var pipeName);

        result.Should().BeTrue();
        pipeName.Should().Be("WpfDevTools_1234_abcdef");
    }
}
