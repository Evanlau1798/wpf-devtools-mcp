using FluentAssertions;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void StopWindowsSandboxHcs_Force_ShouldStopSandboxHostProcessesWhenHcsDiagListFails()
    {
        var tempRoot = CreateTempRoot();
        Process? process = null;
        try
        {
            var fakeHcsDiagPath = Path.Combine(tempRoot, "fake-failing-hcsdiag.ps1");
            File.WriteAllText(fakeHcsDiagPath, "param([string]$Command) if ($Command -eq 'list') { exit 1 } exit 64");

            var systemPowerShell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            var sandboxNamedPowerShell = Path.Combine(tempRoot, "WindowsSandboxServer.exe");
            File.Copy(systemPowerShell, sandboxNamedPowerShell);

            process = Process.Start(new ProcessStartInfo
            {
                FileName = sandboxNamedPowerShell,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 120" }
            });
            process.Should().NotBeNull();

            var cleanupScript = Path.Combine(RepoRoot, "scripts", "ci", "Stop-WindowsSandboxHcs.ps1");
            var result = RunPowerShell($"""
            & '{EscapePowerShellPath(cleanupScript)}' `
                -HcsDiagPath '{EscapePowerShellPath(fakeHcsDiagPath)}' `
                -OutputRoot '{EscapePowerShellPath(tempRoot)}' `
                -ShutdownTimeoutSeconds 5 `
                -SkipProcessTableWait `
                -Force
            """);

            result.ExitCode.Should().Be(0, result.Output);
            process!.Refresh();
            process.HasExited.Should().BeTrue("Force cleanup should close orphaned Windows Sandbox host processes");
            File.ReadAllText(Path.Combine(tempRoot, "hcsdiag-kill.txt")).Should()
                .Contain("hcsdiag list failed")
                .And.Contain("Force-stopping Windows Sandbox host process");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(30000);
            }

            process?.Dispose();
            DeleteTempRoot(tempRoot);
        }
    }
}
