using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// Helper for bridging MCP SDK tool methods to existing tool ExecuteAsync implementations.
/// Converts typed parameters to JsonElement and wraps results as CallToolResult.
/// </summary>
public static class ToolCallHelper
{
    private static readonly ConcurrentDictionary<string, object> ToolCache = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Build a JsonElement from a dictionary of named parameters.
    /// Null values are excluded from the resulting JSON object.
    /// </summary>
    /// <param name="parameters">Named parameter tuples</param>
    /// <returns>JsonElement containing the parameters, or null if all values are null</returns>
    public static JsonElement? BuildJsonArgs(params (string name, object? value)[] parameters)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (name, value) in parameters)
        {
            if (value is not null)
            {
                dict[name] = value;
            }
        }

        if (dict.Count == 0)
            return null;

        return JsonSerializer.SerializeToElement(dict, SerializerOptions);
    }

    /// <summary>
    /// Execute a tool and wrap the result as a CallToolResult.
    /// Detects tool errors by checking for { success: false } in the result.
    /// </summary>
    /// <param name="execute">The tool's ExecuteAsync function</param>
    /// <param name="args">JSON arguments for the tool</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>CallToolResult with IsError set appropriately</returns>
    public static async Task<CallToolResult> ExecuteAndWrapAsync(
        Func<JsonElement?, CancellationToken, Task<object>> execute,
        JsonElement? args,
        CancellationToken cancellationToken)
    {
        var result = await execute(args, cancellationToken).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(result, SerializerOptions);
        var isError = IsToolResultError(json);

        return new CallToolResult()
        {
            Content = [new TextContentBlock() { Text = json }],
            IsError = isError
        };
    }

    /// <summary>
    /// Detect if a tool result indicates an error by checking for success: false
    /// </summary>
    internal static bool IsToolResultError(string json)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(json);

            if (element.TryGetProperty("success", out var successProp)
                && successProp.ValueKind == JsonValueKind.False)
            {
                return true;
            }
        }
        catch (JsonException)
        {
            // If we can't parse, assume success
        }

        return false;
    }

    /// <summary>
    /// Get or create a cached tool instance. Tools are stateless (only hold SessionManager
    /// reference) and thread-safe, so a single instance can be reused across concurrent calls.
    /// </summary>
    /// <typeparam name="T">Tool type</typeparam>
    /// <param name="key">Unique cache key (typically the MCP tool name)</param>
    /// <param name="factory">Factory to create the tool if not cached</param>
    internal static T CachedTool<T>(string key, Func<T> factory) where T : class
        => (T)ToolCache.GetOrAdd(key, _ => factory());
}
