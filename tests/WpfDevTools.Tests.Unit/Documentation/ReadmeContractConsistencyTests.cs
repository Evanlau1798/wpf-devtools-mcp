using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ReadmeContractConsistencyTests
{
    private static readonly Assembly McpServerAssembly =
        typeof(WpfDevTools.Mcp.Server.ServerInstructions).Assembly;

    private static readonly IReadOnlyDictionary<string, string> ReadmeCategoryMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Process"] = "Process Management",
            ["Tree"] = "Tree & XAML",
            ["Binding"] = "Binding Diagnostics",
            ["DependencyProperty"] = "DependencyProperty",
            ["Style"] = "Style/Template",
            ["Event"] = "RoutedEvent",
            ["Interaction"] = "Interaction",
            ["Layout"] = "Layout",
            ["MVVM"] = "MVVM",
            ["Performance"] = "Performance",
            ["State"] = "State & Scene Diagnostics",
            ["Scene Diagnostics"] = "State & Scene Diagnostics"
        };

    [Fact]
    public void Readme_ToolCategorySummary_ShouldMatchCurrentMcpSurface()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));
        var toolCounts = GetToolCategories()
            .GroupBy(entry => entry.Category)
            .Select(group => new
            {
                group.Key,
                Count = group.Count()
            })
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
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
        var textFallbackHelper = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/McpTools/ToolCallHelper.TextFallback.cs"));

        helper.Should().Contain("StructuredContent = jsonElement");
        textFallbackHelper.Should().Contain("AppendHighSignalFallbackFields");
        textFallbackHelper.Should().Contain("Annotations = isError ? ErrorAnnotations : null");
        textFallbackHelper.Should().Contain("hasStructuredContent");
        readme.Should().Contain("Structured content: `StructuredContent` is populated on all tool results; object/array `Content.Text` preserves high-signal top-level scalar fields and collection counts as a compact fallback summary when structured payload is present, and error results include `Annotations`.");
        readme.Should().NotContain("Structured content: `StructuredContent` and `Annotations` populated on all tool results");
    }

    [Fact]
    public void Readme_ShouldSeparateToolInputSchemasFromResponseContracts()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));

        readme.Should().Contain("Use MCP tool discovery for input schemas");
        readme.Should().Contain("Use `wpf://contracts/response` for machine-readable response contracts");
        readme.Should().Contain("structuredContent");
        readme.Should().Contain("navigation");
        readme.Should().Contain("nextSteps");
        readme.Should().NotContain("Use MCP tool discovery for full schemas.");
    }

    private static IReadOnlyList<(string ToolName, string Category)> GetToolCategories()
    {
        var tools = new List<(string ToolName, string Category)>();

        foreach (var type in McpServerAssembly.GetTypes()
                     .Where(candidate => candidate.GetCustomAttribute<McpServerToolTypeAttribute>() != null))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttribute == null)
                {
                    continue;
                }

                var descriptionAttribute = method.GetCustomAttribute<DescriptionAttribute>();
                descriptionAttribute.Should().NotBeNull(
                    $"tool '{toolAttribute.Name}' should expose a [Description] attribute");
                toolAttribute.Name.Should().NotBeNullOrWhiteSpace(
                    "all MCP tool attributes should provide a stable tool name");

                var codeCategory = ExtractCodeCategory(descriptionAttribute!.Description);
                ReadmeCategoryMap.TryGetValue(codeCategory, out var readmeCategory).Should().BeTrue(
                    $"tool '{toolAttribute.Name}' should map code category '{codeCategory}' to a README category");

                tools.Add((toolAttribute.Name!, readmeCategory!));
            }
        }

        return tools;
    }

    private static string ExtractCodeCategory(string description)
    {
        const string prefix = "CATEGORY: ";
        var start = description.IndexOf(prefix, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "tool descriptions should include a CATEGORY header");

        var categoryStart = start + prefix.Length;
        var categoryEnd = description.IndexOf("\n", categoryStart, StringComparison.Ordinal);
        if (categoryEnd < 0) categoryEnd = description.Length;

        return description.Substring(categoryStart, categoryEnd - categoryStart).Trim();
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
