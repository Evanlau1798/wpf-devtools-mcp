namespace WpfDevTools.Shared.Configuration;

/// <summary>
/// Configuration settings for Inspector operations
/// Can be overridden via environment variables
/// </summary>
public static class InspectorConfig
{
    /// <summary>
    /// Timeout for UI thread operations (default: 5 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_UI_TIMEOUT_MS
    /// </summary>
    public static TimeSpan UIThreadTimeout { get; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_UI_TIMEOUT_MS",
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Timeout for Named Pipe connection (default: 5 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_PIPE_CONNECT_TIMEOUT_MS
    /// </summary>
    public static TimeSpan PipeConnectTimeout { get; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_PIPE_CONNECT_TIMEOUT_MS",
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Timeout for Inspector request/response (default: 30 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_REQUEST_TIMEOUT_MS
    /// </summary>
    public static TimeSpan RequestTimeout { get; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_REQUEST_TIMEOUT_MS",
        TimeSpan.FromSeconds(30));

    /// <summary>
    /// Timeout for idle or incomplete reads on an established inspector client session (default: 60 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_IDLE_TIMEOUT_MS
    /// </summary>
    public static TimeSpan IdleConnectionTimeout { get; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_IDLE_TIMEOUT_MS",
        TimeSpan.FromSeconds(60));

    /// <summary>
    /// Timeout for graceful shutdown (default: 5 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_SHUTDOWN_TIMEOUT_MS
    /// </summary>
    public static TimeSpan ShutdownTimeout { get; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_SHUTDOWN_TIMEOUT_MS",
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Heartbeat interval for connection health check (default: 10 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_HEARTBEAT_INTERVAL_MS
    /// </summary>
    public static TimeSpan HeartbeatInterval { get; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_HEARTBEAT_INTERVAL_MS",
        TimeSpan.FromSeconds(10));

    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(5);

    private static TimeSpan GetTimeoutFromEnv(string envVarName, TimeSpan defaultValue)
    {
        var envValue = Environment.GetEnvironmentVariable(envVarName);
        return ParseTimeout(envValue, defaultValue);
    }

    /// <summary>
    /// Parse and clamp a timeout value from a string (milliseconds).
    /// Clamps to MaxTimeout (5 minutes) to prevent misconfiguration.
    /// </summary>
    internal static TimeSpan ParseTimeout(string? envValue, TimeSpan defaultValue)
    {
        if (string.IsNullOrWhiteSpace(envValue))
        {
            return defaultValue;
        }

        if (int.TryParse(envValue, out var milliseconds) && milliseconds > 0)
        {
            var result = TimeSpan.FromMilliseconds(milliseconds);
            return result > MaxTimeout ? MaxTimeout : result;
        }

        return defaultValue;
    }
}
