using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class ReleasePackagingWorkflowTests
{
    [Fact]
    public void CiWorkflow_ShouldSmokeTestReleasePackagingScripts()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Publish-Release.ps1");
        content.Should().Contain("bin/install.ps1");
        content.Should().Contain("scripts/online-installer.ps1");
        content.Should().NotContain("Install-WpfDevTools.ps1");
        content.Should().NotContain("Uninstall-WpfDevTools.ps1");
    }

    [Fact]
    public void CiWorkflow_ShouldCoverArm64ReleasePackagingLayout()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("architecture: [x64, x86, arm64]");
        content.Should().Contain("release_*_win-${{ matrix.architecture }}");
    }

    [Fact]
    public void PublishReleaseScript_ShouldBundleCanonicalInstallerScript()
    {
        var content = File.ReadAllText(GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("scripts\\online-installer.ps1");
        content.Should().NotContain("Setup-WpfDevTools.ps1");
        content.Should().NotContain("internal-install.ps1");
    }

    [Fact]
    public void PublishReleaseScript_ShouldCreateZipArchivesForStaticBootstrapInstaller()
    {
        var content = File.ReadAllText(GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("Compress-Archive");
        content.Should().Contain("release_${version}_win-$architecture.zip");
    }

    [Fact]
    public void CiWorkflow_ShouldSmokeTestCanonicalOnlineInstaller()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("scripts/online-installer.ps1");
        content.Should().Contain("release_*_win-${{ matrix.architecture }}.zip");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
