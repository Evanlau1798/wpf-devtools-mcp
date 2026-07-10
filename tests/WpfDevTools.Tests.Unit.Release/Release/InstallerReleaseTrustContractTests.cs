using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerReleaseTrustContractTests
{
    [Fact]
    public void ResolvePackageSession_OfflineDirectory_ShouldExposeNotApplicableArchiveIntegrity()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDirectory = Path.Combine(tempRoot, "package");
            var manifestPath = Path.Combine(packageDirectory, "manifest.json");
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllText(manifestPath, "{\"version\":\"1.2.3\",\"architecture\":\"x64\"}");

            var command = string.Join(Environment.NewLine,
            [
                ". '" + Escape(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Release.ps1")) + "'",
                "$global:WorkingRoot='" + Escape(Path.Combine(tempRoot, "work")) + "'",
                "function Resolve-AbsoluteDirectory { param([string]$Path) return [IO.Path]::GetFullPath($Path) }",
                "function Test-PackageArchiveRequested { return $false }",
                "function Resolve-LocalPackageRoot { return '" + Escape(packageDirectory) + "' }",
                "function Resolve-PackageManifestPath { param([string]$PackageDirectory) return '" + Escape(manifestPath) + "' }",
                "function Get-LocalPackageTrustedReleaseMetadata { return [ordered]@{ TrustedSignerThumbprint=$null; TrustedSignerSubject=$null; DownloadUri=$null; PackageAssetName=$null } }",
                "Set-StrictMode -Version Latest",
                "$session = Resolve-PackageSession -Mode offline -ResolvedVersion 1.2.3 -ResolvedArchitecture x64",
                "[ordered]@{ Status=[string]$session.ArchiveIntegrity.VerificationStatus; Expected=$session.ArchiveIntegrity.ExpectedSha256; Actual=$session.ArchiveIntegrity.ActualSha256 } | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("Status").GetString().Should().Be("not-applicable");
            json.RootElement.GetProperty("Expected").ValueKind.Should().Be(JsonValueKind.Null);
            json.RootElement.GetProperty("Actual").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InvokeInstallerActionCore_MissingTrustProjection_ShouldFailBeforeMutation()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDirectory = Path.Combine(tempRoot, "package");
            var manifestPath = Path.Combine(packageDirectory, "manifest.json");
            Directory.CreateDirectory(packageDirectory);
            File.WriteAllText(manifestPath, "{\"version\":\"1.2.3\",\"architecture\":\"x64\",\"signaturePolicy\":\"ReleaseChecksumOnly\"}");

            var command = string.Join(Environment.NewLine,
            [
                ". '" + Escape(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Actions.ps1")) + "'",
                "$global:NonInteractive=$true; $global:OutputJson=$true",
                "$script:installCalled=$false; $script:commitCalled=$false",
                "function Resolve-InstallerMode { return 'online' }",
                "function Resolve-PackageSession { return [ordered]@{ PackageDirectory='" + Escape(packageDirectory) + "'; ResolvedVersion='1.2.3'; PackageAssetName='release_1.2.3_win-x64.zip'; DownloadSource='github-release'; DownloadUri='https://example.invalid/release.zip'; CleanupSession=$false; SessionRoot=$null; TrustedArchiveManifestPolicy=$true; TrustedSignerThumbprint=$null; TrustedSignerSubject=$null } }",
                "function Resolve-PackageManifestPath { return '" + Escape(manifestPath) + "' }",
                "function Get-ReleaseAssetIdentity { param([string]$AssetName) return [ordered]@{ AssetName=$AssetName; ResolvedVersion='1.2.3' } }",
                "function Get-ReleaseDownloadUri { return 'https://example.invalid/release.zip' }",
                "function Install-PackagePayload { $script:installCalled=$true; return [ordered]@{ installRoot='" + Escape(Path.Combine(tempRoot, "install")) + "'; installBase='" + Escape(Path.Combine(tempRoot, "install", "x64")) + "'; installedExecutable='" + Escape(Path.Combine(tempRoot, "install", "x64", "tool.exe")) + "'; reusedExistingBinary=$false } }",
                "function Invoke-ClientRegistration { return @([ordered]@{ client='other'; mode='artifact-only'; target='artifact'; applied=$true }) }",
                "function Invoke-InstallVerification { return [ordered]@{ Succeeded=$true; InstalledVersion='1.2.3'; VerificationMessage='ok'; LastVerifiedUtc='2026-07-10T00:00:00Z' } }",
                "function Update-InstalledManifestManagedRegistrationTarget { }",
                "function Get-InstallerState { return [ordered]@{ architectures=[ordered]@{}; registrations=[ordered]@{} } }",
                "function Update-InstallerStateAfterInstall { }",
                "function Save-InstallerState { return 'state.json' }",
                "function Complete-InstalledPayloadCommit { $script:commitCalled=$true }",
                "function Remove-PathIfExists { }",
                "Set-StrictMode -Version Latest",
                "try { Invoke-InstallerActionCore -ResolvedAction install -ResolvedArchitecture x64 -ResolvedClient other -ResolvedInstallRoot '" + Escape(Path.Combine(tempRoot, "install")) + "' -RequestedVersion 1.2.3 | Out-Null } catch { }",
                "[ordered]@{ InstallCalled=$script:installCalled; CommitCalled=$script:commitCalled } | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("InstallCalled").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("CommitCalled").GetBoolean().Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
