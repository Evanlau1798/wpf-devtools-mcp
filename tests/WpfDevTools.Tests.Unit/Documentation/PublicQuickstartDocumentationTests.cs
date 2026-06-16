using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PublicQuickstartDocumentationTests
{
    [Fact]
    public void PublicQuickstartPages_ShouldUseInstalledReleaseExecutableExamples()
    {
        string[] files =
        [
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/quickstart/claude-desktop.md",
            "docfx/quickstart/cursor-vscode.md"
        ];

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("wpf-devtools-<arch>.exe",
                $"{file} should guide public users toward the packaged release executable");
            content.Should().NotContain("dotnet run --project",
                $"{file} should not use source-tree launch commands as the public quickstart path");
        }
    }

    [Fact]
    public void ClientQuickstarts_ShouldFocusOnGeneratedRegistrationArtifacts()
    {
        string[] files =
        [
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/quickstart/claude-desktop.md",
            "docfx/quickstart/cursor-vscode.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/claude-code.md",
            "docfx/zh-tw/quickstart/openai-codex.md",
            "docfx/zh-tw/quickstart/claude-desktop.md",
            "docfx/zh-tw/quickstart/cursor-vscode.md"
        ];

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("client-registration");
            content.Should().Contain("index.md",
                $"{file} should delegate canonical installation procedure to the 5-Minute Setup");
            content.Should().NotContain("-PackageArchivePath",
                $"{file} should not duplicate deployment/install commands from the canonical quickstart");
        }
    }

    [Fact]
    public void ClientQuickstartDocs_ShouldDescribeCursorArtifactsAndCodexCliBranding()
    {
        foreach (var file in new[] { "docfx/quickstart/cursor-vscode.md", "docfx/zh-tw/quickstart/cursor-vscode.md" })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("Cursor");
            content.Should().Contain("cursor.global.json");
            content.Should().Contain("cursor.project.json");
            content.Should().Contain(".cursor\\mcp.json");
            content.Should().Contain("mcpServers");
        }

        foreach (var file in new[] { "docfx/quickstart/openai-codex.md", "docfx/zh-tw/quickstart/openai-codex.md" })
        {
            File.ReadAllText(GetRepoFilePath(file)).Should().Contain("Codex CLI");
        }
    }

    [Fact]
    public void CanonicalInstallDocs_ShouldDocumentManualPackageFallbackAndSidecars()
    {
        string[] files =
        [
            "README.md",
            "docfx/index.md",
            "docfx/quickstart/index.md",
            "docfx/production/deployment.md",
            "docfx/zh-tw/index.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/production/deployment.md"
        ];

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("irm https://installer.wpf-mcptools.evanlau1798.com | iex");
            content.Should().Contain("release_<version>_win-<arch>.zip");
            content.Should().Contain("SHA256SUMS.txt");
            content.Should().Contain("release-assets.json");
            content.Should().Contain("package-sbom.spdx.json");
            content.Should().Contain("run.bat");
            content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
        }
    }

    [Fact]
    public void ReleaseLayoutDocs_ShouldDescribeZipAssetsAndPackageEntrypoints()
    {
        foreach (var file in new[] { "docfx/production/release-layout.md", "docfx/zh-tw/production/release-layout.md" })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("release_<version>_win-x64.zip");
            content.Should().Contain("run.bat");
            content.Should().Contain("install.ps1");
            content.Should().Contain("client-registration");
            content.Should().Contain("bootstrapper");
            content.Should().Contain("inspectors");
            content.Should().NotContain("setup.ps1");
        }
    }

    [Fact]
    public void PublicLandingPages_ShouldUseDocFxEndpointAndNotInstallerHostAsDocumentation()
    {
        File.ReadAllText(GetRepoFilePath("README.md"))
            .Should().Contain("https://wpf-mcptools.evanlau1798.com/");

        foreach (var file in new[] { "README.md", "docfx/index.md", "docfx/zh-tw/index.md" })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().NotContain("https://evanlau1798.github.io/wpf-devtools-mcp");
            content.Should().NotContain("https://installer.wpf-mcptools.evanlau1798.com/quickstart");
        }
    }

    [Fact]
    public void Quickstarts_ShouldShowFirstSessionPolicyGates()
    {
        foreach (var file in new[] { "docfx/quickstart/index.md", "docfx/zh-tw/quickstart/index.md" })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS");
            content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true");
            content.Should().Contain("WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS");
        }
    }

    [Fact]
    public void Quickstarts_ShouldIncludeRequiredFirstSessionGatesBeforeToolCalls()
    {
        foreach (var file in new[] { "docfx/quickstart/index.md", "docfx/zh-tw/quickstart/index.md" })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            var toolCallIndex = content.IndexOf("`connect`", StringComparison.Ordinal);

            toolCallIndex.Should().BeGreaterThan(0, $"{file} should show the first tool call sequence");
            var beforeToolCalls = content[..toolCallIndex];
            beforeToolCalls.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS");
            beforeToolCalls.Should().Contain("WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS");
            beforeToolCalls.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS");
            beforeToolCalls.Should().Contain("'true'");
        }
    }

    [Fact]
    public void DocfxConfig_ShouldNotPublishScriptsFromDocumentationSite()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/docfx.json"));

        content.Should().NotContain("install.ps1");
        content.Should().NotContain("scripts/");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
