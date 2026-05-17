using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseSignerTrustTests
{
    [Fact]
    public void PackagePayloadIntegrity_ShouldRejectGitHubReleaseSignerWhenOnlyPinComesFromReleaseMetadata()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(
                tempRoot,
                "x64",
                useSignedPayload: true,
                isolateArchiveContents: true);
            ReleaseScriptTestHarness.WriteAdjacentReleaseMetadata(
                archivePath,
                "TESTSIGNER00000000000000000000000000000000",
                "CN=WPFDEVTOOLS TEST SIGNER");

            var extractRoot = Path.Combine(tempRoot, "package-extract");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var command = string.Join(Environment.NewLine, new[]
            {
                "$ErrorActionPreference = 'Stop'",
                "$script:WpfDevToolsInstallerTestModeEnabled = $false",
                $". {QuotePowerShellString(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Release.ps1"))}",
                $". {QuotePowerShellString(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.PackageIntegrity.ps1"))}",
                "function Resolve-InstallerMode { return 'online' }",
                "function Get-PackagePayloadSignature {",
                "    param([string]$Path)",
                "    return [pscustomobject]@{",
                "        Status = [System.Management.Automation.SignatureStatus]::Valid",
                "        SignerCertificate = [pscustomobject]@{",
                "            Thumbprint = 'TESTSIGNER00000000000000000000000000000000'",
                "            Subject = 'CN=WPFDEVTOOLS TEST SIGNER'",
                "        }",
                "    }",
                "}",
                $"$manifest = Get-Content -LiteralPath {QuotePowerShellString(Path.Combine(extractRoot, "bin", "manifest.json"))} -Raw | ConvertFrom-Json",
                $"$integrity = Assert-ArchiveIntegrity -ArchivePath {QuotePowerShellString(archivePath)} -DownloadSource 'github-release' -ResolvedVersion '1.2.3' -ResolvedArchitecture 'x64'",
                "Assert-PackagePayloadIntegrity -PackageDirectory " +
                    $"{QuotePowerShellString(extractRoot)} -PackageManifest $manifest " +
                    "-TrustedSignerThumbprint ([string]$integrity.TrustedSignerThumbprint) " +
                    "-TrustedSignerSubject ([string]$integrity.TrustedSignerSubject) " +
                    "-TrustedArchiveManifestPolicy",
                "'accepted'"
            });

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().NotBe(0, result.Stdout + result.Stderr);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("requires pinned signer metadata");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string QuotePowerShellString(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
