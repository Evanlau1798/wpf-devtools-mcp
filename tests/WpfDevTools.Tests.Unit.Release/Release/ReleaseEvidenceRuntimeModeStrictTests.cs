using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseEvidenceRuntimeModeStrictTests
{
    [Fact]
    public void WriteReleaseEvidence_PublicReleaseStrictMode_ShouldRejectSingleEvidenceClaimingBothInstallModes()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "release-evidence.json");
            var runtimeEvidencePath = WriteRuntimeEvidence(
                tempRoot,
                "runtime-evidence-x64-installed.json",
                installMode: "package-local",
                packageLocalStatus: "passed",
                onlineInstallerStatus: "passed");
            var result = RunReleaseEvidence(tempRoot, outputPath, runtimeEvidencePath, publicReleaseStrict: true);

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("runtimeEvidence.windows-x64.online-installer");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void WriteReleaseEvidence_PublicReleaseStrictMode_ShouldAcceptDistinctInstallModeEvidence()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "release-evidence.json");
            var runtimeEvidenceCsv = string.Join(
                ',',
                WriteRuntimeEvidence(
                    tempRoot,
                    "runtime-evidence-x64-installed.json",
                    installMode: "package-local",
                    packageLocalStatus: "passed",
                    onlineInstallerStatus: "passed-or-not-public"),
                WriteRuntimeEvidence(
                    tempRoot,
                    "runtime-evidence-x64-online.json",
                    installMode: "online-installer",
                    packageLocalStatus: "passed-or-not-public",
                    onlineInstallerStatus: "passed"));

            var result = RunReleaseEvidence(tempRoot, outputPath, runtimeEvidenceCsv, publicReleaseStrict: true);

            result.ExitCode.Should().Be(0, result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void WriteReleaseEvidence_PublicReleaseStrictMode_ShouldAcceptChecksumOnlyProtocolEvidence()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "release-evidence.json");
            var runtimeEvidenceCsv = string.Join(
                ',',
                WriteRuntimeEvidence(
                    tempRoot,
                    "runtime-evidence-x64-installed.json",
                    installMode: "package-local",
                    packageLocalStatus: "passed",
                    onlineInstallerStatus: "passed-or-not-public",
                    liveSmokePassed: false),
                WriteRuntimeEvidence(
                    tempRoot,
                    "runtime-evidence-x64-online.json",
                    installMode: "online-installer",
                    packageLocalStatus: "passed-or-not-public",
                    onlineInstallerStatus: "passed",
                    liveSmokePassed: false));

            var result = RunReleaseEvidence(
                tempRoot,
                outputPath,
                runtimeEvidenceCsv,
                publicReleaseStrict: true,
                releaseTrustMode: "ReleaseChecksumOnly");

            result.ExitCode.Should().Be(0, result.Stderr);
            using var evidence = JsonDocument.Parse(File.ReadAllText(outputPath));
            evidence.RootElement.GetProperty("releaseTrustMode").GetString().Should().Be("ReleaseChecksumOnly");
            evidence.RootElement.GetProperty("liveSmoke").GetProperty("connect").GetBoolean().Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunReleaseEvidence(
        string tempRoot,
        string outputPath,
        string runtimeEvidencePath,
        bool publicReleaseStrict,
        string releaseTrustMode = "Signed")
    {
        var docFxEvidencePath = Path.Combine(tempRoot, "docfx-evidence.json");
        File.WriteAllText(docFxEvidencePath, """{"englishParity":true,"zhTwParity":true,"brokenLinks":0}""");
        var shaSumsPath = Path.Combine(tempRoot, "SHA256SUMS.txt");
        var releaseAssetsPath = Path.Combine(tempRoot, "release-assets.json");
        var sbomPath = Path.Combine(tempRoot, "release-sbom.spdx.json");
        var packageSbomPath = Path.Combine(tempRoot, "package-sbom.spdx.json");
        File.WriteAllText(shaSumsPath, "hash  release.zip");
        File.WriteAllText(releaseAssetsPath, """{"assets":[]}""");
        File.WriteAllText(sbomPath, """{"spdxVersion":"SPDX-2.3"}""");
        File.WriteAllText(packageSbomPath, """{"spdxVersion":"SPDX-2.3","name":"package-sbom"}""");

        var arguments = new List<string>
        {
            "-OutputPath", outputPath,
            "-Repository", "Evanlau1798/wpf-devtools-mcp",
            "-Branch", "v1.2.3",
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
            "-WorkflowSha", "fedcba9876543210fedcba9876543210fedcba98",
            "-ReleaseTrustMode", releaseTrustMode,
            "-UninstallResiduePassed"
        };
        if (publicReleaseStrict)
        {
            arguments.Add("-PublicReleaseStrict");
        }

        return ReleaseScriptTestHarness.RunPowerShellScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseEvidence.ps1"),
            arguments);
    }

    private static string WriteRuntimeEvidence(
        string tempRoot,
        string fileName,
        string installMode,
        string packageLocalStatus,
        string onlineInstallerStatus,
        bool liveSmokePassed = true)
    {
        var path = Path.Combine(tempRoot, fileName);
        File.WriteAllText(path, $$"""
            {
              "installMode": "{{installMode}}",
              "toolsList": {
                "count": 71,
                "nameSetHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "schemaSnapshotHash": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
              },
              "security": {
                "mitmMatrixPassed": true,
                "stdoutPurityPassed": true,
                "screenshotIntegrityPassed": true
              },
              "packageSmoke": {
                "x64PackageLocal": "{{packageLocalStatus}}",
                "x64OnlineInstaller": "{{onlineInstallerStatus}}",
                "x86PackageLocal": "passed-or-not-public",
                "x86OnlineInstaller": "passed-or-not-public",
                "arm64PackageLocal": "passed-or-not-public",
                "arm64OnlineInstaller": "passed-or-not-public"
              },
              "liveSmoke": {
                "connect": {{liveSmokePassed.ToString().ToLowerInvariant()}},
                "ping": {{liveSmokePassed.ToString().ToLowerInvariant()}},
                "getUiSummary": {{liveSmokePassed.ToString().ToLowerInvariant()}},
                "safeRead": {{liveSmokePassed.ToString().ToLowerInvariant()}},
                "mutationRestore": {{liveSmokePassed.ToString().ToLowerInvariant()}},
                "uninstallResidue": true
              }
            }
            """);
        return path;
    }
}
