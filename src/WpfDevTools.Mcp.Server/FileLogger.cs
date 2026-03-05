using System.Threading.Channels;
using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Async file-based logger for MCP Server using background queue
/// Logs to file instead of stdout to avoid interfering with JSON-RPC communication
/// Uses Channel for non-blocking async I/O to prevent thread pool starvation
/// Supports structured logging with correlation IDs and performance metrics
/// </summary>
public class FileLogger : IAsyncDisposable
{
    private readonly string _logFilePath;
    private readonly Channel<string> _logQueue;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _shutdownCts;
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxQueueCapacity = 10000; // Prevent unbounded memory growth

    /// <summary>
    /// Create a new FileLogger instance
    /// </summary>
    /// <param name="logFilePath">Optional path to log file. If null, creates file in temp directory.</param>
    public FileLogger(string? logFilePath = null)
    {
        _logFilePath = logFilePath ?? Path.Combine(
            Path.GetTempPath(),
            $"WpfDevTools_McpServer_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        // Create bounded channel to prevent memory exhaustion
        _logQueue = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest // Drop oldest if queue full
        });

        _shutdownCts = new CancellationTokenSource();
        _processingTask = Task.Run(() => ProcessLogQueueAsync(_shutdownCts.Token));
    }

    /// <summary>
    /// Log an informational message
    /// </summary>
    /// <param name="message">Message to log</param>
    public void LogInfo(string message)
    {
        Log("INFO", message);
    }

    /// <summary>
    /// Log an error message
    /// </summary>
    /// <param name="message">Message to log</param>
    public void LogError(string message)
    {
        Log("ERROR", message);
    }

    /// <summary>
    /// Log a debug message
    /// </summary>
    /// <param name="message">Message to log</param>
    public void LogDebug(string message)
    {
        Log("DEBUG", message);
    }

    /// <summary>
    /// Log a warning message
    /// </summary>
    /// <param name="message">Message to log</param>
    public void LogWarning(string message)
    {
        Log("WARNING", message);
    }

    /// <summary>
    /// Log a structured message with additional context fields for observability
    /// </summary>
    /// <param name="level">Log level (INFO, ERROR, etc.)</param>
    /// <param name="message">Log message</param>
    /// <param name="context">Additional structured context (will be serialized as JSON)</param>
    public void LogStructured(string level, string message, object? context = null)
    {
        try
        {
            var logEntry = new
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                level,
                message,
                context
            };

            var json = JsonSerializer.Serialize(logEntry);
            _logQueue.Writer.TryWrite(json + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Structured logging failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Log a request with structured fields for observability and performance tracking
    /// </summary>
    /// <param name="method">Method name (e.g., "get_visual_tree")</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="processId">Target process ID</param>
    /// <param name="durationMs">Request duration in milliseconds</param>
    /// <param name="success">Whether the request succeeded</param>
    /// <param name="error">Error message if failed</param>
    public void LogRequest(string method, string? correlationId, int? processId, long durationMs, bool success, string? error = null)
    {
        LogStructured("REQUEST", $"{method} completed", new
        {
            correlationId,
            method,
            processId,
            durationMs,
            success,
            error
        });
    }

    private void Log(string level, string message)
    {
        try
        {
            var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

            // Non-blocking write to channel (returns immediately)
            _logQueue.Writer.TryWrite(logEntry);
        }
        catch (Exception ex)
        {
            // Fallback to console if channel write fails
            Console.Error.WriteLine($"Logging failed: {ex.Message}");
        }
    }

    private async Task ProcessLogQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var logEntry in _logQueue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    RotateIfNeeded();
                    await File.AppendAllTextAsync(_logFilePath, logEntry, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown requested - exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logFilePath))
            {
                return;
            }

            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length >= MaxFileSizeBytes)
            {
                var oldPath = _logFilePath + ".old";

                // Delete existing .old file if present
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }

                File.Move(_logFilePath, oldPath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Log rotation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the path to the log file
    /// </summary>
    public string LogFilePath => _logFilePath;

    /// <summary>
    /// Dispose resources and flush remaining log entries
    /// </summary>
    /// <returns>ValueTask representing the async disposal operation</returns>
    public async ValueTask DisposeAsync()
    {
        // Signal shutdown
        _logQueue.Writer.Complete();

        // Wait for background task to finish processing remaining logs
        try
        {
            await _processingTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Force shutdown if taking too long
            _shutdownCts.Cancel();
        }

        _shutdownCts.Dispose();
    }
}
