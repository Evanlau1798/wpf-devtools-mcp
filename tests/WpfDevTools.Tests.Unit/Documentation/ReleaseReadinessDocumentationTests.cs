using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ReleaseReadinessDocumentationTests
{
    [Fact]
    public void Readme_ShouldLinkToReleaseGuideAndPreflightCommand()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("RELEASING.md",
            "maintainers should be able to discover the dedicated release guide from the README");
        content.Should().Contain("Preflight-Release.ps1",
            "the README should point maintainers to the no-upload local release validation command");
    }

    [Fact]
    public void ReleasingGuide_ShouldDocumentLocalPreflightAndGitHubWorkflow()
    {
        var content = File.ReadAllText(GetRepoFilePath("RELEASING.md"));

        content.Should().Contain("Preflight-Release.ps1",
            "the release guide should document the local preflight script");
        content.Should().Contain("workflow_dispatch",
            "the release guide should explain how to manually rerun the GitHub release workflow");
        content.Should().Contain("release.yml",
            "the release guide should point maintainers to the GitHub release automation workflow");
        content.Should().Contain("without uploading to GitHub",
            "the guide should include a local validation path that stops before publication");
        content.Should().Contain("Desktop development with C++",
            "maintainers need the native bootstrapper toolchain prerequisites called out before running release packaging");
        content.Should().Contain("ARM64",
            "the guide should mention that ARM64 release packaging requires the ARM64 native build tools workload/components");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
