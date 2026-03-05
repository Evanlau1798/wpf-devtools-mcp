using Xunit;
using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Injector;

public class WpfProcessDetectorPerformanceTests
{
    [Fact]
    public void GetAllWpfProcesses_ShouldFilterEarlyByMainWindowHandle()
    {
        // Arrange
        var detector = new WpfProcessDetector();

        // Act - measure time to get all WPF processes
        var sw = Stopwatch.StartNew();
        var processes = detector.GetAllWpfProcesses();
        sw.Stop();

        // Assert - should complete quickly by filtering early
        // Most processes don't have MainWindowHandle, so early filtering saves time
        sw.ElapsedMilliseconds.Should().BeLessThan(5000,
            "early filtering by MainWindowHandle should make detection fast");

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
