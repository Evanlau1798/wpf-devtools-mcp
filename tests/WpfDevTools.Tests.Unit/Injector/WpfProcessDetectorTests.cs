using Xunit;
using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Injector;

[Collection("ProcessDiscovery")]
public class WpfProcessDetectorTests
{
    [Fact]
    public void GetAllWpfProcesses_ParameterlessOverload_ShouldUseVisibleFilter()
    {
        var detector = new RecordingProcessDetector([]);

        // Act
        var processes = detector.GetAllWpfProcesses();

        // Assert
        detector.RequestedFilter.Should().Be(ProcessWindowFilter.Visible);
        processes.Should().BeEmpty();
    }

    [Fact]
    public void GetAllWpfProcesses_ParameterlessOverload_ShouldReturnOverriddenResult()
    {
        var expected = new[]
        {
            new WpfProcessInfo
            {
                ProcessId = 42,
                ProcessName = "TestApp",
                IsWpfApplication = true,
                Architecture = ProcessArchitecture.X64
            }
        };
        var detector = new RecordingProcessDetector(expected);

        // Act
        var processes = detector.GetAllWpfProcesses();

        // Assert
        processes.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void GetProcessInfo_WithValidWpfProcess_ShouldReturnInfo()
    {
        // Arrange
        var detector = new WpfProcessDetector();
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

        // Act
        var info = detector.GetProcessInfo(currentProcessId);

        // Assert
        info.Should().NotBeNull();
        info!.ProcessId.Should().Be(currentProcessId);
        info.Architecture.Should().NotBe(ProcessArchitecture.Unknown);
    }

    [Fact]
    public void GetProcessInfo_WithInvalidProcessId_ShouldReturnNull()
    {
        // Arrange
        var detector = new WpfProcessDetector();
        var invalidProcessId = 999999;

        // Act
        var info = detector.GetProcessInfo(invalidProcessId);

        // Assert
        info.Should().BeNull();
    }

    [Fact]
    public void DetectArchitecture_ShouldReturnValidArchitecture()
    {
        // Arrange
        var detector = new WpfProcessDetector();
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

        // Act
        var info = detector.GetProcessInfo(currentProcessId);

        // Assert
        if (info != null)
        {
            info.Architecture.Should().BeOneOf(
                ProcessArchitecture.X86,
                ProcessArchitecture.X64,
                ProcessArchitecture.ARM64);
        }
    }

    private sealed class RecordingProcessDetector(IReadOnlyList<WpfProcessInfo> results) : WpfProcessDetector
    {
        internal ProcessWindowFilter? RequestedFilter { get; private set; }

        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
        {
            RequestedFilter = windowFilter;
            return results;
        }
    }
}
