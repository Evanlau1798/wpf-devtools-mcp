using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PublicEndpointDocumentationTests
{
    private const string PublicInstallerAlias =
        "irm https://installer.wpf-mcptools.evanlau1798.com";
    private const string InsecurePublicInstallerAlias =
        "irm http://installer.wpf-mcptools.evanlau1798.com";
    private const string RetiredDocsHostInstallerAlias =
        "irm https://wpf-mcptools.evanlau1798.com";
    private const string GitHubBlobSourcePrefix =
        "https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/";
    private const string PublicDocfxBaseUri =
        "https://wpf-mcptools.evanlau1798.com/";

    [Fact]
    public void PublicInstallerDocs_ShouldUseHttpsAliasWithReleaseAssetGate()
    {
        foreach (var file in PublicInstallerDocs())
        {
            var content = File.ReadAllText(GetRepoFilePath(file));

            content.Should().Contain(PublicInstallerAlias,
                $"{file} should publish the HTTPS installer alias for release-candidate docs");
            content.Should().Contain("release",
                $"{file} should describe release-based installation rather than source-tree setup");
            content.Should().NotContain(InsecurePublicInstallerAlias,
                $"{file} should not advertise the retired HTTP installer alias");
            content.Should().NotContain(RetiredDocsHostInstallerAlias,
                $"{file} must not use the DocFX custom domain as the installer alias");
            content.Should().NotContain(GitHubBlobSourcePrefix,
                $"{file} should not link readers to blob source paths instead of release artifacts");
        }
    }

    [Fact]
    public void Readme_ShouldUsePublicDocfxSiteForDocumentationLinks()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain(PublicDocfxBaseUri);
        content.Should().Contain(PublicDocfxBaseUri + "quickstart/");
        content.Should().Contain(PublicDocfxBaseUri + "quickstart/ai-agent-clients.html");
        content.Should().Contain(PublicDocfxBaseUri + "quickstart/sdk-hosted-inspector.html");
        content.Should().Contain(PublicDocfxBaseUri + "production/deployment.html");
        content.Should().Contain(PublicDocfxBaseUri + "production/security.html");
        content.Should().Contain(PublicDocfxBaseUri + "reference/tools/");
    }

    [Fact]
    public void DocfxPagesArtifact_ShouldDeclarePublicCustomDomain()
    {
        var cnamePath = GetRepoFilePath("docfx/CNAME");

        File.Exists(cnamePath).Should().BeTrue("GitHub Pages needs a CNAME file in the DocFX artifact root");
        File.ReadAllText(cnamePath).Trim().Should().Be("wpf-mcptools.evanlau1798.com");
    }

    [Fact]
    public void ReleasingGuide_ShouldGatePublicEndpointPromotionOnAnonymousSmoke()
    {
        var content = File.ReadAllText(GetRepoFilePath("RELEASING.md"));

        content.Should().Contain("public endpoint smoke",
            "maintainers need a release gate before promoting public installer docs");
        content.Should().Contain("https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases/latest");
        content.Should().Contain("https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1");
        content.Should().Contain("https://installer.wpf-mcptools.evanlau1798.com");
        content.Should().Contain("HTTP 200 anonymously");
    }

    private static IEnumerable<string> PublicInstallerDocs()
    {
        yield return "README.md";
        yield return "docfx/index.md";
        yield return "docfx/quickstart/index.md";
        yield return "docfx/production/deployment.md";
        yield return "docfx/production/release-layout.md";
        yield return "docfx/zh-tw/index.md";
        yield return "docfx/zh-tw/quickstart/index.md";
        yield return "docfx/zh-tw/production/deployment.md";
        yield return "docfx/zh-tw/production/release-layout.md";
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
