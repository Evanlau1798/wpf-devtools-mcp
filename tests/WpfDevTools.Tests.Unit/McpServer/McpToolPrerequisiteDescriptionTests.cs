using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolPrerequisiteDescriptionTests
{
    private const string ConnectPrerequisite =
        "PREREQUISITE: connect() or connect(processId) must have succeeded for the target process.";

    private static readonly HashSet<string> ToolsThatDoNotRequireConnectedInspector = new(StringComparer.Ordinal)
    {
        "get_processes",
        "get_active_process",
        "connect",
        "list_ui_block_packs",
        "get_ui_block_catalog",
        "compose_ui_blueprint",
        "validate_ui_blueprint",
        "expand_ui_recipe",
        "render_ui_blueprint",
        "repair_ui_blueprint",
        "apply_ui_blueprint",
        "preview_ui_blueprint"
    };

    [Fact]
    public void ToolsRequiringConnectedInspector_ShouldUseStandardPrerequisiteDescription()
    {
        var missingPrerequisites = GetMcpTools()
            .Where(tool => !ToolsThatDoNotRequireConnectedInspector.Contains(tool.Name))
            .Where(tool => !tool.Description.Contains(ConnectPrerequisite, StringComparison.Ordinal))
            .Select(tool => tool.Name)
            .ToArray();

        missingPrerequisites.Should().BeEmpty(
            "AI clients need a consistent, greppable prerequisite marker for tools that require a connected inspector session");
    }

    [Fact]
    public void ApplyUiBlueprintDescription_ShouldNameDestructiveAndProjectWriteGates()
    {
        var description = GetMcpTools()
            .Single(tool => string.Equals(tool.Name, "apply_ui_blueprint", StringComparison.Ordinal))
            .Description;

        description.Should().Contain(McpServerConfiguration.AllowDestructiveToolsEnvVar);
        description.Should().Contain(McpServerConfiguration.AllowProjectWritesEnvVar);
        description.Should().Contain(McpServerConfiguration.AllowedProjectRootsEnvVar);
        description.Should().Contain("Non-dry-run writes");
        description.Should().Contain("executed file plan");
        description.Should().Contain("pre-write");
    }

    [Fact]
    public void PreviewUiBlueprintDescription_ShouldDiscloseStructuralOnlyVisualFidelity()
    {
        var description = GetMcpTools()
            .Single(tool => string.Equals(tool.Name, "preview_ui_blueprint", StringComparison.Ordinal))
            .Description;

        description.Should().Contain("structural-only");
        description.Should().Contain("applied, built, and launched WPF application");
        description.Should().Contain("visualComparisonChecklist");
        description.Should().Contain("window chrome");
        description.Should().Contain("icons");
        description.Should().Contain("layout and spacing");
        description.Should().Contain("propertyWarnings");
        description.Should().Contain("exact blueprint JSON path");
    }

    [Fact]
    public void GetUiBlockCatalogDescription_ShouldExplainPackDefinedAuthoringHints()
    {
        var description = GetMcpTools()
            .Single(tool => string.Equals(tool.Name, "get_ui_block_catalog", StringComparison.Ordinal))
            .Description;

        description.Should().Contain("description");
        description.Should().Contain("previewWarning");
        description.Should().Contain("pack-defined");
        description.Should().Contain("compositionSkeleton");
        description.Should().Contain("pack-neutral");
    }

    [Fact]
    public void ValidateUiBlueprintDescription_ShouldExplainSizeBudget()
    {
        var description = GetMcpTools()
            .Single(tool => string.Equals(tool.Name, "validate_ui_blueprint", StringComparison.Ordinal))
            .Description;

        description.Should().Contain("blueprintSize");
        description.Should().Contain("remainingCharacters");
    }

    private static IEnumerable<(string Name, string Description)> GetMcpTools()
    {
        var assembly = typeof(WpfDevTools.Mcp.Server.ServerInstructions).Assembly;
        var toolTypes = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null);

        foreach (var type in toolTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var tool = method.GetCustomAttribute<McpServerToolAttribute>();
                if (tool == null)
                {
                    continue;
                }

                yield return (
                    tool.Name!,
                    method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty);
            }
        }
    }
}
