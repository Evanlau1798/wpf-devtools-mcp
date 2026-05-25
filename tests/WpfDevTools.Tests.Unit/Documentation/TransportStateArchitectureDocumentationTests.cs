using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class TransportStateArchitectureDocumentationTests
{
    private const string EnglishAdr = "docfx/architecture/adrs/adr-006-stdio-session-state.md";
    private const string ZhTwAdr = "docfx/zh-tw/architecture/adrs/adr-006-stdio-session-state.md";

    [Theory]
    [InlineData(EnglishAdr, "STDIO single-session boundary")]
    [InlineData(ZhTwAdr, "STDIO 單 session 邊界")]
    public void StdioSessionStateAdr_ShouldDocumentHttpSseMigrationGate(
        string relativePath,
        string title)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(title);
        content.Should().Contain("ToolCallHelper");
        content.Should().Contain("MetricsCollector");
        content.Should().Contain("SessionManager");
        content.Should().Contain("SessionNavigationStateStore");
        content.Should().Contain("ToolNavigationPlanner");
        content.Should().Contain("McpToolExecutionPolicy");
        content.Should().Contain("Streamable HTTP");
        content.Should().Contain("SSE");
        content.Should().Contain("DI/request/session scope");
        content.Should().Contain("release gate");
    }

    [Theory]
    [InlineData("docfx/toc.yml", "architecture/adrs/adr-006-stdio-session-state.md")]
    [InlineData("docfx/zh-tw/toc.yml", "architecture/adrs/adr-006-stdio-session-state.md")]
    [InlineData("docfx/architecture/adrs/index.md", "adr-006-stdio-session-state.md")]
    [InlineData("docfx/zh-tw/architecture/adrs/index.md", "adr-006-stdio-session-state.md")]
    public void StdioSessionStateAdr_ShouldBeListedInDocfxNavigation(
        string relativePath,
        string expectedLink)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedLink);
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
