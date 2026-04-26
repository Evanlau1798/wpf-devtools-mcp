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
                outputSchemas = "Use shared toolPayload/errorPayload guidance plus highValueTools specialized contracts until native tools/list outputSchema is enabled."
            },
            constraintFields = new[]
            {
                "type",
                "defaultValue",
                "minimum",
                "maximum",
                "allowedValues"
            }
        };
    }
}
