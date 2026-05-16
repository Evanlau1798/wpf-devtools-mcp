using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PublicQuickstartInstallerAutomationDocumentationTests
{
    [Fact]
    public void InstallerAutomationExamples_ShouldUseNonInteractiveAndOutputJson()
    {
        var files = new[]
        {
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
            content.Should().Contain("-NonInteractive",
                $"{file} should show explicit non-interactive installer usage for automation-safe examples");
            content.Should().Contain("-OutputJson",
                $"{file} should show machine-readable installer output for automation-safe examples");
        }
    }

    [Fact]
    public void InstallerExamples_ShouldNotPresentX64AsTheDefaultArchitecture()
    {
        var files = new[]
        {
            "docfx/index.md",
            "docfx/quickstart/index.md",
            "docfx/quickstart/ai-agent-clients.md",
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/claude-desktop.md",
            "docfx/quickstart/cursor-vscode.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/production/deployment.md",
            "docfx/zh-tw/index.md",
            "docfx/zh-tw/quickstart/index.md",
            "docfx/zh-tw/quickstart/ai-agent-clients.md",
            "docfx/zh-tw/quickstart/claude-code.md",
            "docfx/zh-tw/quickstart/claude-desktop.md",
            "docfx/zh-tw/quickstart/cursor-vscode.md",
            "docfx/zh-tw/quickstart/openai-codex.md",
            "docfx/zh-tw/production/deployment.md"
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().NotContain("-Version latest -Architecture x64",
                $"{file} should not imply that x64 is the installer default; omitted architecture is auto-detected");
        }
    }

    [Theory]
    [InlineData("README.md", "omit `-Architecture`", "detects the system architecture")]
    [InlineData("docfx/index.md", "omit `-Architecture`", "detects the system architecture")]
    [InlineData("docfx/quickstart/index.md", "omit `-Architecture`", "detects the system architecture")]
    [InlineData("docfx/production/deployment.md", "omit `-Architecture`", "detects the system architecture")]
    [InlineData("docfx/zh-tw/index.md", "省略 `-Architecture`", "偵測系統架構")]
    [InlineData("docfx/zh-tw/quickstart/index.md", "省略 `-Architecture`", "偵測系統架構")]
    [InlineData("docfx/zh-tw/production/deployment.md", "省略 `-Architecture`", "偵測系統架構")]
    public void InstallerOverviewDocs_ShouldDocumentArchitectureAutoDetection(
        string relativePath,
        string omissionText,
        string detectionText)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(omissionText);
        content.Should().Contain(detectionText);
    }

    [Fact]
    public void CliQuickstarts_ShouldDescribeFallbackExecutablePath_NotFixedDefaultRoot()
    {
        File.ReadAllText(GetRepoFilePath("docfx/quickstart/claude-code.md"))
            .Should().NotContain("default executable path",
                "Claude Code quickstart should describe the AppData path as a fallback, not a fixed resolved install root");
        File.ReadAllText(GetRepoFilePath("docfx/quickstart/openai-codex.md"))
            .Should().NotContain("default executable path",
                "Codex quickstart should describe the AppData path as a fallback, not a fixed resolved install root");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/quickstart/claude-code.md"))
            .Should().NotContain("預設 executable 路徑",
                "Traditional Chinese Claude quickstart should describe the AppData path as a fallback, not a fixed resolved install root");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/quickstart/openai-codex.md"))
            .Should().NotContain("預設 executable 路徑",
                "Traditional Chinese Codex quickstart should describe the AppData path as a fallback, not a fixed resolved install root");
    }

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
