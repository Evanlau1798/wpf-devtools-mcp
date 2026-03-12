using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PublicQuickstartDocumentationTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

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

    [Fact]
    public void PublicDocs_ShouldDocumentGitHubPagesOneCommandInstaller()
    {
        var files = new[]
        {
            "README.md",
            "docfx/index.md",
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/production/deployment.md",
            "docfx/zh-tw/index.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/claude-code.md",
            "docfx/zh-tw/quickstart/openai-codex.md",
            "docfx/zh-tw/production/deployment.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("https://evanlau1798.github.io/wpf-devtools-mcp/install.ps1",
                $"{file} should reference the GitHub Pages hosted one-command installer");
            content.Should().Contain("irm",
                $"{file} should explain the one-command bootstrap path");
        }
    }

    [Fact]
    public void PublicQuickstartPages_ShouldReferenceScriptsOnlineInstallerEntryPoint()
    {
        var files = new[]
        {
            "README.md",
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("scripts/online-installer.ps1",
                $"{file} should point maintainers at the canonical online installer source entrypoint");
        }
    }

    [Fact]
    public void ReleaseLayoutDocs_ShouldDescribeZipAssetsAndSetupWizard()
    {
        var files = new[]
        {
            "docfx/production/release-layout.md",
            "docfx/zh-tw/production/release-layout.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("release_{version}_win-x64.zip");
            content.Should().Contain("setup.ps1");
            content.Should().Contain("install.ps1");
        }
    }

    [Fact]
    public void DocfxConfig_ShouldPublishBootstrapInstallerAtSiteRoot()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/docfx.json"));

        content.Should().Contain("install.ps1");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
