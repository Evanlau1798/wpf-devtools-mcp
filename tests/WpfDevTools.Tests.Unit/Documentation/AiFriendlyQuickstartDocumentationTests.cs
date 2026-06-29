using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AiFriendlyQuickstartDocumentationTests
{
    private const string StableLatestInstallerCommand =
        "irm https://installer.wpf-mcptools.evanlau1798.com | iex";

    [Theory]
    [InlineData("docfx/reference/mcp-contracts.md")]
    [InlineData("docfx/reference/tools/index.md")]
    [InlineData("docfx/zh-tw/reference/mcp-contracts.md")]
    [InlineData("docfx/zh-tw/reference/tools/index.md")]
    [InlineData("docfx/guides/ai-agent-guide.md")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md")]
    public void ContractDocs_ShouldPreferRuntimeNavigationContracts(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("navigation.recommended",
            $"{relativePath} should teach clients to follow runtime navigation guidance before improvising the next tool");
        content.Should().Contain("nextSteps",
            $"{relativePath} should keep the compatibility follow-up field explicit for older clients");
    }

    [Theory]
    [InlineData("docfx/guides/ai-agent-guide.md", "advertised", "get_binding_errors")]
    [InlineData("docfx/reference/tools/binding-and-dp.md", "advertised", "get_binding_errors")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "已經明確公告", "get_binding_errors")]
    [InlineData("docfx/zh-tw/reference/tools/binding-and-dp.md", "已經公告", "get_binding_errors")]
    public void NavigationOptOutGuidance_ShouldPreserveSchemaDiscoverabilityCaveat(
        string relativePath,
        string expectedSnippet,
        string expectedToolName)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("navigation=false");
        content.Should().Contain(expectedToolName);
        content.Should().Contain(expectedSnippet);
    }

    [Theory]
    [InlineData("docfx/quickstart/cursor-vscode.md")]
    [InlineData("docfx/zh-tw/quickstart/cursor-vscode.md")]
    public void CursorEditorQuickstarts_ShouldAvoidRawProtocolFirstWorkflows(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().NotContain("tools/list",
            "editor-facing quickstarts should describe realistic client behavior instead of raw protocol bootstrapping");
        content.Should().Contain("connect",
            "the first useful workflow should still begin with connect in the common case");
    }

    [Theory]
    [InlineData("docfx/quickstart/cursor-vscode.md")]
    [InlineData("docfx/quickstart/claude-desktop.md")]
    [InlineData("docfx/zh-tw/quickstart/cursor-vscode.md")]
    [InlineData("docfx/zh-tw/quickstart/claude-desktop.md")]
    public void JsonArtifactExamples_ShouldUseLiteralAbsolutePathTemplates(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("\"type\": \"stdio\"");
        content.Should().Contain("C:\\\\Users\\\\<you>\\\\AppData\\\\Roaming\\\\WpfDevToolsMcp\\\\<arch>\\\\current\\\\bin\\\\wpf-devtools-<arch>.exe");
        content.Should().NotContain("%APPDATA%\\\\WpfDevToolsMcp\\\\x64\\\\current\\\\bin\\\\wpf-devtools-x64.exe");
    }

    [Fact]
    public void AiGuide_ShouldReferenceOfficialToolDefinitionGuidance()
    {
        var englishGuide = File.ReadAllText(GetRepoFilePath("docfx/guides/ai-agent-guide.md"));
        var traditionalChineseGuide = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/guides/ai-agent-guide.md"));

        englishGuide.Should().Contain("runtime validation");
        englishGuide.Should().Contain("tool descriptions");
        englishGuide.Should().Contain("when to use");
        englishGuide.Should().Contain("when not to use");
        englishGuide.Should().Contain("wpf://contracts/tools");
        englishGuide.Should().Contain("wpf://contracts/response");

        traditionalChineseGuide.Should().Contain("執行期驗證");
        traditionalChineseGuide.Should().Contain("工具描述");
        traditionalChineseGuide.Should().Contain("適用時機");
        traditionalChineseGuide.Should().Contain("不適用時機");
        traditionalChineseGuide.Should().Contain("wpf://contracts/tools");
        traditionalChineseGuide.Should().Contain("wpf://contracts/response");
    }

    [Theory]
    [InlineData("docfx/reference/mcp-contracts.md")]
    [InlineData("docfx/zh-tw/reference/mcp-contracts.md")]
    public void McpContractReference_ShouldCentralizeDiscoveryResourcesPromptsAndNavigation(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("wpf://contracts/tools");
        content.Should().Contain("wpf://contracts/response");
        content.Should().Contain("wpf://capabilities");
        content.Should().Contain("debug_binding_issue");
        content.Should().Contain("navigation.recommended");
        content.Should().Contain("navigation.alternatives");
        content.Should().Contain("prefetchTools");
        content.Should().Contain("contextRefs");
        content.Should().Contain("nextSteps");
        content.Should().Contain("structuredContent");
        content.Should().Contain("get_binding_errors");
        content.Should().Contain("navigation=false");
    }

    [Fact]
    public void PublicInstallerEntrypoints_ShouldPreferReviewedOnlineInstaller_NotRawMasterScript()
    {
        string[] files =
        [
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
            content.Should().NotContain("raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1");
            content.Should().Contain(StableLatestInstallerCommand);
            content.Should().Contain("release_<version>_win-<arch>.zip");
            content.Should().Contain("release-assets.json");
        }
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
