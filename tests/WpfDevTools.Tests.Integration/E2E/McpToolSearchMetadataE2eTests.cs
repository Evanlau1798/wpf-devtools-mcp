using System.IO;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Tests.Integration.E2E;

[Trait("Category", "Integration")]
public sealed class McpToolSearchMetadataE2eTests
{
    [Fact]
    public async Task ToolsList_ShouldExposeSearchOptimizedAnchorTitles()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListToolsAsync();
        var tools = response.GetProperty("result").GetProperty("tools");

        AssertTitle(tools, "get_processes", "List Inspectable WPF Processes");
        AssertTitle(tools, "connect", "Connect To Running WPF Process");
        AssertTitle(tools, "get_visual_tree", "Inspect WPF Visual Tree");
        AssertTitle(tools, "get_binding_errors", "Diagnose WPF Binding Errors");
        AssertTitle(tools, "get_viewmodel", "Inspect WPF ViewModel");
    }

    [Fact]
    public async Task ToolsList_ShouldNotExposeClaudeIncompatibleStructuredContentOutputSchema()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListToolsAsync();
        var tools = response.GetProperty("result").GetProperty("tools");

        foreach (var tool in tools.EnumerateArray())
        {
            tool.TryGetProperty("outputSchema", out var outputSchema).Should().BeFalse(
                $"tool '{tool.GetProperty("name").GetString()}' should not advertise Claude-incompatible structured content output schema");

            if (tool.TryGetProperty("outputSchema", out outputSchema) &&
                outputSchema.ValueKind == JsonValueKind.Object &&
                outputSchema.TryGetProperty("properties", out var properties))
            {
                properties.TryGetProperty("structuredContent", out _).Should().BeFalse(
                    $"tool '{tool.GetProperty("name").GetString()}' should not include outputSchema.properties.structuredContent because Claude rejects it during tools/list validation");
            }
        }
    }

    [Fact]
    public async Task Initialize_ShouldDescribeNavigationEnvelopeForAdvancedClients()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        var init = await client.StartAsync(serverExe);
        init.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("instructions", out var instructions).Should().BeTrue();
        var text = instructions.GetString();

        text.Should().Contain("navigation");
        text.Should().Contain("nextSteps");
        text.Should().Contain("contextRefs");
        text.Should().Contain("prefetchTools");
        text.Should().Contain("wpf://contracts/response");
    }

    [Fact]
    public async Task ResourcesList_ShouldExposeMachineReadableResponseContractResource()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListResourcesAsync();
        var resources = response.GetProperty("result").GetProperty("resources");
        var resource = resources.EnumerateArray()
            .Single(item => item.GetProperty("uri").GetString() == "wpf://contracts/response");

        resource.GetProperty("name").GetString().Should().Be("wpf_response_contract");
        resource.GetProperty("title").GetString().Should().Be("Response Contract");
        resource.GetProperty("mimeType").GetString().Should().Be("application/json");
    }

    [Fact]
    public async Task ReadResource_ShouldReturnMachineReadableResponseContractJson()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ReadResourceAsync("wpf://contracts/response");
        var contents = response.GetProperty("result").GetProperty("contents");
        var content = contents.EnumerateArray().Single();

        content.GetProperty("uri").GetString().Should().Be("wpf://contracts/response");
        content.GetProperty("mimeType").GetString().Should().Be("application/json");

        using var document = JsonDocument.Parse(content.GetProperty("text").GetString()!);
        var root = document.RootElement;

        root.GetProperty("responseContractVersion").GetString().Should().Be(ResponseContractVersion.Current);
        root.GetProperty("toolPayload").GetProperty("canonicalField").GetString().Should().Be("structuredContent");
        root.GetProperty("navigation").GetProperty("field").GetString().Should().Be("navigation");
        root.GetProperty("nextSteps").GetProperty("derivedFrom").GetString().Should().Be("navigation.recommended");
        root.GetProperty("compatibility").GetProperty("toolListOutputSchema").GetString().Should().Be("omitted");
    }

    [Fact]
    public async Task RawToolCallEnvelope_ShouldMatchPublishedResponseContractForErrorAnnotations()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.CallToolEnvelopeAsync("ping");
        var result = response.GetProperty("result");

        result.TryGetProperty("structuredContent", out var structuredContent).Should().BeTrue();
        structuredContent.GetProperty("success").GetBoolean().Should().BeFalse();

        var content = result.GetProperty("content");
        var textBlock = content.EnumerateArray().Single();
        textBlock.TryGetProperty("text", out var text).Should().BeTrue();
        JsonDocument.Parse(text.GetString()!).RootElement.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
        textBlock.TryGetProperty("annotations", out var annotations).Should().BeTrue();
        annotations.GetProperty("priority").GetDouble().Should().Be(1.0d);
    }

    private static void AssertTitle(JsonElement tools, string toolName, string expectedTitle)
    {
        var tool = tools.EnumerateArray()
            .Single(t => t.GetProperty("name").GetString() == toolName);

        tool.GetProperty("title").GetString().Should().Be(expectedTitle);
    }

    private static string FindServerExecutable()
    {
        return IntegrationExecutableLocator.FindExecutable(
                AppContext.BaseDirectory,
                "src",
                "WpfDevTools.Mcp.Server",
                "net8.0",
                "WpfDevTools.Mcp.Server.exe")
            ?? throw new InvalidOperationException(
                "WpfDevTools.Mcp.Server.exe was not found for the current test configuration. Build the MCP server first.");
    }
}
