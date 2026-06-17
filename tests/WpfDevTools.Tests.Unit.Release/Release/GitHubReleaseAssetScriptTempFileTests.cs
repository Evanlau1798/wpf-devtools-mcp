using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("ProcessEnvironment")]
public sealed class GitHubReleaseAssetScriptTempFileTests
{
    [Fact]
    public void ExportGitHubReleaseAssets_ShouldExcludeHarnessTempScriptsFromPackageSbom()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        var tempScriptPath = Path.Combine(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools"),
            $"Sign-Binaries.test-{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(tempScriptPath, "Write-Host test harness temp script");
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            ReleaseScriptTestHarness.WriteDummyReleaseArchiveSet(inputRoot);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[] { "-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3", "-OutputJson" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var packageSbomPath = Path.Combine(outputRoot, "v1.2.3", "package-sbom.spdx.json");
            using var sbom = JsonDocument.Parse(File.ReadAllText(packageSbomPath));
            var fileNames = sbom.RootElement.GetProperty("files")
                .EnumerateArray()
                .Select(file => file.GetProperty("fileName").GetString())
                .ToArray();

            fileNames.Any(name => !string.IsNullOrEmpty(name) && name.Contains(".test-", StringComparison.Ordinal))
                .Should().BeFalse("release package SBOMs must not capture transient test harness scripts from concurrent test runs");
        }
        finally
        {
            try
            {
                if (File.Exists(tempScriptPath))
                {
                    File.Delete(tempScriptPath);
                }
            }
            finally
            {
                ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
            }
        }
    }
}
