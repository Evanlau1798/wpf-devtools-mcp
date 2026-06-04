using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PreReleaseInstallerDocumentationTests
{
    private const string HttpsInstallerCommand =
        "irm https://wpf-mcptools.evanlau1798.com | iex";

    [Theory]
    [InlineData("README.md")]
    [InlineData("docfx/index.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/zh-tw/index.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    public void PublicInstallerDocs_ShouldPublishHttpsAliasButKeepFirstReleaseGate(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(HttpsInstallerCommand);
        content.Should().NotContain("irm http://wpf-mcptools.evanlau1798.com | iex",
            "the public installer alias is HTTPS-only");
        content.Should().Contain("GitHub Release assets",
            $"{relativePath} should make release asset availability the promotion gate");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    public void PreReleaseE2eDocs_ShouldDescribeSourceCheckoutLocalPackageInstall(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("git clone https://github.com/Evanlau1798/wpf-devtools-mcp.git");
        content.Should().Contain("dotnet pack");
        content.Should().Contain("dotnet tool install --tool-path ./.tools --add-source ./artifacts/package");
        content.Should().Contain("pre-release E2E",
            $"{relativePath} should separate source-checkout E2E from published release install");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
