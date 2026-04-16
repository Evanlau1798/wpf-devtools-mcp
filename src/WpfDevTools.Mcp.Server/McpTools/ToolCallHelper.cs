using System.Collections.Concurrent;
using System.Text.Json;
using System.Buffers;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// Helper for bridging MCP SDK tool methods to existing tool ExecuteAsync implementations.
/// Converts typed parameters to JsonElement and wraps results as CallToolResult.
/// </summary>
public static partial class ToolCallHelper
{
    private static readonly ConcurrentDictionary<string, object> ToolCache = new();
    private static readonly HashSet<string> NavigationOptOutTools = new(StringComparer.Ordinal)
    {
        "get_binding_errors"
    };
    private static MetricsCollector? _metrics;
    private static ToolNavigationPlanner _navigationPlanner = new(new ToolNavigationRegistry());

    private static readonly Annotations ErrorAnnotations = new() { Priority = 1.0f };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Set the MetricsCollector instance for recording tool execution metrics.
    /// Called once during DI initialization from Program.cs.
    /// </summary>
    internal static void SetMetricsCollector(MetricsCollector metrics) => _metrics = metrics;

    /// <summary>
    /// Build a JsonElement from a dictionary of named parameters.
    /// Null values are excluded from the resulting JSON object.
    /// Note: value types (int, bool) are boxed via the object? parameter, which is acceptable
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
    /// CRITICAL FIX: Enforces timeout (from McpServerConfiguration) on all tool executions
    /// to prevent server hang if target process is frozen or unresponsive.
    /// </summary>
    /// <param name="execute">The tool's ExecuteAsync function</param>
    /// <param name="args">JSON arguments for the tool</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="timeoutSeconds">Optional override for tool timeout in seconds.</param>
    /// <param name="navigationState">Optional navigation-only session state used to compute conditional next steps.</param>
    /// <param name="toolName">Tool name for metrics (auto-populated from caller method name)</param>
    /// <returns>CallToolResult with IsError set appropriately</returns>
    public static async Task<CallToolResult> ExecuteAndWrapAsync(
        Func<JsonElement?, CancellationToken, Task<object>> execute,
        JsonElement? args,
        CancellationToken cancellationToken,
        int? timeoutSeconds = null,
        NavigationSessionState? navigationState = null,
        [System.Runtime.CompilerServices.CallerMemberName] string toolName = "unknown")
    {
        var effectiveTimeoutSeconds = timeoutSeconds ?? McpServerConfiguration.DefaultToolTimeoutSeconds;
        var includeNavigation = ShouldIncludeNavigation(toolName, args);

        // CRITICAL FIX: Enforce timeout on all tool executions
        // Prevents server hang if target process is frozen or unresponsive
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(effectiveTimeoutSeconds));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await execute(args, cts.Token).ConfigureAwait(false);
            sw.Stop();
            var jsonElement = JsonSerializer.SerializeToElement(result, SerializerOptions);
            if (includeNavigation)
            {
                var navigation = _navigationPlanner.PlanEnvelope(toolName, jsonElement, args, navigationState);
                jsonElement = EnsureNavigation(jsonElement, navigation);
            }
            jsonElement = ApplyToolSpecificContracts(toolName, args, jsonElement);
            jsonElement = NormalizePendingEventsContract(jsonElement);
            var isError = IsToolResultError(jsonElement);

            _metrics?.RecordRequest(toolName, sw.ElapsedMilliseconds, !isError);

            return new CallToolResult()
            {
                Content = [CreateTextContentBlock(jsonElement, isError)],
                StructuredContent = jsonElement,
                IsError = isError
            };
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _metrics?.RecordRequest(toolName, sw.ElapsedMilliseconds, false);

