namespace WpfDevTools.Mcp.Server.McpResources;

internal static class ResponseContractSchemaMetadata
{
    public static object GetSchemaMetadata()
    {
        return new
        {
            format = "wpf-response-contract-v1",
            jsonSchemaDialect = "https://json-schema.org/draft/2020-12/schema",
            perToolSchemaStrategy = new
            {
                inputSchemas = "Use MCP tools/list inputSchema for callable parameters.",
                outputSchemas = "Use MCP tools/list outputSchema first. High-value tools expose exact closed output schemas there; other tools use the generic structured payload schema. Then use this resource's toolPayload/errorPayload guidance and highValueTools contracts for detailed WPF-specific semantics and compatibility versioning."
            },
            constraintFields = new[]
            {
                "type",
                "defaultValue",
                "minimum",
                "maximum",
                "maxItems",
                "maxLength",
                "allowedValues"
            }
        };
    }
}
