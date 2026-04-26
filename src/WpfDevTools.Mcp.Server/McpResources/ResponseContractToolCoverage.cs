using System.Reflection;
using ModelContextProtocol.Server;

namespace WpfDevTools.Mcp.Server.McpResources;

internal static class ResponseContractToolCoverage
{
    public static object GetRegisteredToolCoverage(string resourceUri)
    {
        var tools = GetRegisteredTools(resourceUri);

        return new
        {
            generatedFrom = nameof(McpServerToolAttribute),
            coverageKind = "generated-completeness",
            contractResource = resourceUri,
            specializedContractsField = "highValueTools",
            coverageMeaning = "All registered MCP tools inherit the shared toolPayload, navigation, nextSteps, pendingEvents, and errorPayload guidance unless highValueTools provides a specialized contract.",
            toolCount = tools.Length,
            tools
        };
    }

    private static object[] GetRegisteredTools(string resourceUri)
    {
        return typeof(ResponseContractToolCoverage).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(tool => new
            {
                tool,
                coverageKind = "generated-completeness",
                contractResource = resourceUri,
                guidanceFields = new[]
                {
                    "toolPayload",
                    "navigation",
                    "nextSteps",
                    "pendingEventsAdditiveContract",
                    "errorPayload"
                }
            })
            .ToArray();
    }
}
