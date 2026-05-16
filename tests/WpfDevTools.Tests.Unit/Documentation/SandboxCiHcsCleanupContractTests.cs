using FluentAssertions;
using System.Diagnostics;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void StopWindowsSandboxHcs_ShouldKillOnlyExplicitWindowsSandboxComputeSystems()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var fakeHcsDiagPath = Path.Combine(tempRoot, "fake-hcsdiag.ps1");
            var killedPath = Path.Combine(tempRoot, "killed.txt");
            File.WriteAllText(
                fakeHcsDiagPath,
                $$"""
                param([string]$Command, [string]$Id)
                if ($Command -eq 'list') {
                    if (Test-Path '{{EscapePowerShellPath(killedPath)}}') {
                        '22222222-2222-2222-2222-222222222222'
                        '    VM, Running, 22222222-2222-2222-2222-222222222222, CmService'
                        exit 0
                    }

                    '11111111-1111-1111-1111-111111111111'
                    '    VM, Running, 11111111-1111-1111-1111-111111111111, WindowsSandbox'
                    '22222222-2222-2222-2222-222222222222'
                    '    VM, Running, 22222222-2222-2222-2222-222222222222, CmService'
                    exit 0
                }

                if ($Command -eq 'kill') {
                    Add-Content -LiteralPath '{{EscapePowerShellPath(killedPath)}}' -Value $Id -Encoding UTF8
                    exit 0
                }

                exit 64
                """);

            var cleanupScript = Path.Combine(RepoRoot, "scripts", "ci", "Stop-WindowsSandboxHcs.ps1");
            var result = RunPowerShell($"""
            & '{EscapePowerShellPath(cleanupScript)}' `
                -HcsDiagPath '{EscapePowerShellPath(fakeHcsDiagPath)}' `
                -OutputRoot '{EscapePowerShellPath(tempRoot)}' `
                -Confirm:$false
            """);

            result.ExitCode.Should().Be(0, result.Output);
            File.ReadAllLines(killedPath).Should().Equal("11111111-1111-1111-1111-111111111111");
            File.ReadAllText(Path.Combine(tempRoot, "hcsdiag-kill.txt")).Should().Contain("All Windows Sandbox HCS compute systems are closed.");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void StopWindowsSandboxHcs_ShouldSucceedWhenNoComputeSystemsAreListed()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var fakeHcsDiagPath = Path.Combine(tempRoot, "fake-empty-hcsdiag.ps1");
            var killedPath = Path.Combine(tempRoot, "killed.txt");
            File.WriteAllText(
                fakeHcsDiagPath,
                $$"""
                param([string]$Command, [string]$Id)
                if ($Command -eq 'list') {
                    ''
                    exit 0
                }

                if ($Command -eq 'kill') {
                    Add-Content -LiteralPath '{{EscapePowerShellPath(killedPath)}}' -Value $Id -Encoding UTF8
                    exit 0
                }

                exit 64
                """);

            var cleanupScript = Path.Combine(RepoRoot, "scripts", "ci", "Stop-WindowsSandboxHcs.ps1");
            var result = RunPowerShell($"""
            & '{EscapePowerShellPath(cleanupScript)}' `
                -HcsDiagPath '{EscapePowerShellPath(fakeHcsDiagPath)}' `
                -OutputRoot '{EscapePowerShellPath(tempRoot)}' `
                -Confirm:$false
            """);

            result.ExitCode.Should().Be(0, result.Output);
            File.Exists(killedPath).Should().BeFalse();
            File.ReadAllText(Path.Combine(tempRoot, "hcsdiag-kill.txt")).Should().Contain("No matching Windows Sandbox");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void StopWindowsSandboxHcs_ShouldNotWaitForProcessTableWhenNoComputeSystemsAreListed()
    {
        var tempRoot = CreateTempRoot();
        Process? process = null;
        try
        {
            var fakeHcsDiagPath = Path.Combine(tempRoot, "fake-empty-hcsdiag.ps1");
            File.WriteAllText(fakeHcsDiagPath, "param([string]$Command) if ($Command -eq 'list') { exit 0 } exit 64");

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
                -ShutdownTimeoutSeconds 1 `
                -Confirm:$false
            """);

            result.ExitCode.Should().Be(0, result.Output);
            result.Output.Should().NotContain("Windows Sandbox processes did not close");
            File.ReadAllText(Path.Combine(tempRoot, "hcsdiag-kill.txt"))
                .Should().Contain("HcsDiagPath was explicitly provided");
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

    [Fact]
    public void StopWindowsSandboxHcs_WhatIf_ShouldNotKillMatchingComputeSystems()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var fakeHcsDiagPath = Path.Combine(tempRoot, "fake-hcsdiag.ps1");
            var killedPath = Path.Combine(tempRoot, "killed.txt");
            File.WriteAllText(
                fakeHcsDiagPath,
                $$"""
                param([string]$Command, [string]$Id)
                if ($Command -eq 'list') {
                    '11111111-1111-1111-1111-111111111111'
                    '    VM, Running, 11111111-1111-1111-1111-111111111111, WindowsSandbox'
                    exit 0
                }

                if ($Command -eq 'kill') {
                    Add-Content -LiteralPath '{{EscapePowerShellPath(killedPath)}}' -Value $Id -Encoding UTF8
                    exit 0
                }

                exit 64
                """);

            var cleanupScript = Path.Combine(RepoRoot, "scripts", "ci", "Stop-WindowsSandboxHcs.ps1");
            var result = RunPowerShell($"""
            & '{EscapePowerShellPath(cleanupScript)}' `
                -HcsDiagPath '{EscapePowerShellPath(fakeHcsDiagPath)}' `
                -OutputRoot '{EscapePowerShellPath(tempRoot)}' `
                -WhatIf
            """);

            result.ExitCode.Should().Be(0, result.Output);
            File.Exists(killedPath).Should().BeFalse();
            File.ReadAllText(Path.Combine(tempRoot, "hcsdiag-kill.txt")).Should().Contain("Skipped by ShouldProcess");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void StopWindowsSandboxHcs_ShouldRejectPathTraversalLogFileName()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var fakeHcsDiagPath = Path.Combine(tempRoot, "fake-empty-hcsdiag.ps1");
            File.WriteAllText(fakeHcsDiagPath, "param([string]$Command) if ($Command -eq 'list') { exit 0 } exit 64");

            var cleanupScript = Path.Combine(RepoRoot, "scripts", "ci", "Stop-WindowsSandboxHcs.ps1");
            var result = RunPowerShell($"""
            & '{EscapePowerShellPath(cleanupScript)}' `
                -HcsDiagPath '{EscapePowerShellPath(fakeHcsDiagPath)}' `
                -OutputRoot '{EscapePowerShellPath(tempRoot)}' `
                -LogFileName '..\outside.txt' `
                -Confirm:$false
            """);

            result.ExitCode.Should().NotBe(0, result.Output);
            File.Exists(Path.Combine(tempRoot, "..", "outside.txt")).Should().BeFalse();
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void StopWindowsSandboxHcs_ShouldFailWhenKilledSandboxStillExists()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var fakeHcsDiagPath = Path.Combine(tempRoot, "fake-stuck-hcsdiag.ps1");
            File.WriteAllText(
                fakeHcsDiagPath,
                """
                param([string]$Command, [string]$Id)
                if ($Command -eq 'list') {
                    '11111111-1111-1111-1111-111111111111'
                    '    VM, Running, 11111111-1111-1111-1111-111111111111, WindowsSandbox'
                    exit 0
                }

                if ($Command -eq 'kill') {
                    exit 0
                }

                exit 64
                """);

            var cleanupScript = Path.Combine(RepoRoot, "scripts", "ci", "Stop-WindowsSandboxHcs.ps1");
            var result = RunPowerShell($"""
            & '{EscapePowerShellPath(cleanupScript)}' `
                -HcsDiagPath '{EscapePowerShellPath(fakeHcsDiagPath)}' `
                -OutputRoot '{EscapePowerShellPath(tempRoot)}' `
                -ShutdownTimeoutSeconds 1 `
                -Confirm:$false
            """);

            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain("did not close");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void StopWindowsSandboxHcs_ShouldNotKillWhenDetailGuidDoesNotMatchPreviousId()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var fakeHcsDiagPath = Path.Combine(tempRoot, "fake-mismatch-hcsdiag.ps1");
            var killedPath = Path.Combine(tempRoot, "killed.txt");
            File.WriteAllText(
                fakeHcsDiagPath,
                $$"""
                param([string]$Command, [string]$Id)
                if ($Command -eq 'list') {
                    '11111111-1111-1111-1111-111111111111'
                    '    VM, Running, 22222222-2222-2222-2222-222222222222, WindowsSandbox'
                    exit 0
                }

                if ($Command -eq 'kill') {
                    Add-Content -LiteralPath '{{EscapePowerShellPath(killedPath)}}' -Value $Id -Encoding UTF8
                    exit 0
                }

                exit 64
                """);

            var cleanupScript = Path.Combine(RepoRoot, "scripts", "ci", "Stop-WindowsSandboxHcs.ps1");
            var result = RunPowerShell($"""
            & '{EscapePowerShellPath(cleanupScript)}' `
                -HcsDiagPath '{EscapePowerShellPath(fakeHcsDiagPath)}' `
                -OutputRoot '{EscapePowerShellPath(tempRoot)}' `
                -Confirm:$false
            """);

            result.ExitCode.Should().Be(0, result.Output);
            File.Exists(killedPath).Should().BeFalse();
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }
}
