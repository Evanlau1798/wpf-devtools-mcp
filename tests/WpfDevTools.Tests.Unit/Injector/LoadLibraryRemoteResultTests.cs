using System;
using FluentAssertions;
using WpfDevTools.Injector.Injection;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class LoadLibraryRemoteResultTests
{
    [Fact]
    public void IsSuccessful_WhenWaitCompletesAndRemoteModuleHandleIsNonZero_ShouldReturnTrue()
    {
        var result = LoadLibraryRemoteResult.IsSuccessful(
            waitResult: 0x00000000,
            exitCodeAvailable: true,
            remoteModuleHandle: new IntPtr(1));

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void IsSuccessful_WhenExitCodeCannotBeReadOrHandleIsZero_ShouldReturnFalse(
        bool exitCodeAvailable,
        long remoteModuleHandle)
    {
        var result = LoadLibraryRemoteResult.IsSuccessful(
            waitResult: 0x00000000,
            exitCodeAvailable: exitCodeAvailable,
            remoteModuleHandle: new IntPtr(remoteModuleHandle));

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(0x00000102)]
    [InlineData(0xFFFFFFFF)]
    [InlineData(0x00000080)]
    public void IsSuccessful_WhenThreadDidNotSignal_ShouldReturnFalse(uint waitResult)
    {
        var result = LoadLibraryRemoteResult.IsSuccessful(
            waitResult: waitResult,
            exitCodeAvailable: true,
            remoteModuleHandle: new IntPtr(1));

        result.Should().BeFalse();
    }
}
