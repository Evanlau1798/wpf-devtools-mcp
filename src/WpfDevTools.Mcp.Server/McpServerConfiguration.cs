namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Centralized configuration constants for the MCP Server.
/// Extracted from various classes to improve maintainability and discoverability.
/// </summary>
public static class McpServerConfiguration
{
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
    public const int RateLimitRequestsPerMinute = 100;

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
}
