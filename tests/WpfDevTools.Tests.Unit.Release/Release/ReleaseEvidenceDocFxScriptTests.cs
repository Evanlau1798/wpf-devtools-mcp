using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseEvidenceDocFxScriptTests
{
    [Fact]
    public void WriteReleaseEvidence_ShouldConsumeDocFxEvidenceArtifact()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "release-evidence.json");
            var runtimeEvidencePath = Path.Combine(tempRoot, "runtime-evidence.json");
            var docFxEvidencePath = Path.Combine(tempRoot, "docfx-evidence.json");
            File.WriteAllText(runtimeEvidencePath, """
                {
                  "toolsList": {
                    "count": 64,
                    "nameSetHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "schemaSnapshotHash": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                  },
                  "security": {
                    "mitmMatrixPassed": true,
                    "stdoutPurityPassed": true,
                    "screenshotIntegrityPassed": true
                  },
                  "packageSmoke": {
                    "x64PackageLocal": "passed",
                    "x64OnlineInstaller": "passed",
                    "x86PackageLocal": "passed-or-not-public",
                    "x86OnlineInstaller": "passed-or-not-public",
                    "arm64PackageLocal": "passed-or-not-public",
                    "arm64OnlineInstaller": "passed-or-not-public"
                  },
                  "liveSmoke": {
                    "connect": true,
                    "ping": true,
                    "getUiSummary": true,
                    "safeRead": true,
                    "mutationRestore": true,
                    "uninstallResidue": true
                  }
                }
                """);
            File.WriteAllText(docFxEvidencePath, """
                {
                  "englishParity": false,
                  "zhTwParity": true,
                  "brokenLinks": 2
                }
                """);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseEvidence.ps1"),
                BuildArguments(tempRoot, outputPath, runtimeEvidencePath, docFxEvidencePath));

            result.ExitCode.Should().Be(0, result.Stderr);
            using var evidence = JsonDocument.Parse(File.ReadAllText(outputPath));
            var docfx = evidence.RootElement.GetProperty("docfx");
            docfx.GetProperty("englishParity").GetBoolean().Should().BeFalse();
            docfx.GetProperty("zhTwParity").GetBoolean().Should().BeTrue();
            docfx.GetProperty("brokenLinks").GetInt32().Should().Be(2);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string[] BuildArguments(
        string tempRoot,
        string outputPath,
        string runtimeEvidencePath,
        string docFxEvidencePath)
    {
        var shaSumsPath = Path.Combine(tempRoot, "SHA256SUMS.txt");
        var releaseAssetsPath = Path.Combine(tempRoot, "release-assets.json");
        var sbomPath = Path.Combine(tempRoot, "release-sbom.spdx.json");
        var packageSbomPath = Path.Combine(tempRoot, "package-sbom.spdx.json");
        File.WriteAllText(shaSumsPath, "hash  release.zip");
        File.WriteAllText(releaseAssetsPath, """{"assets":[]}""");
        File.WriteAllText(sbomPath, """{"spdxVersion":"SPDX-2.3"}""");
        File.WriteAllText(packageSbomPath, """{"spdxVersion":"SPDX-2.3","name":"package-sbom"}""");

        return
        [
            "-OutputPath", outputPath,
            "-Repository", "Evanlau1798/wpf-devtools-mcp",
            "-Branch", "master",
            "-CommitSha", "0123456789abcdef0123456789abcdef01234567",
            "-WorkflowRunId", "123456789",
            "-RunnerMatrix", "windows-x64",
            "-RuntimeEvidencePath", runtimeEvidencePath,
            "-DocFxEvidencePath", docFxEvidencePath,
            "-Sha256SumsPath", shaSumsPath,
            "-ReleaseAssetsPath", releaseAssetsPath,
            "-ReleaseSbomPath", sbomPath,
            "-PackageSbomPath", packageSbomPath,
            "-DotnetSdkVersion", "8.0.999",
            "-PowerShellVersion", "7.4.99",
            "-WorkflowSha", "fedcba9876543210fedcba9876543210fedcba98"
        ];
    }
}
