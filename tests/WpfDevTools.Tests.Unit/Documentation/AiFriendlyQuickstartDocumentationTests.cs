using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AiFriendlyQuickstartDocumentationTests
{
    [Fact]
    public void ClientQuickstarts_ShouldPreferRuntimeNavigationContracts()
    {
        var files = new[]
        {
            "README.md",
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/quickstart/claude-desktop.md",
            "docfx/quickstart/cursor-vscode.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/claude-code.md",
            "docfx/zh-tw/quickstart/openai-codex.md",
            "docfx/zh-tw/quickstart/claude-desktop.md",
            "docfx/zh-tw/quickstart/cursor-vscode.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("navigation.recommended",
                $"{file} should teach clients to follow runtime navigation guidance before improvising the next tool");
            content.Should().Contain("nextSteps",
                $"{file} should keep the compatibility follow-up field explicit for older clients");
        }
    }

    [Theory]
    [InlineData("docfx/guides/ai-agent-guide.md", "advertised", "get_binding_errors")]
    [InlineData("docfx/quickstart/claude-code.md", "advertised", "get_binding_errors")]
    [InlineData("docfx/quickstart/openai-codex.md", "advertised", "get_binding_errors")]
    [InlineData("docfx/reference/tools/binding-and-dp.md", "advertised", "get_binding_errors")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "已經明確公告", "get_binding_errors")]
    [InlineData("docfx/zh-tw/quickstart/claude-code.md", "已經公告", "get_binding_errors")]
    [InlineData("docfx/zh-tw/quickstart/openai-codex.md", "已經公告", "get_binding_errors")]
    [InlineData("docfx/zh-tw/reference/tools/binding-and-dp.md", "已經公告", "get_binding_errors")]
    public void NavigationOptOutGuidance_ShouldPreserveSchemaDiscoverabilityCaveat(string relativePath, string expectedSnippet, string expectedToolName)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("navigation=false",
            $"{relativePath} should keep the explicit opt-out syntax visible to AI agents");
        content.Should().Contain(expectedToolName,
            $"{relativePath} should scope the explicit opt-out to the tool that actually advertises it today");
        content.Should().Contain(expectedSnippet,
            $"{relativePath} should explain that this opt-out is available because the current tool schema explicitly advertises it");
    }

    [Theory]
    [InlineData("docfx/quickstart/cursor-vscode.md")]
    [InlineData("docfx/zh-tw/quickstart/cursor-vscode.md")]
    public void CursorEditorQuickstarts_ShouldAvoidRawProtocolFirstWorkflows(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().NotContain("tools/list",
            "editor-facing quickstarts should describe realistic client behavior instead of raw protocol bootstrapping");
        content.Should().Contain("connect()",
            "the first useful workflow should still begin with connect() in the common case");
    }

    [Theory]
    [InlineData("docfx/quickstart/cursor-vscode.md")]
    [InlineData("docfx/quickstart/claude-desktop.md")]
    [InlineData("docfx/zh-tw/quickstart/cursor-vscode.md")]
    [InlineData("docfx/zh-tw/quickstart/claude-desktop.md")]
    public void JsonArtifactExamples_ShouldUseLiteralAbsolutePathTemplates(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("\"type\": \"stdio\"",
            $"{relativePath} should keep the published JSON aligned with the installed stdio artifact shape");
        content.Should().Contain("C:\\\\Users\\\\<you>\\\\AppData\\\\Roaming\\\\WpfDevToolsMcp\\\\<arch>\\\\current\\\\bin\\\\wpf-devtools-<arch>.exe",
            $"{relativePath} should demonstrate the reviewed absolute path template rather than a default-root-specific example");
        content.Should().NotContain("%APPDATA%\\\\WpfDevToolsMcp\\\\x64\\\\current\\\\bin\\\\wpf-devtools-x64.exe",
            $"{relativePath} should not hard-code the default root and x64 into the published JSON artifact examples");
    }

    [Fact]
    public void ReadmeAndAiGuide_ShouldReferenceOfficialToolDefinitionGuidance()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));
        var englishGuide = File.ReadAllText(GetRepoFilePath("docfx/guides/ai-agent-guide.md"));
        var traditionalChineseGuide = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/guides/ai-agent-guide.md"));

        readme.Should().Contain("modelcontextprotocol.io/docs/develop/build-server",
            "README should point maintainers at the official MCP build-server guidance");
        readme.Should().Contain("csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.html",
            "README should point maintainers at the official C# SDK API surface");
        readme.Should().Contain("platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools",
            "README should point maintainers at Anthropic's current tool-definition guidance");
        readme.Should().Contain("anthropic.com/engineering/writing-tools-for-agents",
            "README should point maintainers at Anthropic's detailed tool-design guidance");
        readme.Should().Contain("anthropic.com/engineering/advanced-tool-use",
            "README should include the exact advanced tool-use article requested by the production review");
        readme.Should().Contain("progressive discovery");
        readme.Should().Contain("wpf://contracts/tools");
        readme.Should().Contain("tool examples");

        englishGuide.Should().Contain("runtime validation",
            "the AI guide should explain that schemas and annotations are not a substitute for explicit runtime validation");
        englishGuide.Should().Contain("tool descriptions",
            "the AI guide should explain why detailed tool descriptions matter");
        englishGuide.Should().Contain("when to use");
        englishGuide.Should().Contain("when not to use");

        traditionalChineseGuide.Should().Contain("執行期驗證",
            "the Traditional Chinese AI guide should preserve the same explicit validation guidance with localized wording");
        traditionalChineseGuide.Should().Contain("工具描述");
        traditionalChineseGuide.Should().Contain("適用時機");
        traditionalChineseGuide.Should().Contain("不適用時機");
    }

    [Theory]
    [InlineData(
        "docfx/guides/ai-agent-guide.md",
        "## Prompt patterns that work well",
        "## Anti-patterns",
        "## Golden sequence for automation",
        "This keeps failures")]
    [InlineData(
        "docfx/zh-tw/guides/ai-agent-guide.md",
        "## 容易成功的提示模式",
        "## 常見反模式",
        "## 自動化的黃金順序",
        "這能讓")]
    public void AiGuideSensitiveReadWorkflows_ShouldDeclareSensitiveReadGate(
        string relativePath,
        string promptSectionStart,
        string promptSectionEnd,
        string goldenSectionStart,
        string goldenSectionEnd)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var promptSection = GetSection(content, promptSectionStart, promptSectionEnd);
        var goldenSection = GetSection(content, goldenSectionStart, goldenSectionEnd);

        promptSection.Split("WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true").Length.Should().BeGreaterThanOrEqualTo(5,
            $"{relativePath} prompt examples use scene, binding, form, state, or runtime-event reads and should state the sensitive-read gate in each example");
        goldenSection.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true",
            $"{relativePath} automation sequence should state the sensitive-read gate before read-heavy scene diagnostics");
    }

    [Fact]
    public void PublicInstallerEntrypoints_ShouldPreferReviewedOnlineInstaller_NotRawMasterScript()
    {
        var files = new[]
        {
            "docfx/index.md",
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/production/deployment.md",
            "docfx/zh-tw/index.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/production/deployment.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));

            content.Should().NotContain("raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/online-installer.ps1",
                $"{file} should not tell users to execute the moving master branch installer directly");
            content.Should().Contain("irm https://wpf-mcptools.evanlau1798.com | iex",
                $"{file} should publish the reviewed HTTPS installer alias for release-candidate docs");
            content.Should().Contain("GitHub Release assets",
                $"{file} should gate the public installer command on uploaded release assets");
            content.Should().Contain("-PackageArchivePath",
                $"{file} should point readers at the local package installer path while public endpoints are unavailable");
            content.Should().Contain("integrity",
                $"{file} should explain that the reviewed installer validates the release archive before extraction");
            content.Should().Contain("packaged payload",
                $"{file} should explain that the reviewed installer uses the extracted packaged payload from the resolved release");
        }
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string GetSection(string content, string startMarker, string endMarker)
    {
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"section marker '{startMarker}' should exist");

        var end = content.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"section marker '{endMarker}' should exist after '{startMarker}'");

        return content[start..end];
    }
}
