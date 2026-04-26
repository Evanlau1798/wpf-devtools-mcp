using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class InstallerCliCommandResolutionTests
{
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
                    "$env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED = '1'",
                    "function Get-Command { param([string]$Name) if ($Name -eq 'codex') { return [pscustomobject]@{ Path='" + trustedCommandPath.Replace("'", "''") + "'; Source='" + trustedCommandPath.Replace("'", "''") + "'; Definition='" + trustedCommandPath.Replace("'", "''") + "'; Name='codex' } } throw 'unexpected command lookup' }",
                    "try { Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', 'server.exe') -ClientName 'codex' | Out-Null; throw 'expected failure' } catch { $_.Exception.Message }"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("WPFDEVTOOLS_SKIP_ELEVATION=1");
            result.Stdout.Should().Contain("WPFDEVTOOLS_CODEX_COMMAND_PATH");
            result.Stdout.Should().Contain("PATH is unsafe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeRegistrationCommand_WhenElevatedWithTrustedAbsolutePathOverride_ShouldUseOverride()
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
                    "$env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED = '1'",
                    "$env:WPFDEVTOOLS_CODEX_COMMAND_PATH = '" + trustedCommandPath.Replace("'", "''") + "'",
                    "function Get-Command { throw 'PATH lookup should not be used when a trusted absolute path override is configured.' }",
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
}