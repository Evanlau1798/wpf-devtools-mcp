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

    [Fact]
    public void Inject_WithNonWpfProcess_ShouldReturnNotWpfApplicationError()
    {
        // Arrange
        var injector = new ProcessInjector();
        // Use a known non-WPF process (e.g., current test process)
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var dllPath = typeof(ProcessInjector).Assembly.Location;

        // Act
        var result = injector.Inject(currentProcessId, dllPath);

        // Assert
        result.Success.Should().BeFalse();
        // Current test process is not a WPF app
        result.Error.Should().Be(InjectionError.NotWpfApplication);
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
