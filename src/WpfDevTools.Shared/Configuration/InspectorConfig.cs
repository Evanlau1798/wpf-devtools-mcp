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
    public static TimeSpan UIThreadTimeout { get; internal set; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_UI_TIMEOUT_MS",
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Timeout for Named Pipe connection (default: 5 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_PIPE_CONNECT_TIMEOUT_MS
    /// </summary>
    public static TimeSpan PipeConnectTimeout { get; internal set; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_PIPE_CONNECT_TIMEOUT_MS",
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Timeout for Inspector request/response (default: 30 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_REQUEST_TIMEOUT_MS
    /// </summary>
    public static TimeSpan RequestTimeout { get; internal set; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_REQUEST_TIMEOUT_MS",
        TimeSpan.FromSeconds(30));

    /// <summary>
    /// Timeout for graceful shutdown (default: 5 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_SHUTDOWN_TIMEOUT_MS
    /// </summary>
    public static TimeSpan ShutdownTimeout { get; internal set; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_SHUTDOWN_TIMEOUT_MS",
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Heartbeat interval for connection health check (default: 10 seconds)
    /// Override with environment variable: WPF_DEVTOOLS_HEARTBEAT_INTERVAL_MS
    /// </summary>
    public static TimeSpan HeartbeatInterval { get; internal set; } = GetTimeoutFromEnv(
        "WPF_DEVTOOLS_HEARTBEAT_INTERVAL_MS",
        TimeSpan.FromSeconds(10));

    private static TimeSpan GetTimeoutFromEnv(string envVarName, TimeSpan defaultValue)
    {
        var envValue = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrWhiteSpace(envValue))
        {
            return defaultValue;
        }

        if (int.TryParse(envValue, out var milliseconds) && milliseconds > 0)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        return defaultValue;
    }
}
