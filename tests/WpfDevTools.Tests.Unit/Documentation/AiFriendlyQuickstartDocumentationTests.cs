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

        englishGuide.Should().Contain("runtime validation",
            "the AI guide should explain that schemas and annotations are not a substitute for explicit runtime validation");
        englishGuide.Should().Contain("tool descriptions",
            "the AI guide should explain why detailed tool descriptions matter");
        englishGuide.Should().Contain("when to use");
        englishGuide.Should().Contain("when not to use");

        traditionalChineseGuide.Should().Contain("runtime validation",
            "the Traditional Chinese AI guide should preserve the same explicit validation guidance");
        traditionalChineseGuide.Should().Contain("tool descriptions");
        traditionalChineseGuide.Should().Contain("when to use");
        traditionalChineseGuide.Should().Contain("when not to use");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
