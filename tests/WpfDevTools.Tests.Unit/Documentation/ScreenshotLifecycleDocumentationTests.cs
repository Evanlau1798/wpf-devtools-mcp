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
        content.Should().Contain("not by the Inspector default screenshot cache",
            $"{relativePath} should not blur server-owned retained resources with Inspector cache cleanup");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
