using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class ReleasePackagingWorkflowTests
{
    [Fact]
    public void CiWorkflow_ShouldSmokeTestReleasePackagingScripts()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Publish-Release.ps1",
            "CI should exercise the release packaging path automatically");
        content.Should().Contain("Install-WpfDevTools.ps1",
            "CI should smoke-test installation from a published package");
        content.Should().Contain("Uninstall-WpfDevTools.ps1",
            "CI should verify uninstall/cleanup as part of packaging validation");
    }

    [Fact]
    public void CiWorkflow_ShouldCoverArm64ReleasePackagingLayout()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("architecture: [x64, x86, arm64]",
            "release packaging coverage should include ARM64 artifacts as a first-class target");
        content.Should().Contain("WpfDevTools-win-arm64",
            "ARM64 release packaging should validate the expected output folder contract");
    }

    [Fact]
    public void PublishReleaseScript_ShouldBundleInteractiveSetupWizard()
    {
        var content = File.ReadAllText(GetRepoFilePath("scripts/release/Publish-Release.ps1"));

        content.Should().Contain("Setup-WpfDevTools.ps1",
            "the published package should include the interactive setup wizard alongside install/uninstall scripts");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
