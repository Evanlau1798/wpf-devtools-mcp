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
            ["Scene Diagnostics"] = "State & Scene Diagnostics",
            ["UI Composer"] = "UI Composer"
        };

    [Fact]
    public void DocfxToolCategorySummary_ShouldMatchCurrentMcpSurface()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));
        var toolReference = File.ReadAllText(GetRepoFilePath("docfx/reference/tools/index.md"));
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

        readme.Should().Contain($"{totalCount} MCP tools");
        readme.Should().NotContain("| Process Management | 5 |");
        toolReference.Should().Contain($"The server currently exposes {totalCount} tools across twelve categories.");

        foreach (var category in toolCounts)
        {
            toolReference.Should().Contain(category.Key,
                $"DocFX should stay synchronized with the {category.Key} tool category");
        }
    }

    [Fact]
    public void DocfxStructuredContentSummary_ShouldMatchCurrentWrapperBehavior()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));
        var toolReference = File.ReadAllText(GetRepoFilePath("docfx/reference/tools/index.md"));
        var helper = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/McpTools/ToolCallHelper.cs"));
        var textFallbackHelper = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/McpTools/ToolCallHelper.TextFallback.cs"));

        helper.Should().Contain("StructuredContent = jsonElement");
        textFallbackHelper.Should().Contain("AppendHighSignalFallbackFields");
        textFallbackHelper.Should().Contain("Annotations = isError ? ErrorAnnotations : null");
        textFallbackHelper.Should().Contain("hasStructuredContent");
        readme.Should().Contain("reference/tools/");
        toolReference.Should().Contain("result.structuredContent");
        toolReference.Should().Contain("compact JSON fallback");
        toolReference.Should().Contain("WPFDEVTOOLS_TEXT_FALLBACK_MODE=full");
        toolReference.Should().Contain("result.content[0].annotations");
        toolReference.Should().NotContain("StructuredContent / structuredContent");
        toolReference.Should().NotContain("error results include `Annotations`");
        readme.Should().NotContain("Structured content: `StructuredContent` and `Annotations` populated on all tool results");
    }

    [Fact]
    public void Readme_ShouldSeparateToolInputSchemasFromResponseContracts()
    {
        var readme = File.ReadAllText(GetRepoFilePath("README.md"));
        var toolReference = File.ReadAllText(GetRepoFilePath("docfx/reference/tools/index.md"));

        readme.Should().Contain("DocFX tool reference");
        toolReference.Should().Contain("tools/list");
        toolReference.Should().Contain("wpf://contracts/response");
        toolReference.Should().Contain("structuredContent");
        toolReference.Should().Contain("navigation");
        toolReference.Should().Contain("nextSteps");
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
