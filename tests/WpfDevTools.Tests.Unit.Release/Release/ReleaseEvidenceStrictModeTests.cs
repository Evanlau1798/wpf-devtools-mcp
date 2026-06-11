using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseEvidenceStrictModeTests
{
    [Fact]
    public void WriteReleaseEvidence_PublicReleaseStrictMode_ShouldFailWhenSecurityEvidenceIsFalse()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "release-evidence.json");
            var runtimeEvidencePath = WriteRuntimeEvidence(tempRoot, mitmPassed: false, screenshotPassed: false);
            var docFxEvidencePath = WriteDocFxEvidence(tempRoot);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseEvidence.ps1"),
                BuildArguments(tempRoot, outputPath, runtimeEvidencePath, docFxEvidencePath, extraArguments: ["-PublicReleaseStrict"]));

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("security.mitmMatrixPassed")
                .And.Contain("security.screenshotIntegrityPassed");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void WriteReleaseEvidence_PublicReleaseStrictMode_ShouldAcceptSeparateSecurityEvidence()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "release-evidence.json");
            var runtimeEvidencePath = string.Join(
                ',',
                WriteRuntimeEvidence(
                    tempRoot,
                    mitmPassed: false,
                    screenshotPassed: false,
                    fileName: "runtime-evidence-x64-installed.json",
                    installMode: "package-local",
                    packageLocalStatus: "passed",
                    onlineInstallerStatus: "passed-or-not-public"),
                WriteRuntimeEvidence(
                    tempRoot,
                    mitmPassed: false,
                    screenshotPassed: false,
                    fileName: "runtime-evidence-x64-online.json",
                    installMode: "online-installer",
                    packageLocalStatus: "passed-or-not-public",
                    onlineInstallerStatus: "passed"));
            var docFxEvidencePath = WriteDocFxEvidence(tempRoot);
            var securityEvidencePath = WriteSecurityEvidence(tempRoot);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseEvidence.ps1"),
                BuildArguments(
                    tempRoot,
                    outputPath,
                    runtimeEvidencePath,
                    docFxEvidencePath,
                    extraArguments: ["-SecurityEvidencePath", securityEvidencePath, "-PublicReleaseStrict"]));

            result.ExitCode.Should().Be(0, result.Stderr);
            using var evidence = JsonDocument.Parse(File.ReadAllText(outputPath));
            var security = evidence.RootElement.GetProperty("security");
            security.GetProperty("mitmMatrixPassed").GetBoolean().Should().BeTrue();
            security.GetProperty("screenshotIntegrityPassed").GetBoolean().Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string WriteRuntimeEvidence(
        string tempRoot,
        bool mitmPassed,
        bool screenshotPassed,
        string fileName = "runtime-evidence.json",
        string installMode = "package-local",
        string packageLocalStatus = "passed",
        string onlineInstallerStatus = "passed-or-not-public")
    {
        var path = Path.Combine(tempRoot, fileName);
        File.WriteAllText(path, $$"""
            {
              "installMode": "{{installMode}}",
              "toolsList": {
                "count": 64,
                "nameSetHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "schemaSnapshotHash": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
              },
              "security": {
                "mitmMatrixPassed": {{mitmPassed.ToString().ToLowerInvariant()}},
                "stdoutPurityPassed": true,
                "screenshotIntegrityPassed": {{screenshotPassed.ToString().ToLowerInvariant()}}
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
                "connect": true,
                "ping": true,
                "getUiSummary": true,
                "safeRead": true,
                "mutationRestore": true,
                "uninstallResidue": true
              }
            }
            """);
        return path;
    }

    private static string WriteDocFxEvidence(string tempRoot)
    {
        var path = Path.Combine(tempRoot, "docfx-evidence.json");
        File.WriteAllText(path, """
            {
              "englishParity": true,
              "zhTwParity": true,
              "brokenLinks": 0
            }
            """);
        return path;
    }

    private static string WriteSecurityEvidence(string tempRoot)
    {
        var path = Path.Combine(tempRoot, "security-evidence.json");
        File.WriteAllText(path, """
            {
              "security": {
                "mitmMatrixPassed": true,
                "screenshotIntegrityPassed": true
              }
            }
            """);
        return path;
    }

    private static string[] BuildArguments(
        string tempRoot,
        string outputPath,
        string runtimeEvidencePath,
        string docFxEvidencePath,
        string[]? extraArguments = null)
    {
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
            "-WorkflowSha", "fedcba9876543210fedcba9876543210fedcba98"
        };

        if (extraArguments is not null)
        {
            arguments.AddRange(extraArguments);
        }

        return arguments.ToArray();
    }
}
