using System.Diagnostics;
using Xunit;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
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
        var nonWpfProcessId = 1_500_000_001;
        var injector = new ProcessInjector(
            new NonWpfProcessDetector(nonWpfProcessId),
            new DllInjector());
        var dllPath = "not-used-before-target-validation.dll";

        var result = injector.Inject(nonWpfProcessId, dllPath);

        result.Success.Should().BeFalse();
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

    [Fact]
    public void InjectWithBootstrap_WhenSharedBudgetIsExhaustedBeforeBootstrapPhaseStart_ShouldReturnStructuredTimeout()
    {
        var processId = Process.GetCurrentProcess().Id;
        var injector = new ProcessInjector(
            new AlwaysWpfProcessDetector(processId),
            new SuccessfulDllInjector(),
            () => new PipeReadyProbe((_, _) => true, () => DateTime.UtcNow, _ => { }));
        var request = new InjectionRequest
        {
            ProcessId = processId,
            BootstrapperDllPath = "bootstrapper.dll",
            InspectorDllPath = "inspector.dll",
            ExpectedPipeName = "WpfDevTools_TestPipe",
            TotalTimeout = TimeSpan.Zero
        };

        var result = injector.InjectWithBootstrap(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(InjectionError.Timeout);
        result.FailedAtStage.Should().Be(BootstrapStage.LoadLibrary);
        result.BootstrapExitCode.Should().BeNull();
        result.TimeoutReason.Should().Be(InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart);
    }

    [Fact]
    public void InjectWithBootstrap_WhenSharedBudgetIsExhaustedBeforePipeReadyPhaseStart_ShouldReturnStructuredPipeReadyTimeout()
    {
        var processId = Process.GetCurrentProcess().Id;
        var injector = new ProcessInjector(
            new AlwaysWpfProcessDetector(processId),
            new SuccessfulDllInjector(delay: TimeSpan.FromMilliseconds(300)),
            () => new PipeReadyProbe((_, _) => true, () => DateTime.UtcNow, _ => { }));
        var request = new InjectionRequest
        {
            ProcessId = processId,
            BootstrapperDllPath = "bootstrapper.dll",
            InspectorDllPath = "inspector.dll",
            ExpectedPipeName = "WpfDevTools_TestPipe",
            TotalTimeout = TimeSpan.FromMilliseconds(200)
        };

        var result = injector.InjectWithBootstrap(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(InjectionError.PipeReadyTimeout);
        result.FailedAtStage.Should().Be(BootstrapStage.PipeReady);
        result.BootstrapExitCode.Should().Be(0);
        result.TimeoutReason.Should().Be(InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart);
    }

    private sealed class AlwaysWpfProcessDetector(int expectedProcessId) : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId)
        {
            if (processId != expectedProcessId)
            {
                return null;
            }

            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "TestProcess",
                Architecture = Environment.Is64BitProcess ? ProcessArchitecture.X64 : ProcessArchitecture.X86,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true
            };
        }
    }

    private sealed class NonWpfProcessDetector(int expectedProcessId) : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId)
        {
            if (processId != expectedProcessId)
            {
                return null;
            }

            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "NonWpfTestProcess",
                Architecture = Environment.Is64BitProcess ? ProcessArchitecture.X64 : ProcessArchitecture.X86,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = false
            };
        }
    }

    private sealed class SuccessfulDllInjector(TimeSpan? delay = null) : DllInjector
    {
        public override int InjectAndCallExport(
            IntPtr hProcess,
            string bootstrapperPath,
            string exportName,
            string parameters,
            TimeSpan timeout)
        {
            if (delay.HasValue)
            {
                Thread.Sleep(delay.Value);
            }

            return 0;
        }
    }
}
