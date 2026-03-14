using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class ClaudeToolSearchMetadataTests
{
    private static readonly Assembly McpServerAssembly = typeof(ServerInstructions).Assembly;

    private static readonly Dictionary<string, (string Title, string[] DescriptionKeywords)> AnchorToolExpectations =
        new(StringComparer.Ordinal)
        {
            ["get_processes"] = (
                "List Inspectable WPF Processes",
                ["resolve target ambiguity after connect()", "explicitly need process metadata before connecting"]),
            ["connect"] = (
                "Connect To Running WPF Process",
                ["connect to a running WPF process", "before any inspection tool"]),
            ["get_visual_tree"] = (
                "Inspect WPF Visual Tree",
                ["inspect the runtime visual tree", "running WPF window or element"]),
            ["get_binding_errors"] = (
                "Diagnose WPF Binding Errors",
                ["diagnose WPF binding failures", "blank, stale, or incorrect UI data"]),
            ["get_viewmodel"] = (
                "Inspect WPF ViewModel",
                ["inspect the current WPF ViewModel", "runtime DataContext state"])
        };

    [Fact]
    public void AnchorTools_ShouldExposeSearchOptimizedTitles()
    {
        foreach (var (toolName, expectation) in AnchorToolExpectations)
        {
            var tool = GetTool(toolName);
            tool.Attr.Title.Should().Be(expectation.Title,
                $"anchor tool '{toolName}' should use a domain-explicit title for Claude Tool Search");
        }
    }

    [Fact]
    public void AnchorTools_ShouldFrontLoadRuntimeTaskLanguage_InDescriptions()
    {
        foreach (var (toolName, expectation) in AnchorToolExpectations)
        {
            var tool = GetTool(toolName);
            var description = tool.Method.GetCustomAttribute<DescriptionAttribute>();

            description.Should().NotBeNull();
            var opening = GetOpeningParagraph(description!.Description);

            foreach (var keyword in expectation.DescriptionKeywords)
            {
                opening.Should().Contain(keyword,
                    $"anchor tool '{toolName}' should front-load '{keyword}' for Claude Tool Search");
            }
        }
    }

    [Fact]
    public void SearchFacingDescriptions_ShouldLeadWithTaskLanguage_NotCategoryBanner()
    {
        foreach (var (_, method, attr) in GetAllTools())
        {
            var description = method.GetCustomAttribute<DescriptionAttribute>();
            description.Should().NotBeNull();

            var opening = GetOpeningParagraph(description!.Description);
            opening.Should().NotStartWith("CATEGORY:",
                $"tool '{attr.Name}' should begin with task language rather than metadata banners");
        }
    }

    [Fact]
    public void MostToolTitles_ShouldContainDomainSpecificKeywords_ForToolSearchRanking()
    {
        var keywords = new[]
        {
            "WPF", "Binding", "DependencyProperty", "ViewModel", "Visual Tree", "Logical Tree",
            "XAML", "Window", "Focus", "Layout", "Style", "Template", "State", "Routed Event", "Render"
        };

        var qualifyingTitles = GetAllTools()
            .Select(tool => tool.Attr.Title ?? string.Empty)
            .Count(title => keywords.Any(keyword => title.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

        qualifyingTitles.Should().BeGreaterThanOrEqualTo(40,
            "Claude Tool Search needs most titles to carry explicit WPF/runtime domain cues");
    }

    [Fact]
    public void ServerInstructions_ShouldLeadWithClaudeSearchRoutingGuidance()
    {
        var opening = ServerInstructions.Value[..Math.Min(ServerInstructions.Value.Length, 1500)];

        opening.Should().Contain("Search this server when",
            "Claude Code relies on early server instructions to decide when to search this server");
        opening.Should().Contain("running WPF application");
        opening.Should().Contain("visual tree");
        opening.Should().Contain("binding");
        opening.Should().Contain("ViewModel");
        opening.Should().Contain("secondary windows");
    }

    [Fact]
    public void ServerInstructions_ShouldDescribeSessionAwareNavigationHints()
    {
        ServerInstructions.Value.Should().Contain("session-aware");
        ServerInstructions.Value.Should().Contain("preconditions");
        ServerInstructions.Value.Should().Contain("expectedOutcome");
        ServerInstructions.Value.Should().Contain("workflowId");
        ServerInstructions.Value.Should().Contain("prefetchTools");
        ServerInstructions.Value.Should().Contain("advisory");
    }

    private static (Type Type, MethodInfo Method, McpServerToolAttribute Attr) GetTool(string toolName) =>
        GetAllTools().Single(tool => tool.Attr.Name == toolName);

    private static IEnumerable<(Type Type, MethodInfo Method, McpServerToolAttribute Attr)> GetAllTools()
    {
        var toolTypes = McpServerAssembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null);

        foreach (var type in toolTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute != null)
                {
                    yield return (type, method, attribute);
                }
            }
        }
    }

    private static string GetOpeningParagraph(string description)
    {
        var separator = description.IndexOf("\n\n", StringComparison.Ordinal);
        return separator >= 0
            ? description[..separator]
            : description;
    }
}
