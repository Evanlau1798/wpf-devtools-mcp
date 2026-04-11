using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PublicQuickstartDocumentationTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private const string RepoUrl = "https://github.com/Evanlau1798/wpf-devtools-mcp";

    [Fact]
    public void PublicQuickstartPages_ShouldUseInstalledReleaseExecutableExamples()
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
            content.Should().Contain("wpf-devtools-x64.exe",
                $"{file} should guide public users toward the packaged release executable");
            content.Should().NotContain("dotnet run --project",
                $"{file} should not use source-tree launch commands as the primary public quickstart path");
        }
    }

    [Fact]
    public void ClientQuickstartDocs_ShouldDescribeCursorArtifactsAndCodexCliBranding()
    {
        var cursorDocs = new[]
        {
            "docfx/quickstart/cursor-vscode.md",
            "docfx/zh-tw/quickstart/cursor-vscode.md"
        };

        foreach (var file in cursorDocs)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("Cursor");
            content.Should().Contain("cursor.global.json");
            content.Should().Contain("cursor.project.json");
            content.Should().Contain(".cursor\\mcp.json");
            content.Should().Contain("mcpServers");
        }

        var codexDocs = new[]
        {
            "docfx/quickstart/openai-codex.md",
            "docfx/zh-tw/quickstart/openai-codex.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md"
        };

        foreach (var file in codexDocs)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("Codex CLI");
        }
    }

    [Fact]
    public void DeploymentAndTroubleshootingDocs_ShouldCoverOnlineInstallerAndRunBatFallback()
    {
        var deployment = File.ReadAllText(GetRepoFilePath("docfx/production/deployment.md"));
        var troubleshooting = File.ReadAllText(GetRepoFilePath("docfx/guides/troubleshooting.md"));
        var toc = File.ReadAllText(GetRepoFilePath("docfx/toc.yml"));

        deployment.Should().Contain("irm | iex");
        deployment.Should().Contain("run.bat");
        deployment.Should().Contain("release layout");
        deployment.Should().Contain("scripts/online-installer.ps1");
        toc.Should().Contain("production/release-layout.md");
        troubleshooting.Should().Contain("architecture mismatch");
        troubleshooting.Should().Contain("missing runtime");
        troubleshooting.Should().Contain("bootstrapper resolution");
    }

    [Fact]
    public void ReleaseLayoutPage_ShouldDescribeRunBatAndBinInstallContract()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/production/release-layout.md"));

        content.Should().Contain("run.bat");
        content.Should().Contain("bin/install.ps1");
        content.Should().Contain("wpf-devtools-x64.exe");
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
            content.Should().Contain(RepoUrl);
            content.Should().NotContain("<OWNER>/<REPO>");
            content.Should().NotContain("https://evanlau1798.github.io/wpf-devtools-mcp");
        }
    }

    [Fact]
    public void PublicQuickstartPages_ShouldDocumentPackageLocalRunBatFlow()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/quickstart/index.md"));

        content.Should().Contain("run.bat");
        content.Should().NotContain("setup.ps1");
        content.Should().NotContain("releases/latest/download/install.ps1");
    }

    [Fact]
    public void PublicDocs_ShouldDocumentCanonicalRepositoryInstallSources()
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
            content.Should().Contain(RepoUrl,
                $"{file} should reference the canonical repository URL");
            content.Should().NotContain("https://evanlau1798.github.io/wpf-devtools-mcp",
                $"{file} should not point readers at GitHub Pages as the install source");
            content.Should().Contain("scripts/online-installer.ps1",
                $"{file} should point readers at the canonical script source in the repository");
        }
    }

    [Fact]
    public void InstallerFacingDocs_ShouldExplainRemoteAndLocalInstallFallbacks()
    {
        var files = new[]
        {
            "README.md",
            "docfx/quickstart/index.md",
            "docfx/production/deployment.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/production/deployment.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("irm",
                $"{file} should explain the one-command bootstrap path");
            content.Should().Contain("run.bat",
                $"{file} should document the reviewed package installer fallback");
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
            content.Should().Contain("scripts/online-installer.ps1");
        }
    }

    [Fact]
    public void Readme_ShouldDescribeCursorUsingDedicatedCursorSchema()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("### Cursor");
        content.Should().Contain(".cursor\\mcp.json");
        content.Should().Contain("\"mcpServers\"");
        content.Should().Contain("### VS Code");
        content.Should().Contain(".vscode/mcp.json");
        content.Should().Contain("\"servers\"");
        content.Should().NotContain("### VS Code / Cursor");
    }

    [Fact]
    public void ClientConfigDocs_ShouldShowCanonicalStdioTypeInPublishedJsonExamples()
    {
        CountOccurrences(
            File.ReadAllText(GetRepoFilePath("README.md")),
            "\"type\": \"stdio\"").Should().Be(3,
            "README publishes Claude Desktop, VS Code, and Cursor JSON examples");

        var quickstartFiles = new[]
        {
            "docfx/quickstart/cursor-vscode.md",
            "docfx/zh-tw/quickstart/cursor-vscode.md"
        };

        foreach (var file in quickstartFiles)
        {
            CountOccurrences(
                File.ReadAllText(GetRepoFilePath(file)),
                "\"type\": \"stdio\"").Should().Be(2,
                $"{file} should keep Cursor and VS Code / Visual Studio examples aligned with the generated installer artifacts");
        }
    }

    [Fact]
    public void ReleaseLayoutDocs_ShouldDescribeZipAssetsAndRunBatEntryPoint()
    {
        var files = new[]
        {
            "docfx/production/release-layout.md",
            "docfx/zh-tw/production/release-layout.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("release_<version>_win-x64.zip");
            content.Should().Contain("run.bat");
            content.Should().Contain("install.ps1");
            content.Should().NotContain("setup.ps1");
        }
    }

    [Fact]
    public void DocfxConfig_ShouldNotPublishScriptsFromDocumentationSite()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/docfx.json"));

        content.Should().NotContain("install.ps1");
        content.Should().NotContain("scripts/");
    }

    [Fact]
    public void CliQuickstarts_ShouldNotHardCodeDefaultRootInManualRegistrationExamples()
    {
        var files = new[]
        {
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/zh-tw/quickstart/claude-code.md",
            "docfx/zh-tw/quickstart/openai-codex.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().NotContain("$env:APPDATA\\WpfDevToolsMcp\\x64\\current\\bin\\wpf-devtools-x64.exe",
                $"{file} should not hard-code the default root and x64 in manual CLI commands");
            content.Should().Contain("client-registration\\",
                $"{file} should keep the generated registration artifact as the source of truth");
        }
    }

    [Fact]
    public void PublicQuickstarts_ShouldDescribeClientRegistrationUnderInstallRoot()
    {
        var files = new[]
        {
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("<InstallRoot>\\<arch>\\client-registration\\",
                $"{file} should present the generated registration artifacts relative to the chosen install root");
            content.Should().NotContain("%APPDATA%\\WpfDevToolsMcp\\x64\\client-registration\\",
                $"{file} should not hard-code the default install root and x64 for generated registration artifacts");
        }
    }

    [Fact]
    public void InstallerAutomationExamples_ShouldUseNonInteractiveAndOutputJson()
    {
        var files = new[]
        {
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/claude-code.md",
            "docfx/zh-tw/quickstart/openai-codex.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("-NonInteractive",
                $"{file} should show explicit non-interactive installer usage for automation-safe examples");
            content.Should().Contain("-OutputJson",
                $"{file} should show machine-readable installer output for automation-safe examples");
        }
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    private static int CountOccurrences(string content, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

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
