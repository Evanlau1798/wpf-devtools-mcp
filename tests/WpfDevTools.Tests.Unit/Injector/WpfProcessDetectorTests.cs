using Xunit;
using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Injector;

public class WpfProcessDetectorTests
{
    [Fact]
    public void GetAllWpfProcesses_ShouldReturnProcessList()
    {
        // Arrange
        var detector = new WpfProcessDetector();

        // Act
        var processes = detector.GetAllWpfProcesses();

        // Assert
        processes.Should().NotBeNull();
        // Note: May be empty if no WPF apps running
    }

    [Fact]
    public void GetAllWpfProcesses_ShouldFilterNonWpfProcesses()
    {
        // Arrange
        var detector = new WpfProcessDetector();

        // Act
        var processes = detector.GetAllWpfProcesses();

        // Assert
        // All returned processes should be WPF applications
        foreach (var process in processes)
        {
            process.IsWpfApplication.Should().BeTrue();
        }
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
}
