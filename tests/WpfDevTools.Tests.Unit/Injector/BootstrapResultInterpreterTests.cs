using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Shared.Enums;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class BootstrapResultInterpreterTests
{
    [Fact]
    public void Interpret_ExitCodeZero_ShouldReturnSuccess()
    {
        var result = BootstrapResultInterpreter.Interpret(0x00);

        result.Error.Should().Be(InjectionError.None);
        result.Stage.Should().BeNull();
        result.Message.Should().BeNull();
    }

    [Theory]
    [InlineData(0x10, BootstrapStage.ClrDetection, "No CLR found")]
    [InlineData(0x11, BootstrapStage.ClrHosting, "CLR hosting")]
    [InlineData(0x12, BootstrapStage.ManagedEntrypoint, "ExecuteInDefaultAppDomain")]
    [InlineData(0x13, BootstrapStage.ManagedEntrypoint, "hostfxr")]
    [InlineData(0x14, BootstrapStage.LoadLibrary, "Inspector DLL path")]
    public void Interpret_KnownErrorCode_ShouldReturnCorrectStageAndMessage(
        int exitCode, BootstrapStage expectedStage, string messageFragment)
    {
        var result = BootstrapResultInterpreter.Interpret(exitCode);

        result.Error.Should().Be(InjectionError.BootstrapFailed);
        result.Stage.Should().Be(expectedStage);
        result.Message.Should().Contain(messageFragment);
    }

    [Theory]
    [InlineData(0xFF)]
    [InlineData(0x99)]
    public void Interpret_UnknownErrorCode_ShouldReturnBootstrapFailedWithUnknownStage(
        int exitCode)
    {
        var result = BootstrapResultInterpreter.Interpret(exitCode);

        result.Error.Should().Be(InjectionError.BootstrapFailed);
        result.Stage.Should().Be(BootstrapStage.Unknown);
        result.Message.Should().Contain($"0x{exitCode:X}",
            "unknown exit codes should include hex value for debugging");
    }

    [Fact]
    public void Interpret_ManagedBridgeFailure_ShouldIndicateBootstrapFailed()
    {
        // -1 is returned by BootstrapBridge.Run() when managed exception occurs
        var result = BootstrapResultInterpreter.Interpret(-1);

        result.Error.Should().Be(InjectionError.BootstrapFailed);
    }
}
