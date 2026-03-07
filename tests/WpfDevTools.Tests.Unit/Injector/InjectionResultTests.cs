using Xunit;
using FluentAssertions;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Injector;

public class InjectionResultTests
{
    [Fact]
    public void CreateSuccess_ShouldReturnSuccessResult()
    {
        // Act
        var result = InjectionResult.CreateSuccess(1234, @"C:\path\to\inspector.dll");

        // Assert
        result.Success.Should().BeTrue();
        result.ProcessId.Should().Be(1234);
        result.DllPath.Should().Be(@"C:\path\to\inspector.dll");
    }

    [Fact]
    public void CreateSuccess_ShouldSetErrorToNone()
    {
        // Act
        var result = InjectionResult.CreateSuccess(42, "inspector.dll");

        // Assert
        result.Error.Should().Be(InjectionError.None);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void CreateFailure_ShouldReturnFailureResult()
    {
        // Act
        var result = InjectionResult.CreateFailure(
            5678,
            InjectionError.ProcessNotFound,
            "Process 5678 was not found");

        // Assert
        result.Success.Should().BeFalse();
        result.ProcessId.Should().Be(5678);
        result.Error.Should().Be(InjectionError.ProcessNotFound);
        result.ErrorMessage.Should().Be("Process 5678 was not found");
    }

    [Fact]
    public void CreateFailure_WithAccessDenied_ShouldSetCorrectError()
    {
        // Act
        var result = InjectionResult.CreateFailure(100, InjectionError.AccessDenied, "Access denied");

        // Assert
        result.Error.Should().Be(InjectionError.AccessDenied);
        result.DllPath.Should().BeNull();
    }

    [Fact]
    public void CreateFailure_WithArchitectureMismatch_ShouldSetCorrectError()
    {
        // Act
        var result = InjectionResult.CreateFailure(200, InjectionError.ArchitectureMismatch, "x86/x64 mismatch");

        // Assert
        result.Error.Should().Be(InjectionError.ArchitectureMismatch);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void CreateFailure_WithTimeout_ShouldSetCorrectError()
    {
        // Act
        var result = InjectionResult.CreateFailure(300, InjectionError.Timeout, "Injection timed out");

        // Assert
        result.Error.Should().Be(InjectionError.Timeout);
        result.ErrorMessage.Should().Be("Injection timed out");
    }

    [Fact]
    public void DirectConstruction_WithRequiredProperties_ShouldWork()
    {
        // Act
        var result = new InjectionResult
        {
            Success = true,
            ProcessId = 999,
            DllPath = "test.dll",
            Error = InjectionError.None
        };

        // Assert
        result.Success.Should().BeTrue();
        result.ProcessId.Should().Be(999);
        result.DllPath.Should().Be("test.dll");
        result.Error.Should().Be(InjectionError.None);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void CreateSuccess_WithBootstrapInfo_ShouldPopulateDiagnostics()
    {
        var result = InjectionResult.CreateSuccess(1234, "test.dll",
            bootstrapExitCode: 0, pipeName: "WpfDevTools_1234");

        result.Success.Should().BeTrue();
        result.BootstrapExitCode.Should().Be(0);
        result.PipeName.Should().Be("WpfDevTools_1234");
        result.FailedAtStage.Should().BeNull();
    }

    [Fact]
    public void CreateFailure_WithStageInfo_ShouldPopulateDiagnostics()
    {
        var result = InjectionResult.CreateFailure(1234,
            InjectionError.BootstrapFailed,
            "CLR hosting failed",
            failedAtStage: BootstrapStage.ClrHosting,
            bootstrapExitCode: 0x11);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(InjectionError.BootstrapFailed);
        result.FailedAtStage.Should().Be(BootstrapStage.ClrHosting);
        result.BootstrapExitCode.Should().Be(0x11);
    }

    [Theory]
    [InlineData(InjectionError.AllocationFailed, "Memory allocation failed")]
    [InlineData(InjectionError.WriteFailed, "Write to process failed")]
    [InlineData(InjectionError.CreateThreadFailed, "Remote thread creation failed")]
    [InlineData(InjectionError.SingleFileApplication, "Cannot inject into single-file app")]
    [InlineData(InjectionError.NotWpfApplication, "Target is not a WPF application")]
    [InlineData(InjectionError.Unknown, "An unknown error occurred")]
    public void CreateFailure_WithVariousErrorTypes_ShouldPreserveError(
        InjectionError error, string message)
    {
        // Act
        var result = InjectionResult.CreateFailure(1, error, message);

        // Assert
        result.Error.Should().Be(error);
        result.ErrorMessage.Should().Be(message);
        result.Success.Should().BeFalse();
    }
}
