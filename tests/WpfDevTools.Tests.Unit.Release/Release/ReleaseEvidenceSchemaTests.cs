using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseEvidenceSchemaTests
{
    [Fact]
    public void ReleaseEvidenceSchema_ShouldDeclareAuditableReleaseSections()
    {
        var schemaPath = ReleaseScriptTestHarness.GetRepoFilePath(
            "scripts/tools/packaging/release-evidence.schema.json");
        var schema = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();

        schema["$schema"]!.GetValue<string>().Should().Be("https://json-schema.org/draft/2020-12/schema");
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        required.Should().Contain(["repository", "commitSha", "releaseTrustMode", "docfx", "security", "packageSmoke", "liveSmoke", "releaseAssets"]);
    }

    [Fact]
    public void TestReleaseEvidenceSchema_ShouldAcceptWriterOutputAndRejectMissingRequiredSection()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var validEvidencePath = Path.Combine(tempRoot, "release-evidence.json");
            WriteReleaseEvidence(tempRoot, validEvidencePath);

            var validResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Test-ReleaseEvidenceSchema.ps1"),
                ["-EvidencePath", validEvidencePath]);
            validResult.ExitCode.Should().Be(0, validResult.Stderr);

            var invalidEvidencePath = Path.Combine(tempRoot, "release-evidence-missing-assets.json");
            var invalidEvidence = JsonNode.Parse(File.ReadAllText(validEvidencePath))!.AsObject();
            invalidEvidence.Remove("releaseAssets");
            File.WriteAllText(invalidEvidencePath, invalidEvidence.ToJsonString());

            var invalidResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Test-ReleaseEvidenceSchema.ps1"),
                ["-EvidencePath", invalidEvidencePath]);
            invalidResult.ExitCode.Should().NotBe(0);
            (invalidResult.Stdout + Environment.NewLine + invalidResult.Stderr).Should().Contain("releaseAssets");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void WriteReleaseEvidence(string tempRoot, string outputPath)
    {
        var runtimeEvidencePath = Path.Combine(tempRoot, "runtime-evidence.json");
        File.WriteAllText(runtimeEvidencePath, """
            {
              "toolsList": {
                "count": 74,
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

        var result = ReleaseScriptTestHarness.RunPowerShellScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseEvidence.ps1"),
            [
                "-OutputPath", outputPath,
                "-Repository", "Evanlau1798/wpf-devtools-mcp",
                "-Branch", "codex/release-evidence-chain-fixes",
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
            ]);
        result.ExitCode.Should().Be(0, result.Stderr);
    }
}
