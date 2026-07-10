using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ScreenshotLifecycleDocumentationTests
{
    [Theory]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void SecurityDocumentation_ShouldSeparateServerOwnedRetainedResourcesFromInspectorCache(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("MCP server-owned retained screenshot resources",
            $"{relativePath} should name the owner of file-mode MCP screenshot resources");
        content.Should().Contain("server-issued lease root",
            $"{relativePath} should describe where MCP file-mode screenshots are written");
        content.Should().Contain("Inspector default screenshot cache",
            $"{relativePath} should describe the separate non-MCP Inspector cache");
        content.Should().Contain("WPFDEVTOOLS_SCREENSHOT_DIR",
            $"{relativePath} should tie the Inspector cache override to the Inspector-owned path only");
        content.Should().Contain("preview_ui_blueprint");
        content.Should().Contain("screenshotOutputMode");
        content.Should().Contain("resources/read");
    }

    [Theory]
    [InlineData("docfx/reference/tools/interaction-events-layout.md")]
    [InlineData("docfx/zh-tw/reference/tools/interaction-events-layout.md")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpTools/InteractionMcpToolDescriptions.cs")]
    public void ElementScreenshotDocumentation_ShouldDescribeServerOwnedFileModeLifecycle(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("MCP server-owned retained screenshot resource",
            $"{relativePath} should identify file-mode output as server-owned");
        content.Should().Contain("server-issued lease root",
            $"{relativePath} should describe the storage boundary used by file mode");
        content.Should().Contain("SessionManager",
            $"{relativePath} should describe the component that expires and purges retained resources");
        content.Should().Contain("resourceRead",
            $"{relativePath} should expose the exact same-session resource read request");
        content.Should().Contain("not by the Inspector default screenshot cache",
            $"{relativePath} should not blur server-owned retained resources with Inspector cache cleanup");
    }

    [Theory]
    [InlineData(
        "docfx/reference/tools/interaction-events-layout.md",
        "metadata mode does not return `screenshotId`, `resourceUri`, or a `wpf://screenshots/{screenshotId}` handle")]
    [InlineData(
        "docfx/zh-tw/reference/tools/interaction-events-layout.md",
        "metadata mode 不會回傳 `screenshotId`、`resourceUri` 或 `wpf://screenshots/{screenshotId}` handle")]
    [InlineData(
        "src/WpfDevTools.Mcp.Server/McpTools/InteractionMcpToolDescriptions.cs",
        "metadata mode does not return `screenshotId`, `resourceUri`, or a `wpf://screenshots/{screenshotId}` handle")]
    public void ElementScreenshotDocumentation_ShouldClarifyMetadataModeHasNoResourceHandle(
        string relativePath,
        string expectedPhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedPhrase);
        content.Should().Contain("outputMode");
        content.Should().Contain("file");
    }

    [Theory]
    [InlineData("docfx/reference/tools/interaction-events-layout.md")]
    [InlineData("docfx/zh-tw/reference/tools/interaction-events-layout.md")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpTools/InteractionMcpToolDescriptions.cs")]
    public void ElementScreenshotDocumentation_ShouldRejectAgentSelectedOutputPath(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("outputPath");
        content.Should().Contain("resourceUri");
    }

    [Fact]
    public void ToolPages_ShouldDocumentPreviewScreenshotAndAssignableTypeOptions()
    {
        var expectations = new Dictionary<string, string[]>
        {
            ["docfx/reference/tools/ui-composer.md"] = ["screenshotOutputMode", "resources/read", "same MCP server session"],
            ["docfx/zh-tw/reference/tools/ui-composer.md"] = ["screenshotOutputMode", "resources/read", "相同 MCP server session"],
            ["docfx/reference/tools/tree-and-xaml.md"] = ["typeMatchMode", "assignable", "matchMode"],
            ["docfx/zh-tw/reference/tools/tree-and-xaml.md"] = ["typeMatchMode", "assignable", "matchMode"]
        };

        foreach (var (relativePath, requiredTerms) in expectations)
        {
            var content = File.ReadAllText(GetRepoFilePath(relativePath));
            foreach (var requiredTerm in requiredTerms)
            {
                content.Should().Contain(requiredTerm, $"{relativePath} should document {requiredTerm}");
            }
        }
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
