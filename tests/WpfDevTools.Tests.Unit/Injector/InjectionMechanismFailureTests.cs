using FluentAssertions;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class InjectionMechanismFailureTests
{
    [Fact]
    public void TryInterpret_WhenExitCodeIsManagedBridgeMinusOne_ShouldReturnFalse()
    {
        var interpreted = InjectionMechanismFailure.TryInterpret(-1, out var result);

        interpreted.Should().BeFalse(
            "-1 is a valid managed bootstrap failure code from BootstrapBridge and must not collide with injector transport failures");
        result.Should().BeNull();
    }

    [Fact]
    public void TryInterpret_WhenLoadLibraryFails_ShouldReturnLoadLibraryStage()
    {
        var interpreted = InjectionMechanismFailure.TryInterpret(
            InjectionMechanismFailure.LoadBootstrapperFailed,
            out var result);

        interpreted.Should().BeTrue();
        result!.Stage.Should().Be(BootstrapStage.LoadLibrary);
        result.Message.Should().Contain("LoadLibraryW");
    }

    [Fact]
    public void TryInterpret_WhenExportInvocationTimesOut_ShouldReturnManagedEntrypointStage()
    {
        var interpreted = InjectionMechanismFailure.TryInterpret(
            InjectionMechanismFailure.InvokeBootstrapExportTimedOut,
            out var result);

        interpreted.Should().BeTrue();
        result!.Stage.Should().Be(BootstrapStage.ManagedEntrypoint);
        result.Message.Should().Contain("timeout");
    }

    [Fact]
    public void TryInterpret_WhenBootstrapExitCodeCannotBeRead_ShouldReturnManagedEntrypointStage()
    {
        var interpreted = InjectionMechanismFailure.TryInterpret(
            InjectionMechanismFailure.ReadBootstrapExitCodeFailed,
            out var result);

        interpreted.Should().BeTrue();
        result!.Stage.Should().Be(BootstrapStage.ManagedEntrypoint);
        result.Message.Should().Contain("exit code");
    }
}
