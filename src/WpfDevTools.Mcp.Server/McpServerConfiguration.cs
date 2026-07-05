namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Centralized configuration constants for the MCP Server.
/// Extracted from various classes to improve maintainability and discoverability.
/// </summary>
public static class McpServerConfiguration
{
    public const string RawInjectionAllowedTargetsEnvVar = "WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS";
    public const string AllowedTargetsEnvVar = "WPFDEVTOOLS_MCP_ALLOWED_TARGETS";
    public const string AllowDestructiveToolsEnvVar = "WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS";
    public const string AllowScreenshotsEnvVar = "WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS";
    public const string AllowViewModelInspectionEnvVar = "WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION";
    public const string AllowSensitiveReadsEnvVar = "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS";
    public const string AllowProjectWritesEnvVar = "WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES";
    public const string AllowedProjectRootsEnvVar = "WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS";
    public const string SkipExistingHostReuseEnvVar = "WPFDEVTOOLS_MCP_SKIP_EXISTING_HOST_REUSE";
    public const string RateLimitRequestsPerMinuteEnvVar = "WPFDEVTOOLS_RATE_LIMIT_RPM";
    public const string TextFallbackModeEnvVar = "WPFDEVTOOLS_TEXT_FALLBACK_MODE";

    /// <summary>
    /// Default timeout for tool execution (except connect which has its own timeout).
    /// Prevents server hang if target process is frozen or unresponsive.
    /// </summary>
    public const int DefaultToolTimeoutSeconds = 5;

    /// <summary>
    /// Timeout for the connect tool specifically.
    /// Longer than default because DLL injection and initialization can take time.
    /// </summary>
    public const int ConnectTimeoutSeconds = 30;

    /// <summary>
    /// Timeout for ping operations.
    /// Should be fast since it's just a heartbeat check.
    /// </summary>
    public const int PingTimeoutSeconds = 5;

    /// <summary>
    /// Maximum number of concurrent sessions to prevent resource exhaustion.
    /// Each session holds: 1 NamedPipeClient + 1 RateLimiter + session metadata (~10KB per session).
    /// Total memory: ~500KB for session tracking (negligible).
    /// Limit primarily prevents accidental DoS via rapid connection attempts.
    /// </summary>
    public const int MaxSessions = 50;

    /// <summary>
    /// Idle timeout for sessions. Sessions with no activity for this duration are cleaned up.
    /// Prevents memory leaks from abandoned sessions.
    /// </summary>
    public static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Interval for periodic session cleanup.
    /// Checks for dead processes and idle sessions.
    /// </summary>
    public static readonly TimeSpan SessionCleanupInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Rate limit: maximum requests per minute per session.
    /// Prevents DoS attacks and accidental infinite loops in AI agents.
    /// </summary>
    public const int RateLimitRequestsPerMinute = 300;

    /// <summary>
    /// Maximum allowed RPM override value. Prevents environment variable abuse.
    /// </summary>
    public const int MaxRateLimitRequestsPerMinute = 10000;

    public static int GetConfiguredRateLimitRequestsPerMinute()
    {
        var overrideValue = Environment.GetEnvironmentVariable(RateLimitRequestsPerMinuteEnvVar);
        return int.TryParse(overrideValue, out var parsed) && parsed > 0
            ? Math.Min(parsed, MaxRateLimitRequestsPerMinute)
            : RateLimitRequestsPerMinute;
    }

    /// <summary>
    /// Named pipe connection timeout.
    /// How long to wait for pipe connection before giving up.
    /// </summary>
    public static readonly TimeSpan PipeConnectionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Named pipe read/write timeout.
    /// How long to wait for a single read or write operation.
    /// </summary>
    public static readonly TimeSpan PipeOperationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Short grace window for external SDK-only targets that are not allowlisted for raw injection.
    /// Gives a target-hosted InspectorSdk instance a chance to appear without consuming the full connect budget.
    /// </summary>
    public static readonly TimeSpan ExternalSdkHostReuseGracePeriod = TimeSpan.FromSeconds(2);
}
