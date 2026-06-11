using System.Threading.Channels;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Shared.Utilities;

/// <summary>
/// Async file-based logger using background queue.
/// Uses Channel for non-blocking async I/O to prevent thread pool starvation.
/// Supports structured logging with correlation IDs and performance metrics.
/// Implements both IDisposable (for sync contexts) and IAsyncDisposable (for async contexts).
/// </summary>
public sealed class FileLogger : IDisposable, IAsyncDisposable
{
    private readonly string _logFilePath;
    private readonly Channel<string> _logQueue;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _shutdownCts;
    private readonly TimeSpan _shutdownTimeout;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task>? _writeEntriesOverride;
    private Exception? _lastShutdownError;
    private int _droppedEntries;
    private int _disposeState;
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxQueueCapacity = 10000;

    /// <summary>
    /// Minimum log level. Messages below this level are silently discarded.
    /// Default: Info (Debug messages are skipped unless explicitly lowered).
    /// </summary>
    public FileLogLevel MinimumLevel { get; set; } = FileLogLevel.Info;

    /// <summary>
    /// Returns true when the provided level would be written by this logger.
    /// </summary>
    public bool IsEnabled(FileLogLevel level) => level >= MinimumLevel;

    /// <summary>
    /// Create a new FileLogger instance
    /// </summary>
    /// <param name="logFilePath">Optional path to log file. If null, creates file in temp directory.</param>
    public FileLogger(string? logFilePath = null)
        : this(logFilePath, InspectorConfig.ShutdownTimeout, writeEntriesOverride: null)
    {
    }

    internal FileLogger(
        string? logFilePath,
        TimeSpan shutdownTimeout,
        Func<IReadOnlyList<string>, CancellationToken, Task>? writeEntriesOverride)
    {
        _logFilePath = logFilePath ?? Path.Combine(
            Path.GetTempPath(),
            $"WpfDevTools_McpServer_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");

        _logQueue = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _shutdownCts = new CancellationTokenSource();
        _shutdownTimeout = shutdownTimeout > TimeSpan.Zero ? shutdownTimeout : InspectorConfig.ShutdownTimeout;
        _writeEntriesOverride = writeEntriesOverride;
        _processingTask = Task.Factory
            .StartNew(
                () => ProcessLogQueueAsync(_shutdownCts.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap();
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
    /// Log a structured message with additional context fields
    /// </summary>
    /// <param name="level">Log level (INFO, ERROR, etc.)</param>
    /// <param name="message">Log message</param>
    /// <param name="context">Additional structured context (serialized as JSON)</param>
    public void LogStructured(string level, string message, object? context = null)
    {
        if (!IsLevelEnabled(level))
            return;

        try
        {
            var logEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                level,
                message,
                context
            };

            EnqueueLogEntry(SensitiveLogRedactor.Redact(JsonSerializer.Serialize(logEntry)) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Structured logging failed: {SensitiveLogRedactor.Redact(ex.Message)}");
        }
    }

    /// <summary>
    /// Log a request with structured fields for observability
    /// </summary>
    /// <param name="method">Method name</param>
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
        if (!IsLevelEnabled(level))
            return;

        try
        {
            var logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{level}] {SensitiveLogRedactor.Redact(message)}{Environment.NewLine}";
            EnqueueLogEntry(logEntry);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Logging failed: {SensitiveLogRedactor.Redact(ex.Message)}");
        }
    }

    private void EnqueueLogEntry(string logEntry)
    {
        if (!_logQueue.Writer.TryWrite(logEntry))
        {
            Interlocked.Increment(ref _droppedEntries);
            return;
        }

        FlushDroppedEntryWarning();
    }

    private void FlushDroppedEntryWarning()
    {
        var droppedEntries = Interlocked.Exchange(ref _droppedEntries, 0);
        if (droppedEntries <= 0)
        {
            return;
        }

        var warning = JsonSerializer.Serialize(new
        {
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            level = "WARNING",
            message = "Dropped log entries due to logger backpressure",
            context = new { droppedEntries }
        }) + Environment.NewLine;

        if (!_logQueue.Writer.TryWrite(warning))
        {
            Interlocked.Add(ref _droppedEntries, droppedEntries);
        }
    }

    private async Task ProcessLogQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
#if NET48
            while (!cancellationToken.IsCancellationRequested)
            {
                var canRead = await _logQueue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                if (!ShouldContinueNet48ConsumerLoop(canRead, cancellationToken))
                {
                    break;
                }

                while (_logQueue.Reader.TryRead(out var logEntry))
                {
                    try
                    {
                        var entries = DrainLogEntries(logEntry);
                        await WriteEntriesCoreAsync(entries, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine($"Logging failed: {SensitiveLogRedactor.Redact(ex.Message)}");
                    }
                }
            }
#else
            await foreach (var logEntry in _logQueue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var entries = DrainLogEntries(logEntry);
                    await WriteEntriesCoreAsync(entries, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {SensitiveLogRedactor.Redact(ex.Message)}");
                }
            }
#endif
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private Task WriteEntriesCoreAsync(IReadOnlyList<string> entries, CancellationToken cancellationToken)
        => _writeEntriesOverride?.Invoke(entries, cancellationToken)
           ?? WriteEntriesAsync(entries, cancellationToken);

    private List<string> DrainLogEntries(string firstEntry)
    {
        var entries = new List<string> { firstEntry };
        while (_logQueue.Reader.TryRead(out var queuedEntry))
        {
            entries.Add(queuedEntry);
        }

        return entries;
    }

    private async Task WriteEntriesAsync(IReadOnlyList<string> entries, CancellationToken cancellationToken)
    {
        StreamWriter? writer = null;
        try
        {
            long currentFileBytes = File.Exists(_logFilePath)
                ? new FileInfo(_logFilePath).Length
                : 0;

            foreach (var entry in entries)
            {
                var entryBytes = Encoding.UTF8.GetByteCount(entry);

                if (currentFileBytes > 0 && currentFileBytes + entryBytes >= MaxFileSizeBytes)
                {
                    if (writer is not null)
                    {
                        await writer.FlushAsync().ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.Dispose();
                        writer = null;
                    }

                    RotateFile();
                    currentFileBytes = 0;
                }

                writer ??= CreateStreamWriter();
                await writer.WriteAsync(entry).ConfigureAwait(false);
                currentFileBytes += entryBytes;

                if (currentFileBytes >= MaxFileSizeBytes)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.Dispose();
                    writer = null;
                    RotateFile();
                    currentFileBytes = 0;
                }
            }

            if (writer is not null)
            {
                await writer.FlushAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private StreamWriter CreateStreamWriter()
    {
        return new StreamWriter(
            new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read),
            Encoding.UTF8);
    }

    private void RotateFile()
    {
        try
        {
            if (!File.Exists(_logFilePath))
                return;

            var oldPath = _logFilePath + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);

            File.Move(_logFilePath, oldPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Log rotation failed: {SensitiveLogRedactor.Redact(ex.Message)}");
        }
    }

    /// <summary>
    /// Gets the path to the log file
    /// </summary>
    public string LogFilePath => _logFilePath;

    internal Task ProcessingTaskForTesting => _processingTask;
    internal Exception? LastShutdownErrorForTesting => _lastShutdownError;

    internal static bool ShouldContinueNet48ConsumerLoop(bool canRead, CancellationToken cancellationToken)
        => canRead && !cancellationToken.IsCancellationRequested;

    internal static TimeSpan GetRemainingShutdownTimeout(TimeSpan shutdownTimeout, TimeSpan elapsed)
        => FileLoggerShutdownCoordinator.GetRemainingShutdownTimeout(shutdownTimeout, elapsed);

    /// <summary>
    /// Synchronous dispose - signals shutdown and waits for flush
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _logQueue.Writer.TryComplete();

        try
        {
            _lastShutdownError = WaitForProcessingTaskShutdown();
            ReportShutdownError(_lastShutdownError);
        }
        finally
        {
            _shutdownCts.Dispose();
        }
    }

    /// <summary>
    /// Async dispose - signals shutdown and waits for flush
    /// </summary>
    public
#if NET48
        ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return default;
        }

        _logQueue.Writer.TryComplete();

        try
        {
            _lastShutdownError = WaitForProcessingTaskShutdown();
            ReportShutdownError(_lastShutdownError);
        }
        finally
        {
            _shutdownCts.Dispose();
        }
        return default;
    }
#else
        async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _logQueue.Writer.TryComplete();

