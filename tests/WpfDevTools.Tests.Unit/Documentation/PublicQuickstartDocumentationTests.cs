using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PublicQuickstartDocumentationTests
{
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
            content.Should().MatchRegex(@"wpf-devtools-(x64|<arch>)\.exe",
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
    public void ClientQuickstartPages_ShouldPreferReviewedOnlineInstallerBeforeManualReleasePackage()
    {
        var files = new[]
        {
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/quickstart/claude-desktop.md",
            "docfx/quickstart/cursor-vscode.md",
            "docfx/zh-tw/quickstart/claude-code.md",
            "docfx/zh-tw/quickstart/openai-codex.md",
            "docfx/zh-tw/quickstart/claude-desktop.md",
            "docfx/zh-tw/quickstart/cursor-vscode.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            var installerIndex = content.IndexOf("scripts/online-installer.ps1", StringComparison.Ordinal);
            var packageIndex = content.IndexOf("release_<version>_win-<arch>.zip", StringComparison.Ordinal);

            installerIndex.Should().BeGreaterThanOrEqualTo(0,
                $"{file} should mention the reviewed online installer entrypoint");
            packageIndex.Should().BeGreaterThan(installerIndex,
                $"{file} should present the release package as the fallback after the reviewed online installer path");
        }
    }

    [Fact]
    public void DeploymentAndTroubleshootingDocs_ShouldCoverOnlineInstallerAndRunBatFallback()
    {
        var deployment = File.ReadAllText(GetRepoFilePath("docfx/production/deployment.md"));
        var troubleshooting = File.ReadAllText(GetRepoFilePath("docfx/guides/troubleshooting.md"));
        var toc = File.ReadAllText(GetRepoFilePath("docfx/toc.yml"));

        deployment.Should().Contain("GitHub Release assets");
        deployment.Should().Contain("irm https://installer.wpf-mcptools.evanlau1798.com | iex");
        deployment.Should().Contain("validates archive integrity before extraction");
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
        content.Should().Contain("TUI-first installer script");
        content.Should().Contain("bin/installer");
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
    public void PublicRunBatDocs_ShouldDescribeConditionalElevationAndOptOut()
    {
        var files = new[]
        {
            "docfx/index.md",
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/quickstart/claude-desktop.md",
            "docfx/quickstart/cursor-vscode.md",
            "docfx/production/deployment.md",
            "docfx/zh-tw/index.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/claude-desktop.md",
            "docfx/zh-tw/quickstart/cursor-vscode.md",
            "docfx/zh-tw/production/deployment.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("run.bat",
                $"{file} should describe the package-local launcher path");
            content.Should().Contain("WPFDEVTOOLS_SKIP_ELEVATION=1",
                $"{file} should document the opt-out for launcher elevation");
        }
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
    public void LandingPages_ShouldCallOutCursorAsADedicatedEditorEntryPoint()
    {
        File.ReadAllText(GetRepoFilePath("docfx/index.md"))
            .Should().Contain("| Use the server from Cursor | [Cursor setup](quickstart/cursor-vscode.md) |",
                "the English landing page should surface Cursor as its own editor entry point instead of burying it under the VS Code row");

        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/index.md"))
            .Should().Contain("| 從 Cursor 使用這個 server | [Cursor 快速開始](quickstart/cursor-vscode.md) |",
                "the Traditional Chinese landing page should surface Cursor as its own editor entry point instead of burying it under the VS Code row");
    }

    [Fact]
    public void InstallerFacingDocs_ShouldExplainReviewedInstallerAndPackageFallbacks()
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
            content.Should().Contain("GitHub Release assets",
                $"{file} should gate public installer commands on uploaded release assets");
            content.Should().Contain("irm https://installer.wpf-mcptools.evanlau1798.com | iex",
                $"{file} should publish the reviewed HTTPS installer alias for release-candidate docs");
            content.Should().Contain("-PackageArchivePath",
                $"{file} should point readers at the local package installer path while public endpoints are unavailable");
            content.Should().Contain("integrity",
                $"{file} should explain that the reviewed installer validates the release archive before extraction");
            content.Should().Contain("run.bat",
                $"{file} should document the reviewed package installer fallback");
        }
    }

    [Fact]
    public void LandingPages_WithManualArchiveFallback_ShouldRequireReleaseProvenanceVerification()
    {
        var files = new[]
        {
            "README.md",
            "docfx/index.md",
            "docfx/zh-tw/index.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("SHA256SUMS.txt",
                $"{file} should tell users to verify the downloaded archive hash before trusting the manual release package path");
            content.Should().Contain("release-assets.json",
                $"{file} should point users at the canonical release asset metadata before they run the extracted installer manually");
            content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
                $"{file} should explain how to provide an explicit signer pin when the verified archive is no longer kept beside the extracted package");
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
    public void ManualPackageFallbackDocs_ShouldDescribeReleaseProvenanceSidecars()
    {
        var files = new[]
        {
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/quickstart/claude-desktop.md",
            "docfx/quickstart/cursor-vscode.md",
            "docfx/production/deployment.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/claude-code.md",
            "docfx/zh-tw/quickstart/openai-codex.md",
            "docfx/zh-tw/quickstart/claude-desktop.md",
            "docfx/zh-tw/quickstart/cursor-vscode.md",
            "docfx/zh-tw/production/deployment.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("SHA256SUMS.txt",
                $"{file} should require release checksum sidecars before trusting an extracted package");
            content.Should().Contain("release-assets.json",
                $"{file} should point readers at the canonical release asset metadata before run.bat is trusted");
            content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
                $"{file} should explain how to provide an explicit signer pin when verified archive sidecars are no longer adjacent");
        }
    }

    [Fact]
    public void ClientConfigDocs_ShouldDescribeCursorUsingDedicatedCursorSchema()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/quickstart/cursor-vscode.md"));
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("## Cursor");
        content.Should().Contain("client-registration\\cursor.global.json");
        content.Should().Contain("%USERPROFILE%\\.cursor\\mcp.json");
        content.Should().Contain("\"mcpServers\"");
        content.Should().Contain("## VS Code");
        content.Should().Contain("client-registration\\vscode.json");
        content.Should().Contain("%APPDATA%\\Code\\User\\mcp.json");
        content.Should().Contain("\"servers\"");
        content.Should().NotContain("### VS Code / Cursor");
        readme.Should().NotContain("### Cursor");
        readme.Should().NotContain("\"mcpServers\"");
    }

    [Fact]
    public void ClientConfigDocs_ShouldShowCanonicalStdioTypeInPublishedJsonExamples()
    {
        File.ReadAllText(GetRepoFilePath("README.md"))
            .Should().NotContain("\"type\": \"stdio\"",
                "README should link to DocFX client setup pages instead of carrying full JSON examples");

        var claudeDesktopFiles = new[]
        {
            "docfx/quickstart/claude-desktop.md",
            "docfx/zh-tw/quickstart/claude-desktop.md",
        };

        foreach (var file in claudeDesktopFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("\"type\": \"stdio\"");
            content.Should().Contain("mcpServers");
        }

        var cursorVsCodeFiles = new[]
        {
            "docfx/quickstart/cursor-vscode.md",
            "docfx/zh-tw/quickstart/cursor-vscode.md"
        };

        foreach (var file in cursorVsCodeFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("\"type\": \"stdio\"");
            content.Should().Contain("Cursor");
            content.Should().Contain("VS Code");
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
            content.Should().Contain("bin/installer");
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

    [Theory]
    [InlineData(
        "docfx/quickstart/index.md",
        "WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS",
        "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true",
        "includeFocus = $true")]
    [InlineData(
        "docfx/zh-tw/quickstart/index.md",
        "WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS",
        "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true",
        "includeFocus = $true")]
    public void FirstSessionQuickstarts_ShouldShowRawInjectionPolicyAndValidSnapshotExample(
        string relativePath,
        string rawInjectionAllowlist,
        string sensitiveReadGate,
        string focusOnlySnapshot)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(rawInjectionAllowlist,
            $"{relativePath} should make the second raw-injection allowlist visible before first connect troubleshooting");
        content.Should().Contain(sensitiveReadGate,
            $"{relativePath} should show the read gate needed before scene and focused reads");
        content.Should().Contain(focusOnlySnapshot,
            $"{relativePath} should include a minimal valid capture_state_snapshot example");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

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

}
