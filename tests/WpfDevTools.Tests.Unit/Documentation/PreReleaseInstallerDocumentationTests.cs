using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PreReleaseInstallerDocumentationTests
{
    private const string HttpsInstallerAlias =
        "irm https://installer.wpf-mcptools.evanlau1798.com";
    private const string StableLatestInstallerCommand =
        "irm https://installer.wpf-mcptools.evanlau1798.com | iex";
    private const string PreviewPrereleaseInstallerCommand =
        "& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease";
    private const string PinnedPrereleaseVersionExample =
        "$version = 'v1.0.0-beta.60'";
    private const string PinnedPrereleaseInstallerCommand =
        "& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version $version -Prerelease";
    private const string EnglishArm64PreviewWarning =
        "ARM64 archives may be published as preview assets, but they are not guaranteed stable";
    private const string TraditionalChineseArm64PreviewWarning =
        "ARM64 發行檔可作為 preview asset 提供，但目前不保證穩定性";

    [Theory]
    [InlineData("docfx/index.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/production/release-layout.md")]
    [InlineData("docfx/zh-tw/index.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    [InlineData("docfx/zh-tw/production/release-layout.md")]
    public void PublicInstallerDocs_ShouldPublishHttpsAliasWithReleaseAssetGate(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(HttpsInstallerAlias);
        content.Should().Contain(StableLatestInstallerCommand);
        content.Should().NotContain(PreviewPrereleaseInstallerCommand);
        content.Should().NotContain("irm http://wpf-mcptools.evanlau1798.com | iex",
            "the public installer alias is HTTPS-only");
        content.Should().Contain("release_<version>_win-<arch>.zip",
            $"{relativePath} should gate the public installer on the versioned release package");
        content.Should().Contain("release-assets.json",
            $"{relativePath} should require release metadata before users trust the installer path");
    }

    [Theory]
    [InlineData("docfx/index.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/production/release-layout.md")]
    [InlineData("docfx/zh-tw/index.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    [InlineData("docfx/zh-tw/production/release-layout.md")]
    public void PublicInstallerDocs_ShouldDefaultOnboardingToStableReleaseCommand(
        string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(StableLatestInstallerCommand,
            $"{relativePath} should promote the stable public installer command for release onboarding");
        content.Should().NotContain(PreviewPrereleaseInstallerCommand,
            $"{relativePath} should not default public onboarding to latest prerelease after release readiness");
        content.Should().NotContain("until the first stable GitHub Release is published");
        content.Should().NotContain("第一個 stable GitHub Release 發布前");
    }

    [Fact]
    public void Readme_ShouldPromoteStableReleaseInstallerCommandAndDelegatePrereleaseDetails()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain(StableLatestInstallerCommand);
        content.Should().NotContain(PreviewPrereleaseInstallerCommand);
        content.Should().Contain(PinnedPrereleaseInstallerCommand);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("AGENT_INSTALL.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    public void PublicEntryDocs_ShouldProvideCopyableUninstallRecovery(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("$installRoot = '<exact-install-root>'");
        content.Should().Contain("-Action uninstall -Client '<client-id>' -InstallRoot $installRoot");
        content.Should().Contain("-Action full-uninstall -InstallRoot $installRoot");
        content.Should().Contain("-NonInteractive -Force -OutputJson");
    }

    [Theory]
    [InlineData("docfx/index.md", EnglishArm64PreviewWarning)]
    [InlineData("docfx/quickstart/index.md", EnglishArm64PreviewWarning)]
    [InlineData("docfx/guides/agent-assisted-install.md", EnglishArm64PreviewWarning)]
    [InlineData("docfx/production/deployment.md", EnglishArm64PreviewWarning)]
    [InlineData("docfx/production/release-layout.md", EnglishArm64PreviewWarning)]
    [InlineData("docfx/zh-tw/index.md", TraditionalChineseArm64PreviewWarning)]
    [InlineData("docfx/zh-tw/quickstart/index.md", TraditionalChineseArm64PreviewWarning)]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md", TraditionalChineseArm64PreviewWarning)]
    [InlineData("docfx/zh-tw/production/deployment.md", TraditionalChineseArm64PreviewWarning)]
    [InlineData("docfx/zh-tw/production/release-layout.md", TraditionalChineseArm64PreviewWarning)]
    public void PublicInstallerDocs_ShouldWarnArm64PreviewAssetsAreNotStable(
        string relativePath,
        string expectedWarning)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedWarning);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("AGENT_INSTALL.md")]
    [InlineData("docfx/index.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/zh-tw/index.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    public void ProductionFacingDocs_ShouldNotContainE2eOrSmokeRunbookResidue(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        string[] blockedTerms =
        [
            "GitHub pre-release E2E",
            "validation-only",
            "64-tool",
            ".e2e",
            "NDJSON smoke",
            "stable release assets and anonymous endpoint smoke checks have passed",
            "anonymous endpoint smoke checks",
            "-ExecutionPolicy Bypass"
        ];

        foreach (var term in blockedTerms)
        {
            content.Should().NotContain(term,
                $"{relativePath} should read as production onboarding, not release-validation runbook material");
        }
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    public void PublicQuickstartDocs_ShouldShowConcretePinnedPrereleaseExampleWithoutE2eResidue(
        string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(PinnedPrereleaseVersionExample,
            $"{relativePath} should show a copyable pinned prerelease shape without using an E2E validation tag");
        content.Should().Contain(PinnedPrereleaseInstallerCommand);
        content.Should().NotContain("$version = '<version>'");
        content.Should().NotContain("0.1.0-e2e.");
    }

    [Theory]
    [InlineData(
        "docfx/quickstart/index.md",
        "package-local `run.bat` or `bin\\wpf-devtools-<arch>.exe`",
        "same directory as the original archive and `SHA256SUMS.txt`")]
    [InlineData(
        "docfx/guides/troubleshooting.md",
        "package-local `run.bat` or `bin\\wpf-devtools-<arch>.exe`",
        "same directory as the original archive and `SHA256SUMS.txt`")]
    [InlineData(
        "docfx/zh-tw/quickstart/index.md",
        "package-local `run.bat` 或 `bin\\wpf-devtools-<arch>.exe`",
        "與原始 archive 及 `SHA256SUMS.txt` 放在同一目錄")]
    [InlineData(
        "docfx/zh-tw/guides/troubleshooting.md",
        "package-local `run.bat` 或 `bin\\wpf-devtools-<arch>.exe`",
        "與原始 archive 及 `SHA256SUMS.txt` 放在同一目錄")]
    public void PortablePrereleaseDocs_ShouldDescribePackageLocalChecksumTrustBoundary(
        string relativePath,
        string portableEntrypoint,
        string sidecarBoundary)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(portableEntrypoint,
            $"{relativePath} should not imply package-local prerelease execution is always invalid");
        content.Should().Contain(sidecarBoundary,
            $"{relativePath} should tie portable package-local trust to the original archive and checksum sidecar");
        content.Should().Contain("WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY",
            $"{relativePath} should document the explicit metadata directory path so agents do not need to copy sidecars beside the extracted package");
        content.Should().NotContain("Do not register a package-local executable");
        content.Should().NotContain("不要註冊解壓 archive 內的 package-local executable");
    }

    [Theory]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    public void ManualVerifiedQuickstartInstall_ShouldUsePublicInstallerAliasInsteadOfPackageLocalInstallScript(
        string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com)))",
            $"{relativePath} should keep manual verified install on the reviewed public installer entrypoint");
        content.Should().Contain("-PackageArchivePath $archive");
        content.Should().Contain("-TrustedReleaseMetadataDirectory $metadata");
        content.Should().NotContain("Join-Path $packageRoot 'bin\\install.ps1'",
            $"{relativePath} should not conflict with the agent install contract by using the package-local installer as the install command");
    }

    [Theory]
    [InlineData("AGENT_INSTALL.md")]
    [InlineData("docfx/index.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/production/release-layout.md")]
    [InlineData("docfx/zh-tw/index.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    [InlineData("docfx/zh-tw/production/release-layout.md")]
    public void ReleaseDocs_ShouldDocumentCompleteProductionSidecarSet(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        string[] requiredSidecars =
        [
            "SHA256SUMS.txt",
            "release-assets.json",
            "release-sbom.spdx.json",
            "package-sbom.spdx.json"
        ];

        foreach (var sidecar in requiredSidecars)
        {
            content.Should().Contain(sidecar,
                $"{relativePath} should keep the production release sidecar taxonomy synchronized");
        }

        content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
    }

    [Theory]
    [InlineData("docfx/quickstart/sdk-hosted-inspector.md")]
    [InlineData("docfx/zh-tw/quickstart/sdk-hosted-inspector.md")]
    public void SdkHostedDocs_ShouldDescribeCurrentLocalPackageFlowInsteadOfFutureNuGetOnboarding(
        string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        (content.Contains("Until the SDK package is published to NuGet", StringComparison.Ordinal)
            || content.Contains("在 SDK package 正式發布到 NuGet 前", StringComparison.Ordinal))
            .Should().BeTrue($"{relativePath} should distinguish current local package flow from future NuGet onboarding");
        content.Should().Contain("dotnet pack");
        content.Should().Contain("--source");
        content.Should().Contain("PackageSourceMapping");
        content.Should().Contain("Central Package Management");
        content.Should().Contain("NuGet.config");
        content.Should().NotContain("dotnet add package WpfDevTools.Inspector.Sdk",
            "the future NuGet.org install command must not become the production onboarding path before publication");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
