using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Validation;

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
            tool.GetProperty("riskTier").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("policyTags").ValueKind.Should().Be(JsonValueKind.Array);
            tool.GetProperty("responseContractStatus").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("inputSchemaHash").GetString().Should().HaveLength(64);
            tool.GetProperty("outputSchemaStatus").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("outputSchemaHash").GetString().Should().HaveLength(64);
            tool.GetProperty("examplesStatus").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("docsCoverageStatus").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("liveTestCoverageStatus").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("mutationRestoreRequirementStatus").GetString().Should().NotBeNullOrWhiteSpace();
            tool.GetProperty("parameters").ValueKind.Should().Be(JsonValueKind.Array);
            tool.GetProperty("requiredParameters").ValueKind.Should().Be(JsonValueKind.Array);
            tool.GetProperty("capabilityTags").GetArrayLength().Should().BeGreaterThan(0);
            tool.GetProperty("annotations").ValueKind.Should().Be(JsonValueKind.Object);
        }
    }

    [Fact]
    public void ToolManifestResource_ShouldExposeCompleteGovernanceMetadata()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();

        AssertGovernance(tools, "get_processes", "low", "not-mutating");
        AssertGovernance(tools, "get_ui_summary", "sensitive-read", "not-mutating");
        AssertGovernance(tools, "element_screenshot", "controlled-sensitive", "not-mutating");
        AssertGovernance(tools, "modify_viewmodel", "destructive-sensitive", "snapshot-restore-required");
        AssertGovernance(tools, "batch_mutate", "destructive-sensitive", "snapshot-restore-required");
        var allowedLiveCoverageStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            "missing",
            "live-e2e-covered"
        };

        foreach (var tool in tools)
        {
            tool.GetProperty("policyTags").EnumerateArray()
                .Select(tag => tag.GetString())
                .Should().BeEquivalentTo(tool.GetProperty("policyCapabilityTags").EnumerateArray().Select(tag => tag.GetString()));
            tool.GetProperty("responseContractStatus").GetString()
                .Should().Be(tool.GetProperty("outputSchemaStatus").GetString());
            tool.GetProperty("liveTestCoverageStatus").GetString()
                .Should().BeOneOf(allowedLiveCoverageStatuses);
        }
    }

    [Fact]
    public void ToolManifestResource_ShouldExposeReflectionBackedInputConstraints()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();

        var compose = tools.Single(tool => GetName(tool) == "compose_ui_blueprint");
        var blueprintJson = compose.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "blueprintJson");
        blueprintJson.GetProperty("constraints").GetProperty("maxLength").GetInt32()
            .Should().Be(BoundaryStringLimits.MaxStringifiedJsonArgumentLength);

        var visualTree = tools.Single(tool => GetName(tool) == "get_visual_tree");
        var depth = visualTree.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "depth");
        depth.GetProperty("constraints").GetProperty("minimum").GetInt32().Should().Be(0);
        depth.GetProperty("constraints").GetProperty("maximum").GetInt32()
            .Should().Be(TreeRequestOptions.MaxDepthLimit);
    }

    [Fact]
    public void ToolManifestResource_ShouldUseExplicitLiveEvidenceInventory()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var statusByName = document.RootElement
            .GetProperty("tools")
            .EnumerateArray()
            .ToDictionary(GetName, tool => tool.GetProperty("liveTestCoverageStatus").GetString(), StringComparer.Ordinal);

        statusByName["get_ui_summary"].Should().Be("live-e2e-covered");
        statusByName["set_dp_value"].Should().Be("live-e2e-covered");
        statusByName["override_style_setter"].Should().Be("missing",
            "live coverage must come from an explicit evidence inventory, not from destructive-policy inference");
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
        AssertTags(tools, "get_ui_summary", "read-only", "sensitive-read", "requires-target");
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
        AssertPolicyTags(tools, "get_ui_summary", "sensitive-reads");
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

    [Fact]
    public void ToolManifestResource_ShouldExposeExactOutputSchemaStatusForHighValueTools()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var tools = document.RootElement.GetProperty("tools").EnumerateArray().ToArray();

        foreach (var toolName in new[]
                 {
                     "connect",
                     "get_processes",
                     "get_ui_summary",
                     "get_element_snapshot",
                     "get_bindings",
                     "get_binding_errors",
                     "capture_state_snapshot",
                     "get_state_diff",
                     "restore_state_snapshot",
                     "batch_mutate",
                     "element_screenshot"
                 })
        {
            var tool = tools.Single(entry => GetName(entry) == toolName);
            tool.GetProperty("outputSchemaStatus").GetString().Should().Be("exact-tool-output-schema");
            tool.GetProperty("outputSchemaHash").GetString().Should().HaveLength(64);
        }
    }

    [Fact]
    public void ToolManifestResource_ShouldClassifyEveryOutputContractStatus()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetToolManifest());
        var allowedStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            "exact-tool-output-schema",
            "specialized-response-contract",
            "generic-structured-payload-intentional"
        };

        var unclassified = document.RootElement
            .GetProperty("tools")
            .EnumerateArray()
            .Select(tool => new
            {
                Name = GetName(tool),
                Status = tool.GetProperty("outputSchemaStatus").GetString()
            })
            .Where(tool => tool.Status is null || !allowedStatuses.Contains(tool.Status))
            .Select(tool => $"{tool.Name}: {tool.Status}")
            .ToArray();

        unclassified.Should().BeEmpty(
            "each registered tool must publish an explicit response-contract status in the canonical manifest");
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

    private static void AssertGovernance(
        JsonElement[] tools,
        string toolName,
        string expectedRiskTier,
        string expectedMutationStatus)
    {
        var tool = tools.Single(entry => GetName(entry) == toolName);

        tool.GetProperty("riskTier").GetString().Should().Be(expectedRiskTier);
        tool.GetProperty("mutationRestoreRequirementStatus").GetString().Should().Be(expectedMutationStatus);
        tool.GetProperty("responseContractStatus").GetString().Should().NotBeNullOrWhiteSpace();
        tool.GetProperty("examplesStatus").GetString().Should().NotBe("missing-description-examples");
        tool.GetProperty("docsCoverageStatus").GetString().Should().NotBe("missing");
        tool.GetProperty("liveTestCoverageStatus").GetString().Should().NotBe("missing");
    }

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
