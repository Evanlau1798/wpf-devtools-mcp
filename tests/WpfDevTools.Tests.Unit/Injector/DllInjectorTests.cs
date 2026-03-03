using Xunit;
using FluentAssertions;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Injector;

public class DllInjectorTests
{
    [Fact]
    public void Inject_WithInvalidProcessId_ShouldReturnProcessNotFoundError()
    {
        // Arrange
        var injector = new DllInjector();
        var invalidProcessId = 999999;
        var dllPath = "test.dll";

        // Act
        var result = injector.Inject(invalidProcessId, dllPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be(InjectionError.ProcessNotFound);
    }

    [Fact]
    public void Inject_WithNullDllPath_ShouldThrowArgumentNullException()
    {
        // Arrange
        var injector = new DllInjector();
        var processId = 1;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => injector.Inject(processId, null!));
    }

    [Fact]
    public void Inject_WithNonExistentDllPath_ShouldReturnError()
    {
        // Arrange
        var injector = new DllInjector();
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var nonExistentDll = "C:\\NonExistent\\test.dll";

        // Act
        var result = injector.Inject(currentProcessId, nonExistentDll);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateInjection_WithValidParameters_ShouldReturnNone()
    {
        // Arrange
        var injector = new DllInjector();
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var validDll = typeof(DllInjector).Assembly.Location;

        // Act
        var error = injector.ValidateInjection(currentProcessId, validDll);

        // Assert
        error.Should().Be(InjectionError.None);
    }

    [Fact]
    public void ValidateInjection_WithInvalidProcessId_ShouldReturnProcessNotFound()
    {
        // Arrange
        var injector = new DllInjector();
        var invalidProcessId = 999999;
        var dllPath = "test.dll";

        // Act
        var error = injector.ValidateInjection(invalidProcessId, dllPath);

        // Assert
        error.Should().Be(InjectionError.ProcessNotFound);
    }
}
