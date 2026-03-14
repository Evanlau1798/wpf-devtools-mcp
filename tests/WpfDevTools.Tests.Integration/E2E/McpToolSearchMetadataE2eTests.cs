using System.IO;
using System.Text.Json;
using FluentAssertions;

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
        text.Should().Contain("contextRef");
        text.Should().Contain("prefetchTools");
    }

    private static void AssertTitle(JsonElement tools, string toolName, string expectedTitle)
    {
        var tool = tools.EnumerateArray()
            .Single(t => t.GetProperty("name").GetString() == toolName);

        tool.GetProperty("title").GetString().Should().Be(expectedTitle);
    }

    private static string FindServerExecutable()
    {
        var solutionRoot = FindSolutionRoot();
        var candidates = new[]
        {
            Path.Combine(solutionRoot, "src", "WpfDevTools.Mcp.Server", "bin", "Debug", "net8.0", "WpfDevTools.Mcp.Server.exe"),
            Path.Combine(solutionRoot, "src", "WpfDevTools.Mcp.Server", "bin", "Release", "net8.0", "WpfDevTools.Mcp.Server.exe")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("WpfDevTools.Mcp.Server.exe was not found. Build the MCP server first.");
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Solution root not found for MCP tool search integration test.");
    }
}
