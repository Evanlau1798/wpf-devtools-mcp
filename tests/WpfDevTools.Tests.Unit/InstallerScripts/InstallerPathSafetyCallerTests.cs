using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class InstallerPathSafetyCallerTests
{
    [Fact]
    public void HelperArtifactUnregistration_ShouldIgnoreRejectedUncTargetWithoutProbing()
    {
        var registrationScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1");
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(registrationScriptPath) + "'",
            "function Resolve-ClientBaseId { param([string]$ClientId) return $ClientId }",
            "function Test-Path { param([string]$LiteralPath, [string]$Path) $value = if ($PSBoundParameters.ContainsKey('LiteralPath')) { $LiteralPath } else { $Path }; if ($value.StartsWith('\\')) { throw 'untrusted Test-Path probe' }; Microsoft.PowerShell.Management\\Test-Path @PSBoundParameters }",
            "$record = [ordered]@{ mode = 'artifact-only'; target = '\\\\server\\share\\other.mcpServers.json' }",
            "$result = @(Invoke-ClientUnregistration -SelectedClient 'other' -RegistrationRecord $record)",
            "Write-Output ([string]$result[0].applied)",
            "Write-Output ([string]$result[0].backupPath)"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain("False");
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
    }

    [Fact]
    public void StandaloneJsonDiscovery_ShouldIgnoreRejectedUncConfigWithoutProbing()
    {
        var command = string.Join(" ; ",
        [
            OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action uninstall -Architecture x64 -Client other -InstallRoot 'C:\\LocalRoot' -NonInteractive -OutputJson"),
            "function Test-Path { param([string]$LiteralPath, [string]$Path) $value = if ($PSBoundParameters.ContainsKey('LiteralPath')) { $LiteralPath } else { $Path }; if ($value.StartsWith('\\')) { throw 'untrusted Test-Path probe' }; Microsoft.PowerShell.Management\\Test-Path @PSBoundParameters }",
            "$result = Get-StandaloneJsonRegisteredExecutable -CollectionName 'mcpServers' -ConfigPath '\\\\server\\share\\config.json'",
            "if ($null -eq $result) { Write-Output 'null' } else { Write-Output $result }"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().NotStartWith("\\\\server\\share");
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
    }

    [Fact]
    public void HelperLiveManifestEvidence_ShouldIgnoreRejectedUncInstallRootWithoutProbing()
    {
        var stateScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.State.ps1");
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(stateScriptPath) + "'",
            "function Test-Path { param([string]$LiteralPath, [string]$Path) $value = if ($PSBoundParameters.ContainsKey('LiteralPath')) { $LiteralPath } else { $Path }; if ($value.StartsWith('\\')) { throw 'untrusted Test-Path probe' }; Microsoft.PowerShell.Management\\Test-Path @PSBoundParameters }",
            "$result = Get-LiveInstallerManifestEvidence -InstallRoot '\\\\server\\share\\root' -Architecture 'x64'",
            "if ($null -eq $result) { Write-Output 'null' } else { Write-Output 'value' }"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().NotStartWith("\\\\server\\share");
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
    }

    [Fact]
    public void StandaloneTrustedInstallBase_ShouldIgnoreRejectedUncExecutableWithoutProbingFallback()
    {
        var command = string.Join(" ; ",
        [
            OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action uninstall -Architecture x64 -Client other -InstallRoot 'C:\\LocalRoot' -NonInteractive -OutputJson"),
            "function Test-Path { param([string]$LiteralPath, [string]$Path) $value = if ($PSBoundParameters.ContainsKey('LiteralPath')) { $LiteralPath } else { $Path }; if ($value.StartsWith('\\')) { throw 'untrusted Test-Path probe' }; Microsoft.PowerShell.Management\\Test-Path @PSBoundParameters }",
            "$record = [ordered]@{ installedExecutable = '\\\\server\\share\\wpf-devtools-x64.exe'; installRoot = 'C:\\LocalRoot'; architecture = 'x64' }",
            "$result = Resolve-StandaloneTrustedInstallBaseFromRegistrationRecord -RegistrationRecord $record",
            "if ($null -eq $result) { Write-Output 'null' } else { Write-Output $result }"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be("null");
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
    }

    [Fact]
    public void StandaloneOtherVerification_ShouldIgnoreRejectedRecordedArtifactTargetWithoutProbing()
    {
        var command = string.Join(" ; ",
        [
            OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action uninstall -Architecture x64 -Client other -InstallRoot 'C:\\LocalRoot' -NonInteractive -OutputJson"),
            "function Test-Path { param([string]$LiteralPath, [string]$Path) $value = if ($PSBoundParameters.ContainsKey('LiteralPath')) { $LiteralPath } else { $Path }; if ($value.StartsWith('\\')) { throw 'untrusted Test-Path probe' }; Microsoft.PowerShell.Management\\Test-Path @PSBoundParameters }",
            "$record = [ordered]@{ target = '\\\\server\\share\\other.mcpServers.json'; mode = 'artifact-only' }",
            "$result = Invoke-StandaloneUninstallVerification -SelectedClient 'other' -RegistrationRecord $record -RegistrationChanges @()",
            "Write-Output ([string]$result.Succeeded)"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be("True");
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
    }

    [Fact]
    public void HelperOtherVerification_ShouldIgnoreRejectedRecordedArtifactTargetWithoutProbing()
    {
        var registrationScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1");
        var verificationScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Verification.ps1");
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(registrationScriptPath) + "'",
            ". '" + EscapeForPowerShell(verificationScriptPath) + "'",
            "function Resolve-ClientBaseId { param([string]$ClientId) return $ClientId }",
            "function Test-Path { param([string]$LiteralPath, [string]$Path) $value = if ($PSBoundParameters.ContainsKey('LiteralPath')) { $LiteralPath } else { $Path }; if ($value.StartsWith('\\')) { throw 'untrusted Test-Path probe' }; Microsoft.PowerShell.Management\\Test-Path @PSBoundParameters }",
            "$record = [ordered]@{ target = '\\\\server\\share\\other.mcpServers.json'; mode = 'artifact-only' }",
            "$result = Invoke-UninstallVerification -SelectedClient 'other' -RegistrationRecord $record -RegistrationChanges @()",
            "Write-Output ([string]$result.Succeeded)"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be("True");
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
    }

    [Fact]
    public void StandaloneBootstrapUiPath_ShouldIgnoreRejectedUncInstallRootWithoutProbing()
    {
        var command = string.Join(" ; ",
        [
            OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action uninstall -Architecture x64 -Client other -InstallRoot '\\\\server\\share\\root' -NonInteractive -OutputJson"),
            "function Test-Path { param([string]$LiteralPath, [string]$Path) $value = if ($PSBoundParameters.ContainsKey('LiteralPath')) { $LiteralPath } else { $Path }; if ($value.StartsWith('\\')) { throw 'untrusted Test-Path probe' }; Microsoft.PowerShell.Management\\Test-Path @PSBoundParameters }",
            "$result = Resolve-InstallerBootstrapUiPath",
            "if ($null -eq $result) { Write-Output 'null' } else { Write-Output $result }"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().NotStartWith("\\\\server\\share");
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("'", "''");
}