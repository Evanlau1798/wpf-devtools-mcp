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
                outputSchemas = "Use MCP tools/list outputSchema for the common result.structuredContent payload fields, then this resource's toolPayload/errorPayload guidance and highValueTools contracts for detailed WPF-specific payload fields."
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
