using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ResponseContractCoverageTests
{
    [Fact]
    public void ResponseContractResource_ShouldCoverEveryRegisteredMcpTool()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var root = document.RootElement;

        var registeredTools = GetRegisteredToolNames();
        var coveredTools = GetCoveredToolNames(root);
        var missingTools = registeredTools
            .Where(tool => !coveredTools.Contains(tool))
            .ToArray();

        missingTools.Should().BeEmpty(
            "all registered MCP tools should have response contract coverage; missing tools: {0}",
            string.Join(", ", missingTools));

        root.TryGetProperty("registeredToolCoverage", out var coverage).Should().BeTrue();
        coverage.GetProperty("generatedFrom").GetString().Should().Be("McpServerToolAttribute");
        coverage.GetProperty("contractResource").GetString().Should().Be("wpf://contracts/response");
        coverage.GetProperty("toolCount").GetInt32().Should().Be(registeredTools.Length);
    }

    private static string[] GetRegisteredToolNames()
    {
        return typeof(ProcessMcpTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static HashSet<string> GetCoveredToolNames(JsonElement root)
    {
        var coveredTools = new HashSet<string>(StringComparer.Ordinal);

        if (root.TryGetProperty("highValueTools", out var highValueTools))
        {
            AddCoveredTools(coveredTools, highValueTools);
        }

        if (root.TryGetProperty("registeredToolCoverage", out var coverage))
        {
            AddCoveredTools(coveredTools, coverage.GetProperty("tools"));
        }

        return coveredTools;
    }

    private static void AddCoveredTools(HashSet<string> coveredTools, JsonElement toolEntries)
    {
        foreach (var toolEntry in toolEntries.EnumerateArray())
        {
            var toolName = toolEntry.GetProperty("tool").GetString();
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                coveredTools.Add(toolName);
            }
        }
    }
}
