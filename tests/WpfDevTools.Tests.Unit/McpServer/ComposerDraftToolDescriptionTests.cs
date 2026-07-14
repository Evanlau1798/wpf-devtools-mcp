using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ComposerDraftToolDescriptionTests
{
    private static readonly string[] DownstreamTools =
    [
        "compose_ui_blueprint",
        "validate_ui_blueprint",
        "render_ui_blueprint",
        "preview_ui_blueprint",
        "repair_ui_blueprint",
        "apply_ui_blueprint",
        "apply_ui_project_integration"
    ];

    [Fact]
    public void DownstreamComposerDescriptions_ShouldAdvertiseDraftReferencesConsistently()
    {
        var descriptions = typeof(UiComposerMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => new
            {
                Name = method.GetCustomAttribute<McpServerToolAttribute>()?.Name,
                Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description
            })
            .Where(tool => DownstreamTools.Contains(tool.Name, StringComparer.Ordinal))
            .ToArray();

        descriptions.Select(tool => tool.Name).Should().BeEquivalentTo(DownstreamTools);
        foreach (var tool in descriptions)
        {
            tool.Description.Should().Contain("raw JSON or an opaque draftRef", tool.Name);
        }

        var compose = descriptions.Single(tool => tool.Name == "compose_ui_blueprint").Description;
        compose.Should().Contain("derived draftRef");
        compose.Should().Contain("candidateDraftRef");
        compose.Should().Contain("omits the full blueprint", "draft transport must remain compact");
        compose.Should().Contain("success=false");
        compose.Should().Contain("MCP error result");
        compose.Should().Contain("@Panel.slots.actions");
        compose.Should().Contain("insertedNodeSummary");
        compose.Should().Contain("32");
        compose.Should().Contain("160");
        compose.Should().Contain("compact values");
        compose.Should().Contain("truncation");
        compose.Should().NotContain("properties of 160");
        typeof(UiComposerMcpTools).GetMethod(nameof(UiComposerMcpTools.ComposeUiBlueprint))!
            .GetParameters().Single(parameter => parameter.Name == "targetPath")
            .GetCustomAttribute<DescriptionAttribute>()!.Description
            .Should().Contain("@Panel.slots.actions")
            .And.Contain("$.layout.slots.content");
    }

    [Fact]
    public void PatchDraftContract_ShouldExposeMutuallyExclusiveMergeAndSurgicalModes()
    {
        var method = typeof(UiComposerMcpTools).GetMethod(nameof(UiComposerMcpTools.PatchUiBlueprintDraft))!;
        var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;
        description.Should().Contain("JSON Merge Patch");
        description.Should().Contain("JSON-path set/remove");
        description.Should().Contain("changeSummary");
        description.Should().Contain("Do not combine patchJson with jsonPath");
        method.GetParameters().Single(parameter => parameter.Name == "jsonPath")
            .GetCustomAttribute<DescriptionAttribute>()!.Description
            .Should().Contain("$.layout.properties[\"accent.color\"]")
            .And.Contain("@Panel.properties.text");

        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var tool = document.RootElement.GetProperty("tools").EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "patch_ui_blueprint_draft");
        tool.GetProperty("requiredParameters").EnumerateArray()
            .Select(entry => entry.GetString()).Should().Equal("draftRef");
        tool.GetProperty("parameters").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString())
            .Should().Contain(["patchJson", "jsonPath", "value", "remove"]);
    }
}
