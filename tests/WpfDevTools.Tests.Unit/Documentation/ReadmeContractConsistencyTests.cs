using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ReadmeContractConsistencyTests
{
    private static readonly (string Category, string FileName)[] CategoryFiles =
    [
        ("Process Management", "ProcessMcpTools.cs"),
        ("Tree & XAML", "TreeMcpTools.cs"),
        ("Binding Diagnostics", "BindingMcpTools.cs"),
        ("DependencyProperty", "DependencyPropertyMcpTools.cs"),
        ("Style/Template", "StyleMcpTools.cs"),
        ("RoutedEvent", "EventMcpTools.cs"),
        ("Interaction", "InteractionMcpTools.cs"),
        ("Layout", "LayoutMcpTools.cs"),
        ("MVVM", "MvvmMcpTools.cs"),
        ("Performance", "PerformanceMcpTools.cs"),
        ("State & Scene Diagnostics", "StateMcpTools.cs"),
        ("State & Scene Diagnostics", "SceneDiagnosticsMcpTools.cs")
    ];

    [Fact]
    public void Readme_ToolCategorySummary_ShouldMatchCurrentMcpSurface()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));
        var toolCounts = CategoryFiles
            .GroupBy(entry => entry.Category)
            .Select(group => new
            {
                group.Key,
                Count = group.Sum(entry => CountTools(entry.FileName))
            })
            .ToArray();
        var totalCount = toolCounts.Sum(entry => entry.Count);

        readme.Should().Contain($"The server ships {totalCount} MCP tools across {toolCounts.Length} categories.");

        foreach (var category in toolCounts)
        {
            readme.Should().Contain($"| {category.Key} | {category.Count} |",
                $"README should stay synchronized with {category.Key} tool count");
        }
    }

    [Fact]
    public void Readme_StructuredContentSummary_ShouldMatchCurrentWrapperBehavior()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));
        var helper = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/McpTools/ToolCallHelper.cs"));

        helper.Should().Contain("Annotations = isError ? ErrorAnnotations : null");
        readme.Should().Contain("Structured content: `StructuredContent` is populated on all tool results, and error results include `Annotations`.");
        readme.Should().NotContain("Structured content: `StructuredContent` and `Annotations` populated on all tool results");
    }

    private static int CountTools(string fileName)
    {
        var content = File.ReadAllText(GetRepoFilePath(Path.Combine("src", "WpfDevTools.Mcp.Server", "McpTools", fileName)));
        return CountOccurrences(content, "[McpServerTool(");
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var startIndex = 0;

        while ((startIndex = content.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
