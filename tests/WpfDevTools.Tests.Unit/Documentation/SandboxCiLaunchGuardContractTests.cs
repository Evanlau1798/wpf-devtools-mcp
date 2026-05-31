using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SandboxCiLaunchGuardContractTests
{
    [Fact]
    public void InvokeWindowsSandboxCi_ShouldFailFastWhenSandboxProcessesAlreadyExist()
    {
        var script = ReadSandboxLauncher();

        script.Should().Contain("Assert-NoActiveWindowsSandboxProcesses");
        script.Should().Contain("WindowsSandboxRemoteSession");
        script.Should().Contain("WindowsSandboxServer");
        script.Should().Contain("vmmemWindowsSandbox");
        script.Should().Contain("Existing Windows Sandbox process");
        script.Should().Contain("Stop-WindowsSandboxHcs.ps1");
    }

    [Fact]
    public void InvokeWindowsSandboxCi_ShouldBoundGuestStartupWaitSeparatelyFromFullRunTimeout()
    {
        var script = ReadSandboxLauncher();

        script.Should().Contain("GuestStartupTimeoutSeconds");
        script.Should().Contain("$startupDeadline");
        script.Should().Contain("did not write RUNNING/PASS/FAIL");
        script.Should().Contain("Inspect the generated .wsb file");
    }

    private static string ReadSandboxLauncher()
        => File.ReadAllText(
            WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(
                "scripts/ci/Invoke-WindowsSandboxCi.ps1"));
}
