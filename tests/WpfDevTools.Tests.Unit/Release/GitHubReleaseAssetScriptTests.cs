using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class GitHubReleaseAssetScriptTests
{
    [Fact]
    public void ExportGitHubReleaseAssets_ShouldStageZipAssetsAndChecksums()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            Directory.CreateDirectory(inputRoot);
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3_win-x64.zip"), "x64-asset");
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3_win-x86.zip"), "x86-asset");
            File.WriteAllText(Path.Combine(inputRoot, "ignore.txt"), "not-an-asset");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[]
                {
                    "-InputRoot", inputRoot,
                    "-OutputRoot", outputRoot,
                    "-Tag", "v1.2.3",
                    "-OutputJson"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            var stagedRoot = Path.Combine(outputRoot, "v1.2.3");
            File.Exists(Path.Combine(stagedRoot, "release_1.2.3_win-x64.zip")).Should().BeTrue();
            File.Exists(Path.Combine(stagedRoot, "release_1.2.3_win-x86.zip")).Should().BeTrue();
            File.Exists(Path.Combine(stagedRoot, "SHA256SUMS.txt")).Should().BeTrue();
            File.Exists(Path.Combine(stagedRoot, "release-assets.json")).Should().BeTrue();
            File.Exists(Path.Combine(stagedRoot, "upload-gh-release.ps1")).Should().BeTrue();
            File.ReadAllText(Path.Combine(stagedRoot, "SHA256SUMS.txt"))
                .Should().Contain("release_1.2.3_win-x64.zip")
                .And.Contain("release_1.2.3_win-x86.zip");
            File.ReadAllText(Path.Combine(stagedRoot, "upload-gh-release.ps1"))
                .Should().Contain("$PSScriptRoot")
                .And.Contain("$ReleaseTag")
                .And.Contain("release_1.2.3_win-x64.zip");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExportGitHubReleaseAssets_ShouldIncludeDevAssetsInManifest()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            Directory.CreateDirectory(inputRoot);
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3-dev.1_win-x64.zip"), "dev-asset");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[]
                {
                    "-InputRoot", inputRoot,
                    "-OutputRoot", outputRoot,
                    "-Tag", "v1.2.3-dev.1",
                    "-OutputJson"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "v1.2.3-dev.1", "release-assets.json")));
            manifest.RootElement.GetProperty("tag").GetString().Should().Be("v1.2.3-dev.1");
            manifest.RootElement.GetProperty("assets")[0].GetProperty("name").GetString().Should().Be("release_1.2.3-dev.1_win-x64.zip");
            manifest.RootElement.GetProperty("assets")[0].GetProperty("sha256").GetString().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExportGitHubReleaseAssets_ShouldWritePortableUploadScriptAndStableManifest()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            Directory.CreateDirectory(inputRoot);
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3_win-x64.zip"), "x64-asset");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[] { "-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3", "-OutputJson" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var stagedRoot = Path.Combine(outputRoot, "v1.2.3");
            var uploadScript = File.ReadAllText(Path.Combine(stagedRoot, "upload-gh-release.ps1"));
            uploadScript.Should().Contain("$PSScriptRoot");
            uploadScript.Should().NotContain("gh release upload $ReleaseTag @assets --clobber");

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagedRoot, "release-assets.json")));
            var asset = manifest.RootElement.GetProperty("assets")[0];
            asset.TryGetProperty("path", out _).Should().BeFalse();
            manifest.RootElement.TryGetProperty("generatedUtc", out _).Should().BeFalse();
            manifest.RootElement.TryGetProperty("outputRoot", out _).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