            // Timeout occurred (our CTS was cancelled, but caller's token was not)
            var timeoutPayload = EnsureNavigation(JsonSerializer.SerializeToElement(new
            {
                success = false,
                error = $"Tool execution timed out after {effectiveTimeoutSeconds} seconds. Target process may be frozen or unresponsive.",
                errorCode = "Timeout",
                toolName,
                timeoutSeconds = effectiveTimeoutSeconds,
                suggestedAction = "Check target responsiveness, then retry the tool or reconnect if the session may be stale."
            }, SerializerOptions), ToolNavigationEnvelope.Empty);
            if (!includeNavigation)
            {
                timeoutPayload = RemoveTopLevelProperties(timeoutPayload, "nextSteps", "navigation");
            }

            return new CallToolResult()
            {
                Content = [CreateTextContentBlock(timeoutPayload, isError: true)],
                StructuredContent = timeoutPayload,
                IsError = true
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _metrics?.RecordRequest(toolName, sw.ElapsedMilliseconds, false);

            // Classify exception into stable error code and sanitized message
            // to prevent localized OS text or internal details from leaking to clients
            var (errorCode, sanitizedMessage) = ClassifyException(ex);

            var exceptionPayload = EnsureNavigation(JsonSerializer.SerializeToElement(new
            {
                success = false,
                error = sanitizedMessage,
                errorCode
            }, SerializerOptions), ToolNavigationEnvelope.Empty);
            if (!includeNavigation)
            {
                exceptionPayload = RemoveTopLevelProperties(exceptionPayload, "nextSteps", "navigation");
            }

            return new CallToolResult()
            {
                Content = [CreateTextContentBlock(exceptionPayload, isError: true)],
                StructuredContent = exceptionPayload,
                IsError = true
            };
        }
    }

    /// <summary>
    /// Classify an exception into a stable error code and sanitized message.
    /// Prevents localized OS text or internal implementation details from leaking to clients.
    /// </summary>
    internal static (string errorCode, string message) ClassifyException(Exception ex)
    {
        return ex switch
        {
            InvalidOperationException => ("OperationError", "Operation failed"),
            FileNotFoundException => ("FileNotFound", "Required file not found"),
            UnauthorizedAccessException => ("AccessDenied", "Access denied"),
            ArgumentException => ("InvalidArgument", "Invalid argument"),
            System.Security.Cryptography.CryptographicException => ("SecurityError", "Security verification failed"),
            _ => ("InternalError", "An internal error occurred during tool execution")
        };
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
    /// is registered as a DI singleton in Program.cs, so only one instance exists per process.
    /// </para>
    /// <para>
    /// CRITICAL ASSUMPTION: If the hosting model ever changes to support multiple server
    /// instances per process (e.g., multi-tenant scenarios), this cache MUST be scoped
    /// to the server instance, not static. Current implementation assumes single-server-per-process.
    /// </para>
    /// <para>
    /// Thread Safety: ConcurrentDictionary.GetOrAdd is thread-safe. The factory delegate may
    /// be invoked by multiple threads concurrently, but only one result is stored and returned
    /// to all callers. Extra instances created by concurrent factory calls are discarded by GC.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Tool type</typeparam>
    /// <param name="key">Unique cache key (typically the MCP tool name)</param>
    /// <param name="factory">Factory to create the tool if not cached</param>
    internal static T CachedTool<T>(string key, Func<T> factory) where T : class
        => (T)ToolCache.GetOrAdd(key, _ => factory());

    /// <summary>
    /// Clear the tool cache and metrics. Only for use in tests to ensure test isolation.
    /// </summary>
    internal static void ResetCacheForTesting()
    {
        ToolCache.Clear();
        _metrics = null;
        _navigationPlanner = new ToolNavigationPlanner(new ToolNavigationRegistry());
    }

    internal static void SetNavigationPlannerForTesting(ToolNavigationPlanner planner) =>
        _navigationPlanner = planner ?? throw new ArgumentNullException(nameof(planner));

    internal static NavigationSessionState? ResolveNavigationState(SessionManager sessionManager, JsonElement? args)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);

        var processId = TryGetProcessId(args);
        if (processId is null)
        {
            if (!sessionManager.TryGetActiveProcessId(out var activeProcessId))
            {
                return null;
            }

            processId = activeProcessId;
        }

        if (!sessionManager.TryGetNavigationState(processId.Value, out var state) || state is null)
        {
            return null;
        }

        if (state.ActiveTrace is { } activeTrace && activeTrace.HasExpired(DateTimeOffset.UtcNow))
        {
            sessionManager.ClearActiveTraceState(processId.Value);
            return state with { ActiveTrace = null };
        }

        return state;
    }

    private static JsonElement EnsureNavigation(JsonElement element, ToolNavigationEnvelope navigation)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("nextSteps") || property.NameEquals("navigation"))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WritePropertyName("nextSteps");
        JsonSerializer.Serialize(writer, navigation.Recommended, SerializerOptions);
        writer.WritePropertyName("navigation");
        JsonSerializer.Serialize(writer, navigation, SerializerOptions);
        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static JsonElement ApplyToolSpecificContracts(
        string toolName,
        JsonElement? args,
        JsonElement element)
    {
        if (string.Equals(toolName, "get_ui_summary", StringComparison.Ordinal)
            && TryGetBool(args, "summaryOnly"))
        {
            return RemoveTopLevelProperties(element, "nodes");
        }

        if (string.Equals(toolName, "get_binding_errors", StringComparison.Ordinal)
            && TryGetBool(args, "compact"))
        {
            return RemovePropertyFromArrayItems(element, "errors", "message");
        }

        return element;
    }

    private static JsonElement NormalizePendingEventsContract(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("pendingEvents", out var pendingEvents)
            || pendingEvents.ValueKind != JsonValueKind.Array
            || pendingEvents.GetArrayLength() > 0)
        {
            return element;
        }

        return RemoveTopLevelProperties(element, "pendingEvents");
    }

    private static JsonElement RemoveTopLevelProperties(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object || propertyNames.Length == 0)
        {
            return element;
        }

        var propertiesToRemove = new HashSet<string>(propertyNames, StringComparer.Ordinal);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in element.EnumerateObject())
        {
            if (propertiesToRemove.Contains(property.Name))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static JsonElement RemovePropertyFromArrayItems(
        JsonElement element,
        string arrayPropertyName,
        string propertyToRemove)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(arrayPropertyName, out var arrayProperty)
            || arrayProperty.ValueKind != JsonValueKind.Array)
        {
            return element;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        foreach (var property in element.EnumerateObject())
        {
            if (!property.NameEquals(arrayPropertyName))
            {
                property.WriteTo(writer);
                continue;
            }

            writer.WritePropertyName(property.Name);
            writer.WriteStartArray();
            foreach (var item in arrayProperty.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    item.WriteTo(writer);
                    continue;
                }

                writer.WriteStartObject();
                foreach (var itemProperty in item.EnumerateObject())
                {
                    if (itemProperty.NameEquals(propertyToRemove))
                    {
                        continue;
                    }

                    itemProperty.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static int? TryGetProcessId(JsonElement? args)
    {
        if (args is not { } candidate
            || candidate.ValueKind != JsonValueKind.Object
            || !candidate.TryGetProperty("processId", out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var processId))
        {
            return null;
        }

        return processId;
    }

    private static bool ShouldIncludeNavigation(string toolName, JsonElement? args)
    {
        if (args is not { } candidate
            || candidate.ValueKind != JsonValueKind.Object
            || !candidate.TryGetProperty("navigation", out var property)
            || property.ValueKind != JsonValueKind.False)
        {
            return true;
        }

        return !NavigationOptOutTools.Contains(toolName);
    }

    private static bool TryGetBool(JsonElement? args, string propertyName)
    {
        return args is { } candidate
            && candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();
    }
}


