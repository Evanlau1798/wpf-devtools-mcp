using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PublicEndpointDocumentationTests
{
    private const string PublicEndpointPendingText =
        "Public release endpoints are not yet anonymously reachable";
    private const string PublicInstallerAlias =
        "irm https://wpf-mcptools.evanlau1798.com";
    private const string GitHubBlobSourcePrefix =
        "https://github.com/Evanlau1798/wpf-devtools-mcp/blob/master/";

    [Fact]
    public void PublicInstallerDocs_ShouldNotAdvertiseUnverifiedAnonymousInstallerAlias()
    {
        foreach (var file in PublicInstallerDocs())
        {
            var content = File.ReadAllText(GetRepoFilePath(file));

            content.Should().Contain(PublicEndpointPendingText,
                $"{file} should warn readers before public release endpoints pass anonymous smoke checks");
            content.Should().NotContain(PublicInstallerAlias,
                $"{file} should not advertise a one-line public installer while the alias returns 404 anonymously");
            content.Should().NotContain(GitHubBlobSourcePrefix,
                $"{file} should not link to source paths through the unpublished public GitHub repository");
        }
    }

    [Fact]
    public void ReleasingGuide_ShouldGatePublicEndpointPromotionOnAnonymousSmoke()
    {
        var content = File.ReadAllText(GetRepoFilePath("RELEASING.md"));

        content.Should().Contain("public endpoint smoke",
            "maintainers need a release gate before promoting public installer docs");
        content.Should().Contain("https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases/latest");
        content.Should().Contain("https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1");
        content.Should().Contain("https://wpf-mcptools.evanlau1798.com");
        content.Should().Contain("HTTP 200 anonymously");
    }

    private static IEnumerable<string> PublicInstallerDocs()
    {
        yield return "README.md";
        yield return "docfx/index.md";
        yield return "docfx/quickstart/index.md";
        yield return "docfx/quickstart/ai-agent-clients.md";
        yield return "docfx/quickstart/claude-code.md";
        yield return "docfx/quickstart/openai-codex.md";
        yield return "docfx/quickstart/claude-desktop.md";
        yield return "docfx/quickstart/cursor-vscode.md";
        yield return "docfx/production/deployment.md";
        yield return "docfx/production/release-layout.md";
        yield return "docfx/zh-tw/index.md";
        yield return "docfx/zh-tw/quickstart/index.md";
        yield return "docfx/zh-tw/quickstart/ai-agent-clients.md";
        yield return "docfx/zh-tw/quickstart/claude-code.md";
        yield return "docfx/zh-tw/quickstart/openai-codex.md";
        yield return "docfx/zh-tw/quickstart/claude-desktop.md";
        yield return "docfx/zh-tw/quickstart/cursor-vscode.md";
        yield return "docfx/zh-tw/production/deployment.md";
        yield return "docfx/zh-tw/production/release-layout.md";
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
