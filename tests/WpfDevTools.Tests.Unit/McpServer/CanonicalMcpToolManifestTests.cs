using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;

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
        AssertTags(tools, "element_screenshot", "read-only", "screenshot", "requires-target");
        AssertTags(tools, "get_viewmodel", "read-only", "viewmodel", "requires-target");
        AssertTags(tools, "override_style_setter", "destructive", "nested-mutation-supported", "requires-target");
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
        var tool = tools.Single(entry => GetName(entry) == toolName);
        var tags = tool.GetProperty("capabilityTags")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();

        foreach (var expectedTag in expectedTags)
        {
            tags.Should().Contain(expectedTag);
        }
    }
}
