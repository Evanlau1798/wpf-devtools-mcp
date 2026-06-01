using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseEvidenceScriptTests
{
    [Fact]
    public void WriteReleaseEvidence_ShouldCreateMachineReadableReleaseEvidence()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "release-evidence.json");
            var runtimeEvidencePath = Path.Combine(tempRoot, "runtime-evidence.json");
            File.WriteAllText(
                runtimeEvidencePath,
                """
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
                    "x86PackageLocal": "passed",
                    "x86OnlineInstaller": "passed",
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

            var shaSumsPath = Path.Combine(tempRoot, "SHA256SUMS.txt");
            var releaseAssetsPath = Path.Combine(tempRoot, "release-assets.json");
            var sbomPath = Path.Combine(tempRoot, "release-sbom.spdx.json");
            File.WriteAllText(shaSumsPath, "hash  release.zip");
            File.WriteAllText(releaseAssetsPath, """{"assets":[]}""");
            File.WriteAllText(sbomPath, """{"spdxVersion":"SPDX-2.3"}""");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseEvidence.ps1"),
                [
                    "-OutputPath", outputPath,
                    "-Repository", "Evanlau1798/wpf-devtools-mcp",
                    "-Branch", "codex/v5-near10-production-fixes",
                    "-CommitSha", "0123456789abcdef0123456789abcdef01234567",
                    "-WorkflowRunId", "123456789",
                    "-RunnerMatrix", "windows-x64,windows-x86,windows-arm64-or-not-public",
                    "-RuntimeEvidencePath", runtimeEvidencePath + "," + runtimeEvidencePath,
                    "-Sha256SumsPath", shaSumsPath,
                    "-ReleaseAssetsPath", releaseAssetsPath,
                    "-ReleaseSbomPath", sbomPath,
                    "-ExpectedThumbprintHash", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                    "-ObservedThumbprintHash", "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
                ]);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var evidence = JsonDocument.Parse(File.ReadAllText(outputPath));
            var root = evidence.RootElement;
            root.GetProperty("repository").GetString().Should().Be("Evanlau1798/wpf-devtools-mcp");
            root.GetProperty("branch").GetString().Should().Be("codex/v5-near10-production-fixes");
            root.GetProperty("commitSha").GetString().Should().Be("0123456789abcdef0123456789abcdef01234567");
            root.GetProperty("workflowRunIds")[0].GetInt64().Should().Be(123456789);
            root.GetProperty("toolsList").GetProperty("count").GetInt32().Should().Be(64);
            root.GetProperty("docfx").GetProperty("englishParity").GetBoolean().Should().BeTrue();
            root.GetProperty("docfx").GetProperty("zhTwParity").GetBoolean().Should().BeTrue();
            root.GetProperty("docfx").GetProperty("brokenLinks").GetInt32().Should().Be(0);
            root.GetProperty("releaseAssets").GetProperty("sha256SumsHash").GetString()
                .Should().MatchRegex("^[a-f0-9]{64}$");
            root.GetProperty("signing").GetProperty("expectedThumbprintHash").GetString()
                .Should().MatchRegex("^[a-f0-9]{64}$");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
