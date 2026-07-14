using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class RoutedEventDurationRetryDocumentationTests
{
    [Theory]
    [InlineData("docfx/reference/tools/interaction-events-layout.md")]
    [InlineData("docfx/zh-tw/reference/tools/interaction-events-layout.md")]
    [InlineData("src/WpfDevTools.Mcp.Server/McpTools/EventMcpToolDescriptions.cs")]
    public void RoutedEventGuidance_ShouldDocumentExactShortDurationRetry(string path)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(path));

        content.Should().ContainAll(
            "requestedDuration",
            "effectiveDuration",
            "allowShortStartDuration=true",
            "nextSteps",
            "durationMs");
    }
}
