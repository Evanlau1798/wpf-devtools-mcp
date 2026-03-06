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
    /// Note: value types (int, bool) are boxed via the object? parameter — acceptable
    /// since tool calls are not a hot path (~1-10 calls/second).
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
    /// Uses single-pass serialization: object -> JsonElement -> check error -> raw text.
    /// CRITICAL FIX: Enforces 5-second timeout on all tool executions to prevent server hang
    /// if target process is frozen or unresponsive.
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
        // CRITICAL FIX: Enforce 5-second timeout on all tool executions
        // Prevents server hang if target process is frozen or unresponsive
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var result = await execute(args, cts.Token).ConfigureAwait(false);
            var jsonElement = JsonSerializer.SerializeToElement(result, SerializerOptions);
            var isError = IsToolResultError(jsonElement);

            return new CallToolResult()
            {
                Content = [new TextContentBlock() { Text = jsonElement.GetRawText() }],
                IsError = isError
            };
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred (our CTS was cancelled, but caller's token was not)
            return new CallToolResult()
            {
                Content = [new TextContentBlock()
                {
                    Text = JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = "Tool execution timed out after 5 seconds. Target process may be frozen or unresponsive."
                    }, SerializerOptions)
                }],
                IsError = true
            };
        }
    }

    /// <summary>
    /// Detect if a tool result indicates an error by checking for success: false.
    /// Accepts a pre-parsed JsonElement to avoid redundant deserialization.
    /// </summary>
    internal static bool IsToolResultError(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("success", out var successProp)
            && successProp.ValueKind == JsonValueKind.False;
    }

    /// <summary>
    /// Get or create a cached tool instance. Tools are stateless wrappers that only hold
    /// a SessionManager reference and are thread-safe, so a single instance can be reused
    /// across concurrent calls.
    /// <para>
    /// IMPORTANT: This cache is static and process-lifetime. The factory captures the
    /// SessionManager from the first invocation. This is correct because SessionManager
    /// is registered as a DI singleton in Program.cs — only one instance exists per process.
    /// </para>
    /// <para>
    /// CRITICAL ASSUMPTION: If the hosting model ever changes to support multiple server
    /// instances per process (e.g., multi-tenant scenarios), this cache MUST be scoped
    /// to the server instance, not static. Current implementation assumes single-server-per-process.
    /// </para>
    /// <para>
    /// Thread Safety: ConcurrentDictionary.GetOrAdd is thread-safe. Multiple concurrent calls
    /// with the same key will result in only one factory invocation, with all callers receiving
    /// the same instance.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Tool type</typeparam>
    /// <param name="key">Unique cache key (typically the MCP tool name)</param>
    /// <param name="factory">Factory to create the tool if not cached</param>
    internal static T CachedTool<T>(string key, Func<T> factory) where T : class
        => (T)ToolCache.GetOrAdd(key, _ => factory());

    /// <summary>
    /// Clear the tool cache. Only for use in tests to ensure test isolation.
    /// </summary>
    internal static void ResetCacheForTesting() => ToolCache.Clear();
}
