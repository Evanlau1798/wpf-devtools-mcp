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
        result.Error.Should().Be(InjectionError.FileNotFound);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.ErrorMessage.Should().Contain("not found");
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

    [Fact]
    public void ValidateInjection_WithMissingDllPath_ShouldReturnFileNotFound()
    {
        // Arrange
        var injector = new DllInjector();
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var missingDll = "C:\\NonExistent\\missing.dll";

        // Act
        var error = injector.ValidateInjection(currentProcessId, missingDll);

        // Assert
        error.Should().Be(InjectionError.FileNotFound);
    }

    [Fact]
    public void GetRemainingBootstrapPhaseTimeout_WhenElapsedIsTwoSeconds_ShouldReturnRemainingBudget()
    {
        var remaining = DllInjector.GetRemainingBootstrapPhaseTimeout(
            elapsed: TimeSpan.FromSeconds(2),
            totalTimeout: TimeSpan.FromSeconds(5));

        remaining.Should().Be(TimeSpan.FromSeconds(3),
            "LoadLibrary and bootstrap export invocation should share a single timeout budget");
    }

    [Fact]
    public void GetRemainingBootstrapPhaseTimeout_WhenElapsedConsumesBudget_ShouldReturnZero()
    {
        var remaining = DllInjector.GetRemainingBootstrapPhaseTimeout(
            elapsed: TimeSpan.FromSeconds(5),
            totalTimeout: TimeSpan.FromSeconds(5));

        remaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void InterpretInjectionMechanismFailure_WhenBootstrapExportBudgetIsExhausted_ShouldMapToTimeoutReason()
    {
        var interpreted = InjectionMechanismFailure.TryInterpret(
            InjectionMechanismFailure.InvokeBootstrapExportBudgetExhausted,
            out var result);

        interpreted.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Error.Should().Be(InjectionError.Timeout);
        result.Stage.Should().Be(BootstrapStage.ManagedEntrypoint);
        result.TimeoutReason.Should().Be(InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart);
    }

    [Theory]
    [InlineData(InjectionMechanismFailure.LoadBootstrapperTimedOut, null)]
    [InlineData(InjectionMechanismFailure.LoadBootstrapperBudgetExhausted, InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart)]
    public void InterpretInjectionMechanismFailure_WhenLoadLibraryTimeoutsOccur_ShouldMapToTimeout(
        int exitCode,
        InjectionTimeoutReason? expectedReason)
    {
        var interpreted = InjectionMechanismFailure.TryInterpret(exitCode, out var result);

        interpreted.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Error.Should().Be(InjectionError.Timeout);
        result.Stage.Should().Be(BootstrapStage.LoadLibrary);
        result.TimeoutReason.Should().Be(expectedReason);
    }
}
