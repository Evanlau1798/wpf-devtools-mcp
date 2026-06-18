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
    public void PublicInstallerDocs_ShouldPublishHttpsAliasWithReleaseAssetGate(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(HttpsInstallerAlias);
        content.Should().Contain(PreviewPrereleaseInstallerCommand);
        content.Should().NotContain("irm http://wpf-mcptools.evanlau1798.com | iex",
            "the public installer alias is HTTPS-only");
        content.Should().Contain("release_<version>_win-<arch>.zip",
            $"{relativePath} should gate the public installer on the versioned release package");
        content.Should().Contain("release-assets.json",
            $"{relativePath} should require release metadata before users trust the installer path");
    }

    [Theory]
    [InlineData("README.md")]
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
    public void PublicInstallerDocs_ShouldDefaultPreviewOnboardingToLatestPrereleaseUntilStableExists(
        string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(PreviewPrereleaseInstallerCommand,
            $"{relativePath} should install from the latest pre-release until a stable GitHub release exists");
        content.Should().Contain("pre-release",
            $"{relativePath} should make the release channel explicit instead of implying stable latest");
        content.Should().NotContain(StableLatestInstallerCommand,
            $"{relativePath} should not promote the stable latest installer command before a stable release exists");
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
