using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PublicQuickstartDocumentationTests
{
    [Fact]
    public void PublicQuickstartPages_ShouldUseInstalledExecutableExamples()
    {
        var files = new[]
        {
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/quickstart/claude-desktop.md",
            "docfx/quickstart/cursor-vscode.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("WpfDevTools.Mcp.Server.exe",
                $"{file} should guide public users toward installed executable paths");
            content.Should().NotContain("dotnet run --project",
                $"{file} should not use source-tree launch commands as the primary public quickstart path");
        }
    }

    [Fact]
    public void DeploymentAndTroubleshootingDocs_ShouldCoverReleaseLayoutAndPublicInstallRisks()
    {
        var deployment = File.ReadAllText(GetRepoFilePath("docfx/production/deployment.md"));
        var troubleshooting = File.ReadAllText(GetRepoFilePath("docfx/guides/troubleshooting.md"));
        var toc = File.ReadAllText(GetRepoFilePath("docfx/toc.yml"));

        deployment.Should().Contain("irm | iex");
        deployment.Should().Contain("optional");
        deployment.Should().Contain("release layout");
        toc.Should().Contain("production/release-layout.md");
        troubleshooting.Should().Contain("architecture mismatch");
        troubleshooting.Should().Contain("missing runtime");
        troubleshooting.Should().Contain("bootstrapper resolution");
    }

    [Fact]
    public void ReleaseLayoutPage_ShouldDescribeInstalledFolderContract()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/production/release-layout.md"));

        content.Should().Contain("WpfDevTools.Mcp.Server.exe");
        content.Should().Contain("client-registration");
        content.Should().Contain("bootstrapper");
        content.Should().Contain("inspectors");
    }

    [Fact]
    public void PublicQuickstartPages_ShouldUseCanonicalGitHubRepositoryUrl()
    {
        var files = new[]
        {
            "docfx/quickstart/index.md",
            "docfx/zh-tw/quickstart/index.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("https://github.com/Evanlau1798/wpf-devtools-mcp");
            content.Should().NotContain("<OWNER>/<REPO>");
        }
    }

    [Fact]
    public void PublicQuickstartPages_ShouldDocumentPackageLocalInstallerFlow()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/quickstart/index.md"));

        content.Should().Contain("included `install.ps1`");
        content.Should().NotContain("releases/latest/download/install.ps1");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
