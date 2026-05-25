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
