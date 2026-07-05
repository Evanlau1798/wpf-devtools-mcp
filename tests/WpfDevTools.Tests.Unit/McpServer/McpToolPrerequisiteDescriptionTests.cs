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
        "validate_ui_blueprint",
        "expand_ui_recipe",
        "render_ui_blueprint",
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
