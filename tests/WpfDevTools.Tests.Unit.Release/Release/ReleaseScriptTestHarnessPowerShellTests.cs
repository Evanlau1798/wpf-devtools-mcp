using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseScriptTestHarnessPowerShellTests
{
    [Fact]
    public void RunPowerShellScript_ShouldIgnoreStaleNativeLastExitCodeForOnlineInstallerAfterSuccessfulScriptReturn()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptPath = Path.Combine(tempRoot, "online-installer.ps1");
            File.WriteAllText(
                scriptPath,
                """
                param([string]$WorkingRoot)
                cmd.exe /c exit 128
                Write-Output 'script completed'
                """);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(scriptPath, []);

            result.ExitCode.Should().Be(0, result.Stderr + result.Stdout);
            result.Stdout.Should().Contain("script completed");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
