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

        content.Should().Contain("architecture: [x64, x86]");
        content.Should().Contain("release_*_win-${{ matrix.architecture }}");
        content.Should().Contain("release-packaging-smoke-arm64",
            "ARM64 runtime smoke should run on a dedicated ARM64 lane instead of pretending hosted x64 install/uninstall smoke validates the shipped runtime");
        content.Should().Contain("[self-hosted, Windows, ARM64]",
            "ARM64 runtime smoke needs an actual ARM64 runner so the packaged executable can be launched before release publication");
        content.Should().Contain("WPFDEVTOOLS_ENABLE_ARM64_RUNTIME_SMOKE",
            "the dedicated ARM64 lane should stay opt-in for CI until the repository runner is configured");
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

    [Fact]
    public void CiWorkflow_ShouldSmokeTestInstalledServerRuntimeAfterInstall()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("Test-PackagedServerRuntime.ps1",
            "release packaging smoke should launch the packaged server entrypoint after install so runtime failures are caught before publication");
        content.Should().Contain("Start installed package runtime smoke test",
            "package-local installs should be exercised beyond install/uninstall script success");
        content.Should().Contain("Start online-installed runtime smoke test",
            "online-installer installs should also launch the packaged server entrypoint before cleanup");
    }

    [Fact]
    public void CiWorkflow_ShouldRunReleasePackagingSmokeWithDeterministicSignatureTestMode()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("WPFDEVTOOLS_INSTALLER_TEST_MODE",
            "release packaging smoke tests need an executable signature-validation lane even when CI does not hold the production signing certificate");
        content.Should().Contain("WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA",
            "hosted-runner local archive smoke installs need the explicit test-only trust hook so they exercise the generated release sidecars instead of falling back to live release metadata lookup");
        content.Should().Contain("WPFDEVTOOLS_TEST_SIGNATURE_STATUS",
            "Publish-Release.ps1 only supports deterministic fake signature validation when the workflow opts into installer test mode");
        content.Should().Contain("Valid",
            "the smoke workflow should force a valid test signature state instead of depending on unsigned runner artifacts");
    }

    [Fact]
    public void CiWorkflow_ShouldUninstallViaInstalledPackageEntryPoint()
    {
        var content = File.ReadAllText(GetRepoFilePath(".github/workflows/ci-cd.yml"));

        content.Should().Contain("current\\bin\\install.ps1",
            "the uninstall smoke steps should exercise the installed package-local entrypoint instead of falling back to the source-tree installer helper roots");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