        try
        {
            _lastShutdownError = await WaitForProcessingTaskShutdownAsync().ConfigureAwait(false);
            ReportShutdownError(_lastShutdownError);
        }
        finally
        {
            _shutdownCts.Dispose();
        }
    }
#endif

    private Exception? WaitForProcessingTaskShutdown()
        => FileLoggerShutdownCoordinator.WaitForProcessingTaskShutdown(
            _processingTask,
            _shutdownCts,
            _shutdownTimeout);

    private async Task<Exception?> WaitForProcessingTaskShutdownAsync()
        => await FileLoggerShutdownCoordinator.WaitForProcessingTaskShutdownAsync(
            _processingTask,
            _shutdownCts,
            _shutdownTimeout).ConfigureAwait(false);

    private static void ReportShutdownError(Exception? shutdownError)
    {
        if (shutdownError == null)
        {
            return;
        }

        Trace.TraceWarning($"FileLogger shutdown warning: {SensitiveLogRedactor.Redact(shutdownError.Message)}");
    }

    private bool IsLevelEnabled(string level)
    {
        var numericLevel = level switch
        {
            _ when level.Equals("DEBUG", StringComparison.OrdinalIgnoreCase) => FileLogLevel.Debug,
            _ when level.Equals("INFO", StringComparison.OrdinalIgnoreCase) => FileLogLevel.Info,
            _ when level.Equals("INFORMATION", StringComparison.OrdinalIgnoreCase) => FileLogLevel.Info,
            _ when level.Equals("WARNING", StringComparison.OrdinalIgnoreCase) => FileLogLevel.Warning,
            _ when level.Equals("WARN", StringComparison.OrdinalIgnoreCase) => FileLogLevel.Warning,
            _ when level.Equals("ERROR", StringComparison.OrdinalIgnoreCase) => FileLogLevel.Error,
            _ when level.Equals("REQUEST", StringComparison.OrdinalIgnoreCase) => FileLogLevel.Info,
            _ => FileLogLevel.Info
        };
        return numericLevel >= MinimumLevel;
    }
}

/// <summary>
/// Log level threshold for FileLogger filtering.
/// </summary>
public enum FileLogLevel
{
    /// <summary>Debug level</summary>
    Debug = 0,
    /// <summary>Informational level</summary>
    Info = 1,
    /// <summary>Warning level</summary>
    Warning = 2,
    /// <summary>Error level</summary>
    Error = 3
}
