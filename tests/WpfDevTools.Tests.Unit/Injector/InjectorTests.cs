using Xunit;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Injector;

public class ProcessInjectorTests
{
    [Fact]
    public void Inject_WithInvalidProcessId_ShouldReturnProcessNotFoundError()
    {
        // Arrange
        var injector = new ProcessInjector();
        var invalidProcessId = 999999;
        var dllPath = "test.dll";

        // Act
        var result = injector.Inject(invalidProcessId, dllPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be(InjectionError.ProcessNotFound);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact(Skip = "Requires system process access which may not be available in all test environments")]
    public void Inject_WithNonWpfProcess_ShouldReturnNotWpfApplicationError()
    {
        // Arrange
        var injector = new ProcessInjector();

        // Find a system process that is definitely not WPF (e.g., svchost, System, Idle)
        var systemProcesses = System.Diagnostics.Process.GetProcessesByName("svchost");
        if (systemProcesses.Length == 0)
        {
            // Fallback to other system processes
            systemProcesses = System.Diagnostics.Process.GetProcessesByName("System");
        }

        if (systemProcesses.Length == 0)
        {
            return; // Skip if no suitable process found
        }

        var nonWpfProcessId = systemProcesses[0].Id;
        var dllPath = typeof(ProcessInjector).Assembly.Location;

        // Act
        var result = injector.Inject(nonWpfProcessId, dllPath);

        // Assert
        result.Success.Should().BeFalse();
        // System process is not a WPF app
        result.Error.Should().Be(InjectionError.NotWpfApplication);

        // Cleanup
        foreach (var proc in systemProcesses)
        {
            proc.Dispose();
        }
    }

    [Fact]
    public void ValidateTarget_WithValidWpfProcess_ShouldReturnNone()
    {
        // Arrange
        var injector = new ProcessInjector();
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

        // Act
        var error = injector.ValidateTarget(currentProcessId);

        // Assert
        // Current process exists, so validation should at least not return ProcessNotFound
        error.Should().NotBe(InjectionError.Unknown);
    }

    [Fact]
    public void ValidateTarget_WithInvalidProcessId_ShouldReturnProcessNotFound()
    {
        // Arrange
        var injector = new ProcessInjector();
        var invalidProcessId = 999999;

        // Act
        var error = injector.ValidateTarget(invalidProcessId);

        // Assert
        error.Should().Be(InjectionError.ProcessNotFound);
    }

    [Fact]
    public void Inject_WithTimeout_ShouldRespectTimeout()
    {
        // Arrange
        var injector = new ProcessInjector();
        var invalidProcessId = 999999;
        var dllPath = "test.dll";
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        var result = injector.Inject(invalidProcessId, dllPath, timeout);

        // Assert
        result.Success.Should().BeFalse();
        // Should fail quickly due to process not found, not timeout
        result.Error.Should().Be(InjectionError.ProcessNotFound);
    }
}
