using Xunit;
using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Injector;

public class WpfProcessDetectorPerformanceTests
{
    [Fact]
    public void GetAllWpfProcesses_ShouldSkipExpensiveMetadataUntilProcessLooksLikeWpf()
    {
        // Arrange
        var detector = new WpfProcessDetector();

        // Act - measure time to get all WPF processes
        var sw = Stopwatch.StartNew();
        var processes = detector.GetAllWpfProcesses();
        sw.Stop();

        // Assert - should complete quickly by using top-level window indexing
        // and only running expensive metadata probes after a process looks like WPF.
        // Use 10s threshold to account for system load during full test suite execution
        sw.ElapsedMilliseconds.Should().BeLessThan(10000,
            "top-level window indexing and deferred metadata probes should keep WPF detection fast");

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
