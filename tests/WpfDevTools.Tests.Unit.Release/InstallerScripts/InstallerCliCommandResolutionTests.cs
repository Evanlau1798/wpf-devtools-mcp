using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerCliCommandResolutionTests
{
    [DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, nint securityAttributes);

    [Fact]
    public void TestInstallerRunningElevated_ShouldIgnoreAssumeElevatedEnvironmentOverride()
    {
        var command = string.Join(
            Environment.NewLine,
            [
                ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1").Replace("'", "''") + "'",
                "$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()",
                "$principal = [System.Security.Principal.WindowsPrincipal]::new($identity)",
                "$actual = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)",
                "$script:WpfDevToolsInstallerTestModeEnabled = $false",
                "$env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED = if ($actual) { '0' } else { '1' }",
                "$result = Test-InstallerRunningElevated",
                "[ordered]@{ actual = [bool]$actual; result = [bool]$result } | ConvertTo-Json -Compress"
            ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("result").GetBoolean()
            .Should().Be(json.RootElement.GetProperty("actual").GetBoolean());
    }

    [Fact]
    public void InvokeRegistrationCommand_WhenTrustedOverridePathTraversesJunction_ShouldRejectOverride()
    {
        RequireWindowsJunctions();

        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var realDirectory = Path.Combine(tempRoot, "real");
            var junctionDirectory = Path.Combine(tempRoot, "junction");
            Directory.CreateDirectory(realDirectory);
            var realCommandPath = Path.Combine(realDirectory, "codex.cmd");
            File.WriteAllText(realCommandPath, "@echo off" + Environment.NewLine + "exit /b 0");
            CreateDirectoryJunctionOrSkip(junctionDirectory, realDirectory);
            var overridePath = Path.Combine(junctionDirectory, "codex.cmd");

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1").Replace("'", "''") + "'",
                    "function Test-InstallerRunningElevated { return $false }",
                    "$env:WPFDEVTOOLS_CODEX_COMMAND_PATH = '" + overridePath.Replace("'", "''") + "'",
                    "function Get-Command { throw 'PATH lookup should not be used when a trusted absolute path override is configured.' }",
                    "try { Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', 'server.exe') -ClientName 'codex' | Out-Null; throw 'expected failure' } catch { $_.Exception.Message }"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("reparse point");
            result.Stdout.Should().NotContain("expected failure");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeRegistrationCommand_WhenTrustedOverridePathIsHardLink_ShouldRejectOverride()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var realCommandPath = Path.Combine(tempRoot, "codex-real.cmd");
            var hardLinkPath = Path.Combine(tempRoot, "codex.cmd");
            File.WriteAllText(realCommandPath, "@echo off" + Environment.NewLine + "exit /b 0");
            CreateHardLinkOrSkip(hardLinkPath, realCommandPath);

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1").Replace("'", "''") + "'",
                    "function Test-InstallerRunningElevated { return $false }",
                    "$env:WPFDEVTOOLS_CODEX_COMMAND_PATH = '" + hardLinkPath.Replace("'", "''") + "'",
                    "function Get-Command { throw 'PATH lookup should not be used when a trusted absolute path override is configured.' }",
                    "try { Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', 'server.exe') -ClientName 'codex' | Out-Null; throw 'expected failure' } catch { $_.Exception.Message }"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("multiple hard links");
            result.Stdout.Should().NotContain("expected failure");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeRegistrationCommand_WhenTrustedOverridePathIsDriveRelative_ShouldRejectOverride()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var driveRoot = Path.GetPathRoot(tempRoot);
            (driveRoot is { Length: >= 2 } && driveRoot[1] == ':').Should().BeTrue(
                "drive-relative override validation requires a Windows drive-rooted temp directory");

            var commandPath = Path.Combine(tempRoot, "codex.cmd");
            File.WriteAllText(commandPath, "@echo off" + Environment.NewLine + "exit /b 0");
            var driveRelativePath = driveRoot![..2] + "codex.cmd";

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1").Replace("'", "''") + "'",
                    "Set-Location -LiteralPath '" + tempRoot.Replace("'", "''") + "'",
                    "function Test-InstallerRunningElevated { return $false }",
                    "$env:WPFDEVTOOLS_CODEX_COMMAND_PATH = '" + driveRelativePath.Replace("'", "''") + "'",
                    "try { Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', 'server.exe') -ClientName 'codex' | Out-Null; throw 'expected failure' } catch { $_.Exception.Message }"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("fully qualified absolute path");
            result.Stdout.Should().NotContain("expected failure");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeRegistrationCommand_ShouldExecuteResolvedExternalPathInsteadOfCommandName()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var trustedCommandPath = Path.Combine(tempRoot, "trusted", "codex.cmd");
            var commandLogPath = Path.Combine(tempRoot, "codex.log");
            Directory.CreateDirectory(Path.GetDirectoryName(trustedCommandPath)!);
            File.WriteAllText(
                trustedCommandPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "@echo off",
                        "echo %*>>\"" + commandLogPath + "\"",
                        "exit /b 0"
                    ]));

            var command = string.Join(
                    Environment.NewLine,
                    [
                        ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1").Replace("'", "''") + "'",
                    "function Test-InstallerRunningElevated { return $false }",
                    "function Get-Command { param([string]$Name) if ($Name -eq 'codex') { return [pscustomobject]@{ Path='" + trustedCommandPath.Replace("'", "''") + "'; Source='" + trustedCommandPath.Replace("'", "''") + "'; Definition='" + trustedCommandPath.Replace("'", "''") + "'; Name='codex' } } throw 'unexpected command lookup' }",
                    "function codex { throw 'Invoke-RegistrationCommand must not execute the command name after resolution.' }",
                    "$result = Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', 'server.exe') -ClientName 'codex'",
                    "[ordered]@{ target = [string]$result.target; log = [string](Get-Content -LiteralPath '" + commandLogPath.Replace("'", "''") + "' -Raw) } | ConvertTo-Json -Compress"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("target").GetString().Should().Be(trustedCommandPath);
            json.RootElement.GetProperty("log").GetString().Should().Contain("mcp add wpf-devtools -- server.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeOptionalRemovalCommand_ShouldExecuteResolvedExternalPathInsteadOfCommandName()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var trustedCommandPath = Path.Combine(tempRoot, "trusted", "claude.cmd");
            var commandLogPath = Path.Combine(tempRoot, "claude.log");
            Directory.CreateDirectory(Path.GetDirectoryName(trustedCommandPath)!);
            File.WriteAllText(
                trustedCommandPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "@echo off",
                        "echo %*>>\"" + commandLogPath + "\"",
                        "exit /b 0"
                    ]));

            var command = string.Join(
                    Environment.NewLine,
                    [
                        ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1").Replace("'", "''") + "'",
                    "function Test-InstallerRunningElevated { return $false }",
                    "function Get-Command { param([string]$Name) if ($Name -eq 'claude') { return [pscustomobject]@{ Path='" + trustedCommandPath.Replace("'", "''") + "'; Source='" + trustedCommandPath.Replace("'", "''") + "'; Definition='" + trustedCommandPath.Replace("'", "''") + "'; Name='claude' } } throw 'unexpected command lookup' }",
                    "function claude { throw 'Invoke-OptionalRemovalCommand must not execute the command name after resolution.' }",
                    "$result = Invoke-OptionalRemovalCommand -Command 'claude' -Arguments @('mcp', 'remove', 'wpf-devtools') -ClientName 'claude-code'",
                    "[ordered]@{ target = [string]$result.target; applied = [bool]$result.applied; log = [string](Get-Content -LiteralPath '" + commandLogPath.Replace("'", "''") + "' -Raw) } | ConvertTo-Json -Compress"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("target").GetString().Should().Be(trustedCommandPath);
            json.RootElement.GetProperty("applied").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("log").GetString().Should().Contain("mcp remove wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeRegistrationCommand_WhenElevatedAndOnlyPathResolutionAvailable_ShouldFailWithGuidance()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var trustedCommandPath = Path.Combine(tempRoot, "trusted", "codex.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(trustedCommandPath)!);
            File.WriteAllText(trustedCommandPath, "@echo off" + Environment.NewLine + "exit /b 0");

            var command = string.Join(
                    Environment.NewLine,
                    [
                        ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1").Replace("'", "''") + "'",
                    "function Test-InstallerRunningElevated { return $true }",
                    "function Get-Command { param([string]$Name) if ($Name -eq 'codex') { return [pscustomobject]@{ Path='" + trustedCommandPath.Replace("'", "''") + "'; Source='" + trustedCommandPath.Replace("'", "''") + "'; Definition='" + trustedCommandPath.Replace("'", "''") + "'; Name='codex' } } throw 'unexpected command lookup' }",
                    "try { Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', 'server.exe') -ClientName 'codex' | Out-Null; throw 'expected failure' } catch { $_.Exception.Message }"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("WPFDEVTOOLS_SKIP_ELEVATION=1");
            result.Stdout.Should().Contain("PATH is unsafe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeRegistrationCommand_WhenElevatedWithTrustedAbsolutePathOverride_ShouldRejectOverride()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var trustedCommandPath = Path.Combine(tempRoot, "trusted", "codex.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(trustedCommandPath)!);
            File.WriteAllText(trustedCommandPath, "@echo off" + Environment.NewLine + "exit /b 0");

            var command = string.Join(
                    Environment.NewLine,
                    [
                        ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1").Replace("'", "''") + "'",
                    "function Test-InstallerRunningElevated { return $true }",
                    "$env:WPFDEVTOOLS_CODEX_COMMAND_PATH = '" + trustedCommandPath.Replace("'", "''") + "'",
                    "function Get-Command { throw 'PATH lookup should not be used when a trusted absolute path override is configured.' }",
                    "try { Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', 'server.exe') -ClientName 'codex' | Out-Null; throw 'expected failure' } catch { $_.Exception.Message }"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("WPFDEVTOOLS_CODEX_COMMAND_PATH");
            result.Stdout.Should().Contain("elevated");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeRegistrationCommand_WhenElevatedWithOptedInTrustedAbsolutePathOverride_ShouldRejectOverride()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var trustedCommandPath = Path.Combine(tempRoot, "trusted", "codex.cmd");
            var commandLogPath = Path.Combine(tempRoot, "codex.log");
            Directory.CreateDirectory(Path.GetDirectoryName(trustedCommandPath)!);
            File.WriteAllText(
                trustedCommandPath,
                string.Join(
                    Environment.NewLine,
                    [
                        "@echo off",
                        "echo %*>>\"" + commandLogPath + "\"",
                        "exit /b 0"
                    ]));

            var command = string.Join(
                    Environment.NewLine,
                    [
                        ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1").Replace("'", "''") + "'",
                    "function Test-InstallerRunningElevated { return $true }",
                    "$env:WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH = '1'",
                    "$env:WPFDEVTOOLS_CODEX_COMMAND_PATH = '" + trustedCommandPath.Replace("'", "''") + "'",
                    "function Get-Command { throw 'PATH lookup should not be used when a trusted absolute path override is configured.' }",
                    "try { Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', 'server.exe') -ClientName 'codex' | Out-Null; throw 'expected failure' } catch { $_.Exception.Message }"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(commandLogPath).Should().BeFalse("elevated installer must not execute env-provided CLI overrides");
            result.Stdout.Should().Contain("WPFDEVTOOLS_CODEX_COMMAND_PATH");
            result.Stdout.Should().Contain("elevated");
            result.Stdout.Should().NotContain("expected failure");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void RequireWindowsJunctions()
    {
        OperatingSystem.IsWindows().Should().BeTrue(
            "release installer command-resolution tests require a Windows runner with junction support");
    }

    private static void CreateDirectoryJunctionOrSkip(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + junctionPath + "\" \"" + targetPath + "\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();
        process!.WaitForExit(5000).Should().BeTrue("mklink should complete promptly");
        process.ExitCode.Should().Be(0,
            "directory junction creation must be available for release command-resolution verification. stderr/stdout: {0}",
            process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd());
    }

    private static void CreateHardLinkOrSkip(string hardLinkPath, string existingFilePath)
    {
        OperatingSystem.IsWindows().Should().BeTrue(
            "release installer command-resolution tests require a Windows runner with hardlink support");

        try
        {
            CreateHardLink(hardLinkPath, existingFilePath, nint.Zero)
                .Should().BeTrue("hardlink creation must be available; Win32 error {0}", Marshal.GetLastWin32Error());
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException("Hardlink creation is unavailable.", ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new InvalidOperationException("Hardlink creation is unavailable.", ex);
        }
    }
}
