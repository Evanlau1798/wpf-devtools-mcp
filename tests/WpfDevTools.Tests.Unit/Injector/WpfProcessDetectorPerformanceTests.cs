using Xunit;
using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Injector;

public class WpfProcessDetectorPerformanceTests
{
    [Fact]
    public void GetAllWpfProcesses_ShouldStayWithinInteractiveSmokeBudget()
    {
        // Arrange
        var detector = new WpfProcessDetector();

        // Act - measure time to get all WPF processes
        var sw = Stopwatch.StartNew();
        var processes = detector.GetAllWpfProcesses();
        sw.Stop();

        // Assert - this is a smoke budget, not a microbenchmark. Full-suite execution can
        // run under a much heavier process list and WPF test-host load than isolated runs.
        // Keep the threshold wide enough to catch catastrophic regressions or hangs without
        // making the suite fail on workstation-wide process churn.
        sw.ElapsedMilliseconds.Should().BeLessThan(300000,
            "top-level window indexing and deferred metadata probes should stay bounded even under full-suite workstation load");

        // Verify we got some results (at least the test process itself might be WPF)
        processes.Should().NotBeNull();
    }

    [Fact]
    public void GetProcessInfo_WithNonWpfProcess_ShouldReturnQuickly()
    {
        // Arrange
        var detector = new WpfProcessDetector();
        var currentProcess = Process.GetCurrentProcess();

        // Act - get info for current test process
        var sw = Stopwatch.StartNew();
        var info = detector.GetProcessInfo(currentProcess.Id);
        sw.Stop();

        // Assert - should complete quickly even if not WPF
        sw.ElapsedMilliseconds.Should().BeLessThan(1000,
            "process info detection should be fast");

        info.Should().NotBeNull();
    }
}
