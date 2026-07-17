using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolPrerequisiteDescriptionTests
{
    private const string ConnectPrerequisite =
        "PREREQUISITE: connect() selected target.";

    private static readonly HashSet<string> ToolsThatDoNotRequireConnectedInspector = new(StringComparer.Ordinal)
    {
        "get_processes",
        "get_active_process",
        "connect",
        "list_ui_block_packs",
        "get_ui_block_catalog",
        "create_ui_blueprint_draft",
        "patch_ui_blueprint_draft",
        "compose_ui_blueprint",
        "import_ui_block_pack",
        "validate_ui_blueprint",
        "expand_ui_recipe",
        "render_ui_blueprint",
        "repair_ui_blueprint",
        "apply_ui_blueprint",
        "apply_ui_project_integration",
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
        description.Should().Contain("packageIntegrationGuidance");
        description.Should().Contain("ManagePackageVersionsCentrally");
        description.Should().Contain("inspectionConfidence");
        description.Should().Contain("inspectedFiles");
        description.Should().Contain("inspectionLimitations");
        description.Should().Contain("static XML");
        description.Should().Contain("mode=unknown");
        description.Should().Contain("omits package snippets");
        description.Should().Contain("does not edit project or central package files");
    }

    [Fact]
    public void ApplyUiProjectIntegrationDescription_ShouldNameReviewAndRollbackContract()
    {
        var description = GetMcpTools()
            .Single(tool => string.Equals(tool.Name, "apply_ui_project_integration", StringComparison.Ordinal))
            .Description;

        description.Should().ContainAll(
            McpServerConfiguration.AllowDestructiveToolsEnvVar,
            McpServerConfiguration.AllowProjectWritesEnvVar,
            McpServerConfiguration.AllowedProjectRootsEnvVar,
            "reviewedPlanHash",
            "IntegrationPlanChanged",
            "backupPath",
            "rollbackAction",
            "pack-neutral");
    }

    [Fact]
    public void ListUiBlockPacksDescription_ShouldExplainPackNeutralBlueprintHints()
    {
        var description = GetMcpTools()
            .Single(tool => string.Equals(tool.Name, "list_ui_block_packs", StringComparison.Ordinal))
            .Description;

        description.Should().ContainAll("kind", "themeTokens", "suggested blueprint role", "required=true");
    }

    [Fact]
    public void ComposerToolDescriptions_ShouldLinkCanonicalExamples()
    {
        var composerTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "list_ui_block_packs", "import_ui_block_pack", "get_ui_block_catalog", "compose_ui_blueprint",
            "validate_ui_blueprint", "expand_ui_recipe", "render_ui_blueprint", "preview_ui_blueprint",
            "repair_ui_blueprint", "apply_ui_blueprint"
        };

        var descriptions = GetMcpTools().Where(tool => composerTools.Contains(tool.Name)).ToArray();
        descriptions.Single(tool => tool.Name == "list_ui_block_packs").Description.Should()
            .Contain("https://wpf-mcptools.evanlau1798.com/reference/tools/ui-composer.html");
        descriptions.Where(tool => tool.Name != "list_ui_block_packs").Should().OnlyContain(tool =>
            tool.Description.Contains("See list_ui_block_packs.", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewUiBlueprintDescription_ShouldDiscloseResourceBackedVisualFidelity()
    {
        var description = GetMcpTools()
            .Single(tool => string.Equals(tool.Name, "preview_ui_blueprint", StringComparison.Ordinal))
            .Description;

        description.Should().Contain("resource-backed");
        description.Should().Contain("resource-backed, hybrid-resource-backed, structural, or not-available");
        description.Should().Contain("hash-checked before build");
        description.Should().Contain(McpServerConfiguration.ComposerTrustedRuntimePacksEnvVar);
        description.Should().Contain(McpServerConfiguration.AllowComposerRuntimeApprovalsEnvVar);
        description.Should().Contain("content-bound approval token");
        description.Should().Contain("one preview call");
        description.Should().Contain("viewportWidth");
        description.Should().Contain("viewportHeight");
        description.Should().Contain("Window client");
        description.Should().Contain("exact [version]");
        description.Should().Contain("SHA-512 contentHash");
        description.Should().Contain("preview-local NuGet cache");
        description.Should().Contain("Project/user packs");
        description.Should().Contain("structural");
        description.Should().Contain("applied, built, and launched app");
        description.Should().Contain("visualComparisonChecklist");
        description.Should().Contain("window chrome");
        description.Should().Contain("icons");
        description.Should().Contain("layout and spacing");
        description.Should().Contain("propertyWarnings");
        description.Should().Contain("exact blueprint JSON path");
        description.Should().Contain("elementCorrelations");
        description.Should().Contain("transient x:Name");
        description.Should().Contain("never written into the blueprint");
        description.Should().Contain("ambiguous-authored-name");
        description.Should().Contain("lookup-budget");
        description.Should().Contain("runtime-match-ambiguous");
        description.Should().Contain("runtime-not-found");
        description.Should().Contain("search-incomplete");
        var lookupLimitGuidance = description.Split('\n')
            .Single(line => line.Contains("correlationLookupLimit", StringComparison.Ordinal));
        lookupLimitGuidance.Should().Contain("authored elementName")
            .And.Contain("renderer-provided root x:Name")
            .And.Contain("32").And.Contain("64").And.Contain("lookup-budget");
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
        description.Should().Contain("compact=true");
        description.Should().Contain("exact kind");
    }

    [Fact]
    public void ValidateUiBlueprintDescription_ShouldExplainSizeBudget()
    {
        var description = GetMcpTools()
            .Single(tool => string.Equals(tool.Name, "validate_ui_blueprint", StringComparison.Ordinal))
            .Description;

        description.Should().Contain("blueprintSize");
        description.Should().Contain("remainingCharacters");
        description.Should().Contain("targetPath");
        description.Should().Contain("generated class/member collisions");
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
