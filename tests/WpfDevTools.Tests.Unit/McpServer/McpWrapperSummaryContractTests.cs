using System.Text.RegularExpressions;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpWrapperSummaryContractTests
{
    private static readonly Regex StaleToolCountPattern =
        new(@"///\s.*\(\d+\s+tools?\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void WrapperSummaries_ShouldNotContainHardcodedToolCounts()
    {
        var mcpToolsDirectory = TestRepositoryPaths.GetRepoFilePath("src/WpfDevTools.Mcp.Server/McpTools");
        var matches = Directory.EnumerateFiles(mcpToolsDirectory, "*.cs")
            .SelectMany(FindStaleToolCountComments)
            .ToArray();

        matches.Should().BeEmpty(
            "wrapper XML summaries should avoid hardcoded tool counts that drift when tools are added or removed");
    }

    private static IEnumerable<string> FindStaleToolCountComments(string path)
    {
        return File.ReadLines(path)
            .Select((line, index) => new { Line = line, LineNumber = index + 1 })
            .Where(item => StaleToolCountPattern.IsMatch(item.Line))
            .Select(item => $"{Path.GetFileName(path)}:{item.LineNumber}: {item.Line.Trim()}");
    }
}
