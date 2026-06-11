using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Schema;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Mcp.Server.McpTools;

/// <summary>
/// Helper for bridging MCP SDK tool methods to existing tool ExecuteAsync implementations.
/// Converts typed parameters to JsonElement and wraps results as CallToolResult.
/// </summary>
/// <remarks>
/// <para>
/// This type is intentionally a <c>static partial class</c>. Stateless tools may use a
/// process-wide cache, while tools that capture <see cref="SessionManager"/> use a
/// weakly keyed host-scoped cache. Metrics collector, navigation planner, and
/// navigation-opt-out state remain process-wide and are wired once during DI
/// initialization.
/// </para>
/// <para>
/// Do NOT attempt to multi-tenant metrics or navigation behavior without first converting
/// those remaining static services to DI-scoped instances or a per-request context.
/// </para>
/// </remarks>
public static partial class ToolCallHelper
{
    private const int WaitForDpChangeTimeoutHeadroomSeconds = 2;

    private static readonly ConcurrentDictionary<string, object> GlobalToolCache = new();
    private static ConditionalWeakTable<SessionManager, ConcurrentDictionary<string, object>> HostToolCaches = new();
    private static readonly AsyncLocal<ConcurrentDictionary<string, object>?> ToolCacheOverride = new();
    private static readonly AsyncLocal<MetricsCollector?> MetricsCollectorOverride = new();
    private static readonly AsyncLocal<ToolNavigationPlanner?> NavigationPlannerOverride = new();
    private static readonly HashSet<string> NavigationOptOutTools = new(StringComparer.Ordinal)
    {
        "get_binding_errors"
    };
    private static readonly HashSet<string> TimeoutReconnectOptOutTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connect",
        "GetProcesses",
        "GetActiveProcess",
        "SelectActiveProcess"
    };
    private static readonly string[] RecoveryCompatibilityFields =
    [
        "hint",
        "suggestedAction",
        "requiresReconnect",
        "stateAfterTimeoutUnknown",
        "processId",
        "timeoutSeconds",
        "retryAfterSeconds",
        "retryAfter",
        "availableTokens",
        "availableEvents"
    ];
    private static readonly ToolNavigationPlanner DefaultNavigationPlanner = new(new ToolNavigationRegistry());
    private static MetricsCollector? _metrics;

    private static readonly Annotations ErrorAnnotations = new() { Priority = 1.0f };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static ConcurrentDictionary<string, object> CurrentToolCache =>
        ToolCacheOverride.Value ?? GlobalToolCache;

    private static MetricsCollector? CurrentMetricsCollector =>
        ToolCacheOverride.Value is not null
            ? MetricsCollectorOverride.Value
            : _metrics;

    private static ToolNavigationPlanner CurrentNavigationPlanner =>
        NavigationPlannerOverride.Value ?? DefaultNavigationPlanner;

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
        var effectiveTimeoutSeconds = ResolveExecutionTimeoutSeconds(toolName, args, timeoutSeconds);
        var includeNavigation = ShouldIncludeNavigation(toolName, args);
        var metricsCollector = CurrentMetricsCollector;

        if (!BoundaryParameterValidator.TryValidateStringBoundaries(args, out var boundaryError))
        {
            var payload = NormalizeToolPayload(
                toolName,
                args,
                JsonSerializer.SerializeToElement(boundaryError, SerializerOptions),
                includeNavigation ? ToolNavigationEnvelope.Empty : null);
            RecordRequestMetrics(metricsCollector, toolName, 0, success: false, payload);

            return new CallToolResult()
            {
                Content = [CreateTextContentBlock(payload, isError: true)],
                StructuredContent = payload,
                IsError = true
            };
        }

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
            var navigation = includeNavigation
                ? CurrentNavigationPlanner.PlanEnvelope(toolName, jsonElement, args, navigationState)
                : null;
            jsonElement = NormalizeToolPayload(toolName, args, jsonElement, navigation);
            var isError = IsToolResultError(jsonElement);

            RecordRequestMetrics(metricsCollector, toolName, sw.ElapsedMilliseconds, !isError, jsonElement);

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
            // Timeout occurred (our CTS was cancelled, but caller's token was not)
            var timeoutRecovery = ResolveTimeoutRecovery(toolName, args, TryFindCapturedSessionManager(execute));
            var timeoutPayloadData = new Dictionary<string, object?>
            {
                ["success"] = false,
                ["error"] = $"Tool execution timed out after {effectiveTimeoutSeconds} seconds. Target process may be frozen or unresponsive.",
                ["errorCode"] = "Timeout",
                ["toolName"] = toolName,
                ["timeoutSeconds"] = effectiveTimeoutSeconds,
                ["suggestedAction"] = timeoutRecovery.RequiresReconnect
                    ? timeoutRecovery.ProcessId is int processId
                        ? $"Reconnect to process {processId} and retry after confirming the target is responsive."
                        : "Reconnect and retry after confirming the target is responsive."
                    : "Check target responsiveness, then retry the tool or reconnect if the session may be stale."
            };

            if (timeoutRecovery.RequiresReconnect)
            {
                timeoutPayloadData["requiresReconnect"] = true;
                timeoutPayloadData["stateAfterTimeoutUnknown"] = true;
            }

            if (timeoutRecovery.ProcessId is int timedOutProcessId)
            {
                timeoutPayloadData["processId"] = timedOutProcessId;
            }

            var timeoutPayload = NormalizeToolPayload(
                toolName,
                args,
                JsonSerializer.SerializeToElement(timeoutPayloadData, SerializerOptions),
                includeNavigation ? ToolNavigationEnvelope.Empty : null);
            RecordRequestMetrics(metricsCollector, toolName, sw.ElapsedMilliseconds, false, timeoutPayload);

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
            // Classify exception into stable error code and sanitized message
            // to prevent localized OS text or internal details from leaking to clients
            var (errorCode, sanitizedMessage) = ClassifyException(ex);

            var exceptionPayload = NormalizeToolPayload(toolName, args, JsonSerializer.SerializeToElement(new
            {
                success = false,
                error = sanitizedMessage,
                errorCode
            }, SerializerOptions), includeNavigation ? ToolNavigationEnvelope.Empty : null);
            RecordRequestMetrics(metricsCollector, toolName, sw.ElapsedMilliseconds, false, exceptionPayload);

            return new CallToolResult()
            {
                Content = [CreateTextContentBlock(exceptionPayload, isError: true)],
                StructuredContent = exceptionPayload,
                IsError = true
            };
        }

    }

    private static SessionManager? TryFindCapturedSessionManager(Delegate execute)
    {
        return TryFindCapturedSessionManager(execute.Target, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static SessionManager? TryFindCapturedSessionManager(object? target, HashSet<object> visited)
    {
        if (target is null || target is string || !visited.Add(target))
        {
            return null;
        }

        if (target is SessionManager sessionManager)
        {
            return sessionManager;
        }

        var targetType = target.GetType();
        if (targetType.IsPrimitive || targetType.IsEnum)
        {
            return null;
        }

        foreach (var field in targetType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (typeof(SessionManager).IsAssignableFrom(field.FieldType)
                && field.GetValue(target) is SessionManager fieldSessionManager)
            {
                return fieldSessionManager;
            }

            if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string))
            {
                continue;
            }

            var nested = TryFindCapturedSessionManager(field.GetValue(target), visited);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    internal static CallToolResult CreateStructuredErrorResult(
        string error,
        string errorCode,
        string? hint = null,
        string? suggestedAction = null)
    {
        var payload = NormalizeToolPayload("structured_error", null, JsonSerializer.SerializeToElement(new
        {
            success = false,
            error,
            errorCode,
            hint,
            suggestedAction
        }, SerializerOptions), ToolNavigationEnvelope.Empty);

        return new CallToolResult()
        {
            Content = [CreateTextContentBlock(payload, isError: true)],
            StructuredContent = payload,
            IsError = true
        };
    }

    internal static int ResolveExecutionTimeoutSeconds(
        string toolName,
        JsonElement? args,
        int? timeoutSeconds)
    {
        if (timeoutSeconds.HasValue)
        {
            return timeoutSeconds.Value;
        }

        if (IsWaitForDpChangeTool(toolName)
            )
        {
            var requestedTimeoutMs = TryGetPositiveIntArg(args, "timeoutMs", out var timeoutMs)
                ? timeoutMs
                : DpChangeWaitLimits.DefaultTimeoutMs;
            var requestedTimeoutSeconds = (int)Math.Ceiling(requestedTimeoutMs / 1000d);
            return Math.Max(
                McpServerConfiguration.DefaultToolTimeoutSeconds,
                requestedTimeoutSeconds + WaitForDpChangeTimeoutHeadroomSeconds);
        }

        return McpServerConfiguration.DefaultToolTimeoutSeconds;
    }

    private static bool IsWaitForDpChangeTool(string toolName)
    {
        return string.Equals(toolName, "WaitForDpChange", StringComparison.Ordinal)
            || string.Equals(toolName, "wait_for_dp_change", StringComparison.Ordinal)
            || string.Equals(toolName, "WaitForDpChangeAfterMutation", StringComparison.Ordinal)
            || string.Equals(toolName, "wait_for_dp_change_after_mutation", StringComparison.Ordinal);
    }

    private static bool TryGetPositiveIntArg(JsonElement? args, string propertyName, out int value)
    {
        value = 0;

        if (!args.HasValue
            || !args.Value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var parsed)
            || parsed <= 0)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>
    /// Classify an exception into a stable error code and sanitized message.
    /// Prevents localized OS text or internal implementation details from leaking to clients.
    /// </summary>
    internal static (string errorCode, string message) ClassifyException(Exception ex)
    {
        return ex switch
        {
            TimeoutException => ("Timeout", "Operation timed out"),
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
    /// Get or create a process-wide cached tool instance for tools that do not capture host state.
    /// <para>
    /// IMPORTANT: Use <see cref="CachedTool{T}(SessionManager, string, Func{T})"/> for tools
    /// that capture <see cref="SessionManager"/> or any other host-scoped service.
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
        => (T)CurrentToolCache.GetOrAdd(key, _ => factory());

    /// <summary>
    /// Get or create a cached tool instance scoped to a host SessionManager.
    /// </summary>
    /// <remarks>
    /// Tool instances usually hold a SessionManager reference. Keeping those instances in
    /// a process-wide cache would leak the first host into later same-process hosts. The
    /// host cache is weakly keyed so disposing a SessionManager does not leave a permanent
    /// cache root.
    /// </remarks>
    internal static T CachedTool<T>(SessionManager sessionManager, string key, Func<T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(factory);

        var cache = ToolCacheOverride.Value ?? HostToolCaches.GetValue(sessionManager, static _ => new ConcurrentDictionary<string, object>());
        return (T)cache.GetOrAdd(key, _ => factory());
    }

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
            var endedSessionId = activeTrace.FollowUpExpiresAtUtc.HasValue
                ? activeTrace.SessionId
                : null;
            sessionManager.ClearActiveTraceState(processId.Value, endedSessionId);
            return state with
            {
                ActiveTrace = null,
                LastEndedTraceSessionId = endedSessionId
            };
        }

        return state;
    }

}
