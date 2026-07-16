using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTransactionalRecoveryTests
{
    [Fact]
    public void InvokeInstallerFullUninstallCore_ShouldKeepCommittedStateWhenRollbackDisposalFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var installBase = Path.Combine(installRoot, "x64");
            var installedExecutable = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");

            Directory.CreateDirectory(Path.GetDirectoryName(installedExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(vscodeConfigPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            File.WriteAllText(installedExecutable, "installed-binary");
            File.WriteAllText(
                vscodeConfigPath,
                "{\"servers\":{\"wpf-devtools\":{\"command\":\"" + installedExecutable.Replace("\\", "\\\\") + "\",\"args\":[]}}}");

            var originalStateJson = JsonSerializer.Serialize(new
            {
                lastInstallRoot = installRoot,
                architectures = new Dictionary<string, object?>
                {
                    ["x64"] = new
                    {
                        version = "1.2.3",
                        executable = installedExecutable,
                        installRoot
                    }
                },
                registrations = new Dictionary<string, object?>
                {
                    ["vscode"] = new
                    {
                        architecture = "x64",
                        installRoot,
                        mode = "json-file",
                        target = vscodeConfigPath,
                        resolvedVersion = "1.2.3",
                        installedExecutable,
                        lastVerifiedUtc = "2026-04-16T00:00:00.0000000Z"
                    }
                }
            });
            File.WriteAllText(statePath, originalStateJson);

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Uninstall.ps1").Replace("'", "''") + "'",
                    "$script:saveCompleted = $false",
                    "$script:disposalCount = 0",
                    "function Resolve-ClientStateKey { param([string]$ClientId, [string]$RegistrationMode) return $ClientId }",
                    "function Resolve-ClientBaseId { param([string]$ClientId) return $ClientId }",
                    "function Assert-InstallerLocalPathTrusted { param([string]$Path) return $Path }",
                    "function Get-TrustedRecordedRegistrationTarget { return '" + vscodeConfigPath.Replace("'", "''") + "' }",
                    "function Resolve-InstallerStatePath { return '" + statePath.Replace("'", "''") + "' }",
                    "function Get-DetectedInstallerRegistrations { param($State) return @([ordered]@{ ClientId='vscode'; RegistrationMode='json-file'; RegistrationTarget='" + vscodeConfigPath.Replace("'", "''") + "'; InstallRoot='" + installRoot.Replace("'", "''") + "'; Architecture='x64'; InstalledExecutable='" + installedExecutable.Replace("'", "''") + "'; InstallerOwned=$true }) }",
                    "function Get-DetectedInstallerInstallations { param($State) return @([ordered]@{ InstallRoot='" + installRoot.Replace("'", "''") + "'; Architecture='x64'; InstallBase='" + installBase.Replace("'", "''") + "'; InstalledExecutable='" + installedExecutable.Replace("'", "''") + "'; InstallerOwned=$true }) }",
                    "function Invoke-ClientUnregistration { param([string]$SelectedClient, $RegistrationRecord) $backupPath='" + Path.Combine(tempRoot, "vscode.mcp.json.rollback").Replace("'", "''") + "'; Copy-Item -LiteralPath '" + vscodeConfigPath.Replace("'", "''") + "' -Destination $backupPath -Force; '{}' | Set-Content -LiteralPath '" + vscodeConfigPath.Replace("'", "''") + "' -Encoding UTF8; return @([ordered]@{ client='vscode'; mode='json-file'; target='" + vscodeConfigPath.Replace("'", "''") + "'; backupPath=$backupPath; applied=$true }) }",
                    "function Invoke-UninstallVerification { param([string]$SelectedClient, $RegistrationRecord) return @{ Succeeded = $true; VerificationMessage = 'ok' } }",
                    "function Resolve-InstallBasePath { param([string]$ResolvedInstallRoot, [string]$ResolvedArchitecture) return '" + installBase.Replace("'", "''") + "' }",
                    "function Move-InstallerPathWithRetry { param([string]$SourcePath, [string]$DestinationPath) Move-Item -LiteralPath $SourcePath -Destination $DestinationPath }",
                    "function Remove-InstallerRuntimeScreenshotCache { return $false }",
                    "function Remove-InstallerOwnedEmptyInstallRoots { return @() }",
                    "function Get-EmptyInstallerState { return [ordered]@{ lastInstallRoot=$null; architectures=[ordered]@{}; registrations=[ordered]@{} } }",
                    "function Save-InstallerState { param($State) $script:saveCompleted = $true; '{\"lastInstallRoot\":null,\"architectures\":{},\"registrations\":{}}' | Set-Content -LiteralPath '" + statePath.Replace("'", "''") + "' -Encoding UTF8; return '" + statePath.Replace("'", "''") + "' }",
                    "function Remove-PathIfExists { param([string]$Path, [switch]$BestEffort) if ([string]::IsNullOrWhiteSpace($Path)) { return }; if ($script:saveCompleted -and $Path -like '*.rollback-*') { $script:disposalCount++; if ($script:disposalCount -eq 2) { if ($BestEffort) { return }; throw 'simulated rollback disposal failure' } }; if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force } }",
                    "$succeeded = $false; $errorMessage=$null; try { Invoke-InstallerFullUninstallCore -State ([ordered]@{ lastInstallRoot='" + installRoot.Replace("'", "''") + "'; architectures=[ordered]@{}; registrations=[ordered]@{} }) | Out-Null; $succeeded = $true } catch { $errorMessage=$_.Exception.Message }",
                    "$rollbackResidue = @(Get-ChildItem -LiteralPath '" + installRoot.Replace("'", "''") + "' -Filter 'x64.rollback-*' -Force -ErrorAction SilentlyContinue).Count -eq 1",
                    "[ordered]@{ Succeeded=$succeeded; Error=$errorMessage; State=[string](Get-Content -LiteralPath '" + statePath.Replace("'", "''") + "' -Raw); Config=[string](Get-Content -LiteralPath '" + vscodeConfigPath.Replace("'", "''") + "' -Raw); InstallExists=(Test-Path -LiteralPath '" + installBase.Replace("'", "''") + "'); ExecutableExists=(Test-Path -LiteralPath '" + installedExecutable.Replace("'", "''") + "'); RollbackResidueExists=$rollbackResidue } | ConvertTo-Json -Compress"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("Succeeded").GetBoolean().Should().BeTrue(
                json.RootElement.GetProperty("Error").GetString());
            json.RootElement.GetProperty("State").GetString().Should().NotContain("\"vscode\"",
                "durable state persistence is the full-uninstall commit point");
            json.RootElement.GetProperty("Config").GetString().Should().NotContain("wpf-devtools");
            json.RootElement.GetProperty("InstallExists").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("ExecutableExists").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("RollbackResidueExists").GetBoolean().Should().BeTrue(
                "failed best-effort disposal may leave safe rollback residue for later cleanup");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeInstallerActionCore_Install_ShouldRollbackReusedBinaryArtifactsWhenStateSaveFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var installBase = Path.Combine(installRoot, "x64");
            var installedExecutable = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            var artifactDir = Path.Combine(installBase, "client-registration");
            var artifactPath = Path.Combine(artifactDir, "vscode.json");
            var rollbackArtifactDir = Path.Combine(tempRoot, "rollback", "client-registration");
            var rollbackArtifactPath = Path.Combine(rollbackArtifactDir, "vscode.json");
            var configPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            var packageDirectory = Path.Combine(tempRoot, "package");
            var packageManifestPath = Path.Combine(packageDirectory, "manifest.json");

            Directory.CreateDirectory(Path.GetDirectoryName(installedExecutable)!);
            Directory.CreateDirectory(artifactDir);
            Directory.CreateDirectory(rollbackArtifactDir);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllText(installedExecutable, "existing-binary");
            File.WriteAllText(artifactPath, "{\"servers\":{\"wpf-devtools\":{\"command\":\"tampered-tool.exe\"}}}");
            File.WriteAllText(rollbackArtifactPath, "{\"servers\":{\"wpf-devtools\":{\"command\":\"existing-tool.exe\"}}}");
            File.WriteAllText(configPath, "{\"servers\":{\"wpf-devtools\":{\"command\":\"existing-tool.exe\"}}}");
            File.WriteAllText(packageManifestPath, "{\"architecture\":\"x64\",\"version\":\"2.0.0\"}");

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Actions.ps1").Replace("'", "''") + "'",
                    "$global:NonInteractive = $true",
                    "$global:OutputJson = $true",
                    "function Resolve-InstallerMode { return 'offline' }",
                    "function Resolve-PackageSession { param([string]$Mode, [string]$ResolvedVersion, [string]$ResolvedArchitecture) return [ordered]@{ PackageDirectory='" + packageDirectory.Replace("'", "''") + "'; ResolvedVersion='2.0.0'; PackageAssetName='release_2.0.0_win-x64.zip'; DownloadSource='local-package'; DownloadUri=''; CleanupSession=$false; SessionRoot='" + Path.Combine(tempRoot, "session").Replace("'", "''") + "'; TrustedArchiveManifestPolicy=$false; TrustedSignerThumbprint=$null; TrustedSignerSubject=$null; ArchiveIntegrity=[ordered]@{ VerificationStatus='not-applicable'; TrustedReleaseMetadataSource=$null; ExpectedSha256=$null; ActualSha256=$null } } }",
                    "function Resolve-PackageManifestPath { param([string]$PackageDirectory) return '" + packageManifestPath.Replace("'", "''") + "' }",
                    "function Get-ReleaseAssetIdentity { param([string]$AssetName) return [ordered]@{ AssetName = $AssetName } }",
                    "function Get-ReleaseDownloadUri { param([string]$ResolvedVersion, [string]$ResolvedArchitecture) return 'https://example.invalid/release.zip' }",
                    "function Get-InstallerState { return [ordered]@{ lastInstallRoot = $null; architectures = [ordered]@{}; registrations = [ordered]@{} } }",
                    "function Resolve-ClientStateKey { param([string]$ClientId, [string]$RegistrationMode) return $ClientId }",
                    "function Install-PackagePayload { param([string]$PackageDirectory, $PackageManifest, [string]$ResolvedArchitecture, [string]$ResolvedInstallRoot, [string]$ResolvedVersion) return [ordered]@{ installRoot='" + installRoot.Replace("'", "''") + "'; installBase='" + installBase.Replace("'", "''") + "'; installedExecutable='" + installedExecutable.Replace("'", "''") + "'; reusedExistingBinary=$true; rollbackBackupRegistrationDir='" + rollbackArtifactDir.Replace("'", "''") + "' } }",
                    "function Invoke-ClientRegistration { param([string]$SelectedClient, [string]$InstalledExecutable, [string]$InstallBase) $backupPath='" + Path.Combine(tempRoot, "config", "Code", "User", "mcp.json.bak").Replace("'", "''") + "'; Copy-Item -LiteralPath '" + configPath.Replace("'", "''") + "' -Destination $backupPath -Force; '{\"servers\":{\"wpf-devtools\":{\"command\":\"new-tool.exe\"}}}' | Set-Content -LiteralPath '" + configPath.Replace("'", "''") + "' -Encoding UTF8; return @([ordered]@{ client='vscode'; mode='json-file'; target='" + configPath.Replace("'", "''") + "'; backupPath=$backupPath; applied=$true }) }",
                    "function Invoke-InstallVerification { param([string]$SelectedClient, [string]$ResolvedVersion, [string]$InstalledExecutable, $Registration) return [ordered]@{ Succeeded = $true; InstalledVersion = '2.0.0'; VerificationMessage = 'ok'; LastVerifiedUtc = '2026-04-16T00:00:00.0000000Z' } }",
                    "function Save-InstallerState { param($State) throw 'simulated state save failure' }",
                    "function Remove-PathIfExists { param([string]$Path) if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path -LiteralPath $Path)) { Remove-Item -LiteralPath $Path -Recurse -Force } }",
                    "try { Invoke-InstallerActionCore -ResolvedAction 'install' -ResolvedArchitecture 'x64' -ResolvedClient 'vscode' -ResolvedInstallRoot '" + installRoot.Replace("'", "''") + "' -RequestedVersion 'latest' | Out-Null } catch { }",
                    "[ordered]@{ Config = [string](Get-Content -LiteralPath '" + configPath.Replace("'", "''") + "' -Raw); Artifact = [string](Get-Content -LiteralPath '" + artifactPath.Replace("'", "''") + "' -Raw); RollbackExists = (Test-Path -LiteralPath '" + rollbackArtifactDir.Replace("'", "''") + "') } | ConvertTo-Json -Compress"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("Config").GetString().Should().Contain("existing-tool.exe");
            json.RootElement.GetProperty("Artifact").GetString().Should().Contain("existing-tool.exe",
                "reused-binary install failures should restore the client-registration artifact bundle before returning control to the user");
            json.RootElement.GetProperty("RollbackExists").GetBoolean().Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeInstallerActionCore_Install_ShouldRestoreStateFileWhenCommitCleanupFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var installBase = Path.Combine(installRoot, "x64");
            var installedExecutable = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            var installManifestPath = Path.Combine(installBase, "install-manifest.json");
            var rollbackManifestPath = installManifestPath + ".rollback-test";
            var artifactDir = Path.Combine(installBase, "client-registration");
            var artifactPath = Path.Combine(artifactDir, "vscode.json");
            var rollbackArtifactDir = Path.Combine(tempRoot, "rollback", "client-registration.rollback-test");
            var rollbackArtifactPath = Path.Combine(rollbackArtifactDir, "vscode.json");
            var configPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            var packageDirectory = Path.Combine(tempRoot, "package");
            var packageManifestPath = Path.Combine(packageDirectory, "manifest.json");

            Directory.CreateDirectory(Path.GetDirectoryName(installedExecutable)!);
            Directory.CreateDirectory(artifactDir);
            Directory.CreateDirectory(rollbackArtifactDir);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllText(installedExecutable, "existing-binary");
            File.WriteAllText(artifactPath, "{\"servers\":{\"wpf-devtools\":{\"command\":\"tampered-tool.exe\"}}}");
            File.WriteAllText(rollbackArtifactPath, "{\"servers\":{\"wpf-devtools\":{\"command\":\"existing-tool.exe\"}}}");
            File.WriteAllText(installManifestPath, "{\"version\":\"2.0.0\",\"executable\":\"new-tool.exe\"}");
            File.WriteAllText(rollbackManifestPath, "{\"version\":\"1.2.3\",\"executable\":\"existing-tool.exe\"}");
            File.WriteAllText(configPath, "{\"servers\":{\"wpf-devtools\":{\"command\":\"existing-tool.exe\"}}}");
            File.WriteAllText(packageManifestPath, "{\"architecture\":\"x64\",\"version\":\"2.0.0\"}");
            File.WriteAllText(
                statePath,
                JsonSerializer.Serialize(new
                {
                    lastInstallRoot = installRoot,
                    architectures = new Dictionary<string, object?>
                    {
                        ["x64"] = new
                        {
                            version = "1.2.3",
                            executable = installedExecutable,
                            installRoot
                        }
                    },
                    registrations = new Dictionary<string, object?>
                    {
                        ["vscode"] = new
                        {
                            architecture = "x64",
                            installRoot,
                            mode = "json-file",
                            target = configPath,
                            resolvedVersion = "1.2.3",
                            installedExecutable,
                            lastVerifiedUtc = "2026-04-16T00:00:00.0000000Z"
                        }
                    }
                }));

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Actions.ps1").Replace("'", "''") + "'",
                    "$global:NonInteractive = $true",
                    "$global:OutputJson = $true",
                    "$script:saveCompleted = $false",
                    "$script:cleanupFailed = $false",
                    "function Resolve-InstallerMode { return 'offline' }",
                    "function Resolve-InstallerStatePath { return '" + statePath.Replace("'", "''") + "' }",
                    "function Resolve-PackageSession { param([string]$Mode, [string]$ResolvedVersion, [string]$ResolvedArchitecture) return [ordered]@{ PackageDirectory='" + packageDirectory.Replace("'", "''") + "'; ResolvedVersion='2.0.0'; PackageAssetName='release_2.0.0_win-x64.zip'; DownloadSource='local-package'; DownloadUri=''; CleanupSession=$false; SessionRoot='" + Path.Combine(tempRoot, "session").Replace("'", "''") + "'; TrustedArchiveManifestPolicy=$false; TrustedSignerThumbprint=$null; TrustedSignerSubject=$null; ArchiveIntegrity=[ordered]@{ VerificationStatus='not-applicable'; TrustedReleaseMetadataSource=$null; ExpectedSha256=$null; ActualSha256=$null } } }",
                    "function Resolve-PackageManifestPath { param([string]$PackageDirectory) return '" + packageManifestPath.Replace("'", "''") + "' }",
                    "function Get-ReleaseAssetIdentity { param([string]$AssetName) return [ordered]@{ AssetName = $AssetName } }",
                    "function Get-ReleaseDownloadUri { param([string]$ResolvedVersion, [string]$ResolvedArchitecture) return 'https://example.invalid/release.zip' }",
                    "function Get-InstallerState { return [ordered]@{ lastInstallRoot='" + installRoot.Replace("'", "''") + "'; architectures=[ordered]@{ x64=[ordered]@{ version='1.2.3'; executable='" + installedExecutable.Replace("'", "''") + "'; installRoot='" + installRoot.Replace("'", "''") + "' } }; registrations=[ordered]@{ vscode=[ordered]@{ architecture='x64'; installRoot='" + installRoot.Replace("'", "''") + "'; mode='json-file'; target='" + configPath.Replace("'", "''") + "'; resolvedVersion='1.2.3'; installedExecutable='" + installedExecutable.Replace("'", "''") + "'; lastVerifiedUtc='2026-04-16T00:00:00.0000000Z' } } } }",
                    "function Resolve-ClientStateKey { param([string]$ClientId, [string]$RegistrationMode) return $ClientId }",
                    "function Install-PackagePayload { param([string]$PackageDirectory, $PackageManifest, [string]$ResolvedArchitecture, [string]$ResolvedInstallRoot, [string]$ResolvedVersion) return [ordered]@{ installRoot='" + installRoot.Replace("'", "''") + "'; installBase='" + installBase.Replace("'", "''") + "'; installManifestPath='" + installManifestPath.Replace("'", "''") + "'; installedExecutable='" + installedExecutable.Replace("'", "''") + "'; reusedExistingBinary=$true; rollbackBackupManifestPath='" + rollbackManifestPath.Replace("'", "''") + "'; rollbackBackupRegistrationDir='" + rollbackArtifactDir.Replace("'", "''") + "' } }",
                    "function Invoke-ClientRegistration { param([string]$SelectedClient, [string]$InstalledExecutable, [string]$InstallBase) $backupPath='" + Path.Combine(tempRoot, "config", "Code", "User", "mcp.json.bak").Replace("'", "''") + "'; Copy-Item -LiteralPath '" + configPath.Replace("'", "''") + "' -Destination $backupPath -Force; '{\"servers\":{\"wpf-devtools\":{\"command\":\"new-tool.exe\"}}}' | Set-Content -LiteralPath '" + configPath.Replace("'", "''") + "' -Encoding UTF8; return @([ordered]@{ client='vscode'; mode='json-file'; target='" + configPath.Replace("'", "''") + "'; backupPath=$backupPath; applied=$true }) }",
                    "function Invoke-InstallVerification { param([string]$SelectedClient, [string]$ResolvedVersion, [string]$InstalledExecutable, $Registration) return [ordered]@{ Succeeded = $true; InstalledVersion = '2.0.0'; VerificationMessage = 'ok'; LastVerifiedUtc = '2026-04-17T00:00:00.0000000Z' } }",
                    "function Save-InstallerState { param($State) $script:saveCompleted = $true; ($State | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath '" + statePath.Replace("'", "''") + "' -Encoding UTF8; return '" + statePath.Replace("'", "''") + "' }",
                    "function Remove-PathIfExists { param([string]$Path) if (-not [string]::IsNullOrWhiteSpace($Path) -and $script:saveCompleted -and -not $script:cleanupFailed -and $Path -like '*.rollback-*') { $script:cleanupFailed = $true; throw 'simulated cleanup failure' }; if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path -LiteralPath $Path)) { Remove-Item -LiteralPath $Path -Recurse -Force } }",
                    "try { Invoke-InstallerActionCore -ResolvedAction 'install' -ResolvedArchitecture 'x64' -ResolvedClient 'vscode' -ResolvedInstallRoot '" + installRoot.Replace("'", "''") + "' -RequestedVersion 'latest' | Out-Null } catch { }",
                    "[ordered]@{ State = [string](Get-Content -LiteralPath '" + statePath.Replace("'", "''") + "' -Raw); Config = [string](Get-Content -LiteralPath '" + configPath.Replace("'", "''") + "' -Raw); Artifact = [string](Get-Content -LiteralPath '" + artifactPath.Replace("'", "''") + "' -Raw); Manifest = [string](Get-Content -LiteralPath '" + installManifestPath.Replace("'", "''") + "' -Raw); RollbackExists = (Test-Path -LiteralPath '" + rollbackArtifactDir.Replace("'", "''") + "') } | ConvertTo-Json -Compress"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("State").GetString().Should().Contain("\"1.2.3\"",
                "the original installer state should be restored if commit cleanup fails after state persistence completed");
            json.RootElement.GetProperty("Config").GetString().Should().Contain("existing-tool.exe");
            json.RootElement.GetProperty("Artifact").GetString().Should().Contain("existing-tool.exe");
            json.RootElement.GetProperty("Manifest").GetString().Should().Contain("\"1.2.3\"");
            json.RootElement.GetProperty("RollbackExists").GetBoolean().Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void CompleteInstalledPayloadCommit_ShouldRemoveReusedBinaryRollbackArtifacts()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var rollbackArtifactDir = Path.Combine(tempRoot, "rollback", "client-registration");
            Directory.CreateDirectory(rollbackArtifactDir);
            File.WriteAllText(Path.Combine(rollbackArtifactDir, "vscode.json"), "existing-tool");

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Actions.ps1").Replace("'", "''") + "'",
                    "function Remove-PathIfExists { param([string]$Path) if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path -LiteralPath $Path)) { Remove-Item -LiteralPath $Path -Recurse -Force } }",
                    "Complete-InstalledPayloadCommit -InstallResult ([ordered]@{ reusedExistingBinary = $true; rollbackBackupRegistrationDir = '" + rollbackArtifactDir.Replace("'", "''") + "' })",
                    "[bool](Test-Path -LiteralPath '" + rollbackArtifactDir.Replace("'", "''") + "')"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be("False");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
