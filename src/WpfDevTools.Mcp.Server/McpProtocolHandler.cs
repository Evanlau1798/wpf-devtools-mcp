using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Handles MCP protocol requests (JSON-RPC 2.0)
/// </summary>
public class McpProtocolHandler
{
    private readonly ToolRegistry _toolRegistry;

    public McpProtocolHandler(ToolRegistry? toolRegistry = null)
    {
        _toolRegistry = toolRegistry ?? new ToolRegistry();
    }

    /// <summary>
    /// Handle incoming JSON-RPC request
    /// </summary>
    public async Task<string> HandleRequestAsync(string requestJson, CancellationToken cancellationToken)
    {
        try
        {
            // Parse request
            var request = JsonSerializer.Deserialize<JsonElement>(requestJson);

            var id = request.TryGetProperty("id", out var idProp) ? idProp : (JsonElement?)null;
            var method = request.GetProperty("method").GetString();

            if (string.IsNullOrEmpty(method))
            {
                return CreateErrorResponse(id, -32600, "Invalid Request: missing method");
            }

            // Route to handler
            object? result = method switch
            {
                "initialize" => await HandleInitializeAsync(request, cancellationToken),
                "tools/list" => await HandleToolsListAsync(cancellationToken),
                "tools/call" => await HandleToolsCallAsync(request, cancellationToken),
                _ => throw new MethodNotFoundException($"Method not found: {method}")
            };

            return CreateSuccessResponse(id, result);
        }
        catch (JsonException)
        {
            return CreateErrorResponse(null, -32700, "Parse error");
        }
        catch (MethodNotFoundException ex)
        {
            return CreateErrorResponse(null, -32601, ex.Message);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(null, -32603, $"Internal error: {ex.Message}");
        }
    }

    private async Task<object> HandleInitializeAsync(JsonElement request, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning

        return new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            serverInfo = new
            {
                name = "wpf-devtools-mcp",
                version = "0.1.0"
            }
        };
    }

    private async Task<object> HandleToolsListAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning

        var tools = _toolRegistry.GetAllTools()
            .Select(t => new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = t.Parameters
            })
            .ToList();

        return new { tools };
    }

    private async Task<object> HandleToolsCallAsync(JsonElement request, CancellationToken cancellationToken)
    {
        var paramsElement = request.GetProperty("params");
        var toolName = paramsElement.GetProperty("name").GetString()
            ?? throw new ArgumentException("Missing tool name");

        var tool = _toolRegistry.GetTool(toolName)
            ?? throw new MethodNotFoundException($"Tool not found: {toolName}");

        if (tool.ExecuteHandler == null)
            throw new InvalidOperationException($"Tool '{toolName}' has no execute handler");

        var arguments = paramsElement.TryGetProperty("arguments", out var args) ? args : (JsonElement?)null;

        var result = await tool.ExecuteHandler(arguments, cancellationToken);

        return new
        {
            content = new[]
            {
                new { type = "text", text = JsonSerializer.Serialize(result) }
            }
        };
    }

    private string CreateSuccessResponse(JsonElement? id, object? result)
    {
        var response = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.HasValue ? (id.Value.ValueKind == JsonValueKind.Number ? (object)id.Value.GetInt32() : id.Value.ValueKind == JsonValueKind.String ? (object)id.Value.GetString()! : null) : null,
            ["result"] = result
        };

        return JsonSerializer.Serialize(response);
    }

    private string CreateErrorResponse(JsonElement? id, int code, string message)
    {
        var response = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.HasValue ? (id.Value.ValueKind == JsonValueKind.Number ? (object)id.Value.GetInt32() : id.Value.ValueKind == JsonValueKind.String ? (object)id.Value.GetString()! : null) : null,
            ["error"] = new
            {
                code,
                message
            }
        };

        return JsonSerializer.Serialize(response);
    }

    private class MethodNotFoundException : Exception
    {
        public MethodNotFoundException(string message) : base(message) { }
    }
}
