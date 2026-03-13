using Xunit;
using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Injector;

[Collection("ProcessDiscovery")]
public class WpfProcessDetectorPerformanceTests
{
    [Fact]
    public void GetAllWpfProcesses_ParameterlessOverload_ShouldUseVisibleFilter()
    {
        var detector = new RecordingProcessDetector();

        var result = detector.GetAllWpfProcesses();

        detector.RequestedFilter.Should().Be(ProcessWindowFilter.Visible);
        result.Should().BeEmpty();
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

    private sealed class RecordingProcessDetector : WpfProcessDetector
    {
        internal ProcessWindowFilter? RequestedFilter { get; private set; }

        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
        {
            RequestedFilter = windowFilter;
            return [];
        }
    }
}
