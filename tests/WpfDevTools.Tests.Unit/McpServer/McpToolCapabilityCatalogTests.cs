using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolCapabilityCatalogTests
{
    [Fact]
    public void ToolManifestResource_ShouldExposeExplicitViewModelInspectionPolicySet()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());

        GetToolNamesWithPolicyTag(document, "viewmodel-inspection")
            .Should().BeEquivalentTo(
            [
                "execute_command",
                "get_commands",
                "get_datacontext_chain",
                "get_viewmodel",
                "modify_viewmodel"
            ]);
    }

    [Fact]
    public void ToolManifestResource_ShouldExposeExplicitScreenshotPolicySet()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());

        GetToolNamesWithPolicyTag(document, "screenshots")
            .Should().BeEquivalentTo(["element_screenshot"]);
    }

    [Fact]
    public void ToolManifestResource_ShouldExposeExplicitSensitiveReadPolicySet()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());

        GetToolNamesWithPolicyTag(document, "sensitive-reads")
            .Should().BeEquivalentTo(
            [
                "capture_state_snapshot",
                "compare_trees",
                "diagnose_visibility",
                "drain_events",
                "find_binding_leaks",
                "find_elements",
                "get_affected_elements",
                "get_applied_styles",
                "get_binding_errors",
                "get_binding_mismatches",
                "get_binding_value_chain",
                "get_bindings",
                "get_clipping_info",
                "get_dp_metadata",
                "get_dp_value_source",
                "get_element_snapshot",
                "get_event_handlers",
                "get_focus_state",
                "get_form_summary",
                "get_interaction_readiness",
                "get_layout_info",
                "get_logical_tree",
                "get_namescope",
                "get_render_stats",
                "get_resource_chain",
                "get_state_diff",
                "get_template_tree",
                "get_triggers",
                "get_ui_summary",
                "get_validation_errors",
                "get_visual_count",
                "get_visual_tree",
                "get_windows",
                "restore_state_snapshot",
                "serialize_to_xaml",
                "set_dp_value",
                "trace_routed_events",
                "clear_dp_value",
                "override_style_setter",
                "wait_for_dp_change",
                "wait_for_dp_change_after_mutation",
                "watch_dp_changes"
            ]);
    }

    [Fact]
    public void CapabilityCatalogSource_ShouldNotUseSubstringMatchingForPolicySensitiveTags()
    {
        var source = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/McpTools/McpToolCapabilityCatalog.cs"));

        source.Should().NotContain("Contains(\"viewmodel\"");
        source.Should().NotContain("Contains(\"command\"");
        source.Should().NotContain("Contains(\"datacontext\"");
        source.Should().NotContain("Contains(\"screenshot\"");
    }

    private static string[] GetToolNamesWithPolicyTag(JsonDocument document, string policyTag)
        => document.RootElement.GetProperty("tools")
            .EnumerateArray()
            .Where(tool => tool.GetProperty("policyCapabilityTags").EnumerateArray()
                .Any(tag => string.Equals(tag.GetString(), policyTag, StringComparison.Ordinal)))
            .Select(tool => tool.GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
}
