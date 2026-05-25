using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class CanonicalMcpToolManifestTests
{
    [Fact]
    public void ToolManifestResource_ShouldCoverEveryRegisteredToolWithRequiredMetadata()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var root = document.RootElement;
        var registeredTools = GetRegisteredTools();
        var manifestTools = root.GetProperty("tools").EnumerateArray().ToArray();

        root.GetProperty("resourceUri").GetString().Should().Be("wpf://contracts/tools");
        root.GetProperty("generatedFrom").GetString().Should().Be(nameof(McpServerToolAttribute));
        root.GetProperty("toolCount").GetInt32().Should().Be(registeredTools.Length);
        manifestTools.Select(GetName).Should().BeEquivalentTo(registeredTools.Select(tool => tool.Attribute.Name));

        foreach (var tool in manifestTools)
        {
            tool.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("bridgeFile").GetString().Should().EndWith(".cs");
            tool.GetProperty("method").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("category").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("inputSchemaHash").GetString().Should().HaveLength(64);
            tool.GetProperty("outputSchemaStatus").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("outputSchemaHash").GetString().Should().HaveLength(64);
            tool.GetProperty("examplesStatus").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("docsCoverageStatus").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("parameters").ValueKind.Should().Be(JsonValueKind.Array);
            tool.GetProperty("requiredParameters").ValueKind.Should().Be(JsonValueKind.Array);
            tool.GetProperty("capabilityTags").GetArrayLength().Should().BeGreaterThan(0);
            tool.GetProperty("annotations").ValueKind.Should().Be(JsonValueKind.Object);
        }
    }

    [Fact]
    public void ToolManifestResource_ShouldClassifyPolicySensitiveTools()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();

        AssertTags(tools, "get_processes", "read-only", "process-discovery", "safe-first");
        AssertTags(tools, "connect", "destructive", "process-discovery", "requires-target");
        AssertMissingTags(tools, "connect", "nested-mutation-supported");
        AssertTags(tools, "element_screenshot", "read-only", "screenshot", "requires-target");
        AssertTags(tools, "get_viewmodel", "read-only", "viewmodel", "requires-target");
        AssertTags(tools, "override_style_setter", "destructive", "nested-mutation-supported", "requires-target");
        AssertTags(tools, "wait_for_dp_change_after_mutation", "destructive", "accepts-mutation-step", "requires-target");
        AssertMissingTags(tools, "wait_for_dp_change_after_mutation", "nested-mutation-supported");
    }

    [Fact]
    public void ToolManifestResource_ShouldKeepMutationTagsAlignedWithBatchMutationCatalog()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();

        foreach (var tool in tools)
        {
            var toolName = GetName(tool);
            var tags = GetTags(tool);

            tags.Contains("nested-mutation-supported").Should()
                .Be(BatchMutationCatalog.SupportedTools.Contains(toolName),
                    "nested-mutation-supported should exactly match batch_mutate nested tool support");
            tags.Contains("accepts-mutation-step").Should()
                .Be(string.Equals(toolName, "wait_for_dp_change_after_mutation", StringComparison.Ordinal),
                    "only wait_for_dp_change_after_mutation accepts a single mutation step outside batch_mutate");
        }
    }

    [Fact]
    public void ToolManifestResource_ShouldExposePolicyCapabilityTagsThatMatchStaticPolicyGates()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();

        AssertTags(tools, "connect", "destructive");
        AssertPolicyMissingTags(tools, "connect", "destructive", "destructive-tools");
        AssertTags(tools, "select_active_process", "destructive");
        AssertPolicyMissingTags(tools, "select_active_process", "destructive", "destructive-tools");

        AssertPolicyTags(tools, "modify_viewmodel", "destructive-tools", "viewmodel-inspection");
        AssertPolicyTags(tools, "element_screenshot", "screenshots");
        AssertPolicyTags(tools, "get_datacontext_chain", "viewmodel-inspection");
    }

    [Fact]
    public void ToolManifestResource_ShouldClassifyOnlyStatefulSnapshotToolsAsStateConsuming()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();

        AssertMissingTags(tools, "get_element_snapshot", "state-consuming");
        AssertTags(tools, "capture_state_snapshot", "state-consuming");
        AssertTags(tools, "restore_state_snapshot", "state-consuming");
        AssertTags(tools, "get_state_diff", "state-consuming");
        AssertTags(tools, "drain_events", "state-consuming");
        AssertTags(tools, "batch_mutate", "state-consuming");
    }

    private static (string Name, McpServerToolAttribute Attribute)[] GetRegisteredTools()
    {
        return typeof(ProcessMcpTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute?.Name is not null)
            .Select(attribute => (attribute!.Name!, attribute))
            .OrderBy(tool => tool.Item1, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetName(JsonElement tool) => tool.GetProperty("name").GetString()!;

    private static void AssertTags(JsonElement[] tools, string toolName, params string[] expectedTags)
    {
        var tags = GetTags(tools, toolName);

        foreach (var expectedTag in expectedTags)
        {
            tags.Should().Contain(expectedTag);
        }
    }

    private static void AssertMissingTags(JsonElement[] tools, string toolName, params string[] unexpectedTags)
    {
        var tags = GetTags(tools, toolName);

        foreach (var unexpectedTag in unexpectedTags)
        {
            tags.Should().NotContain(unexpectedTag);
        }
    }

    private static void AssertPolicyTags(JsonElement[] tools, string toolName, params string[] expectedTags)
    {
        var tags = GetPolicyTags(tools, toolName);

        foreach (var expectedTag in expectedTags)
        {
            tags.Should().Contain(expectedTag);
        }
    }

    private static void AssertPolicyMissingTags(JsonElement[] tools, string toolName, params string[] unexpectedTags)
    {
        var tags = GetPolicyTags(tools, toolName);

        foreach (var unexpectedTag in unexpectedTags)
        {
            tags.Should().NotContain(unexpectedTag);
        }
    }

    private static string[] GetTags(JsonElement[] tools, string toolName)
    {
        var tool = tools.Single(entry => GetName(entry) == toolName);
        return GetTags(tool);
    }

    private static string[] GetTags(JsonElement tool)
    {
        return tool.GetProperty("capabilityTags")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();
    }

    private static string[] GetPolicyTags(JsonElement[] tools, string toolName)
    {
        var tool = tools.Single(entry => GetName(entry) == toolName);
        return tool.GetProperty("policyCapabilityTags")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();
    }
}
