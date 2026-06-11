using System.Reflection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;

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
            coverageMeaning = "All registered MCP tools publish an outputContractStatus. exact-tool-output-schema tools also expose closed tools/list output schemas; generic-structured-payload-intentional tools deliberately inherit the shared toolPayload, navigation, nextSteps, pendingEvents, and errorPayload guidance.",
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
                outputContractStatus = McpToolOutputSchemas.GetSchemaStatus(tool),
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
