using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class GitHubPagesInstallerScriptTests
{
    [Fact]
    public void StandaloneOnlineInstaller_ShouldKeepVerifiedArchiveLockedThroughHelperExtraction()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", isolateArchiveContents: true);
            var maliciousArchivePath = Path.Combine(tempRoot, "malicious-replacement.zip");
            var maliciousRoot = Path.Combine(tempRoot, "malicious-replacement");
            ZipFile.ExtractToDirectory(archivePath, maliciousRoot);

            var markerPath = Path.Combine(tempRoot, "replacement-helper.marker");
            var helperPath = Path.Combine(maliciousRoot, "bin", "installer", "Installer.Actions.ps1");
            File.AppendAllText(
                helperPath,
                Environment.NewLine +
                "Set-Content -LiteralPath $env:WPFDEVTOOLS_REPLACEMENT_HELPER_MARKER -Value 'executed' -Encoding UTF8" +
                Environment.NewLine);
            UpdateInstallerHelperManifestRecord(
                Path.Combine(maliciousRoot, "bin", "installer", "installer-helpers.manifest.json"),
                "Installer.Actions.ps1",
                helperPath);
            ZipFile.CreateFromDirectory(maliciousRoot, maliciousArchivePath);

            var scriptPath = Path.Combine(tempRoot, "online-installer.ps1");
            var workingRoot = Path.Combine(tempRoot, "working");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                scriptPath,
                overwrite: true);

            var command = $$"""
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Version 1.2.3 -Architecture x64 -Client other -PackageArchivePath '" + archivePath.Replace("'", "''") + "' -WorkingRoot '" + workingRoot.Replace("'", "''") + "' -NonInteractive", scriptPath)}}
$originalCopyHelperBundle = ${function:Copy-InstallerHelperBundleFromArchive}
function Copy-InstallerHelperBundleFromArchive {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$DestinationRoot,
        [Parameter(Mandatory)] [string[]]$HelperFiles
    )

    try {
        Copy-Item -LiteralPath $env:WPFDEVTOOLS_MALICIOUS_REPLACEMENT_ARCHIVE -Destination $ArchivePath -Force -ErrorAction Stop
        $script:ReplacementSucceeded = $true
    }
    catch {
        $script:ReplacementSucceeded = $false
    }

    & $originalCopyHelperBundle @PSBoundParameters
}

$null = Ensure-TuiHelpersAvailable
foreach ($helperPath in @(Get-InstallerSharedModulePaths)) {
    . $helperPath
}

[ordered]@{
    replacementSucceeded = [bool]$script:ReplacementSucceeded
    markerExists = Test-Path -LiteralPath $env:WPFDEVTOOLS_REPLACEMENT_HELPER_MARKER
} | ConvertTo-Json -Compress
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_MALICIOUS_REPLACEMENT_ARCHIVE"] = maliciousArchivePath,
                    ["WPFDEVTOOLS_REPLACEMENT_HELPER_MARKER"] = markerPath
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var payload = JsonDocument.Parse(result.Stdout);
            payload.RootElement.GetProperty("replacementSucceeded").GetBoolean().Should().BeFalse();
            payload.RootElement.GetProperty("markerExists").GetBoolean().Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
