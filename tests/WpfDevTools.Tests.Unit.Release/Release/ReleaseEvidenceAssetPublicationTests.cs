using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseEvidenceAssetPublicationTests
{
    [Fact]
    public void ExportGitHubReleaseAssets_UploadScriptShouldPublishReleaseEvidenceJson()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            ReleaseScriptTestHarness.WriteDummyReleaseArchiveSet(inputRoot);

            var exportResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                ["-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3", "-OutputJson"]);

            exportResult.ExitCode.Should().Be(0, exportResult.Stderr);
            var stagedRoot = Path.Combine(outputRoot, "v1.2.3");
            var uploadScriptPath = Path.Combine(stagedRoot, "upload-gh-release.ps1");
            var uploadScript = File.ReadAllText(uploadScriptPath);

            uploadScript.Should().Contain("release-evidence.json");

            var uploadResult = ReleaseScriptTestHarness.RunPowerShellScript(
                uploadScriptPath,
                ["-ReleaseTag", "v1.2.3"]);
            uploadResult.ExitCode.Should().NotBe(0);
            (uploadResult.Stdout + Environment.NewLine + uploadResult.Stderr)
                .Should().Contain("release-evidence.json")
                .And.Contain("missing staged release asset");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
