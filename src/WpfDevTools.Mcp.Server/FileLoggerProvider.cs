using Microsoft.Extensions.Logging;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Adapts the existing FileLogger to Microsoft.Extensions.Logging ILoggerProvider.
/// Enables integration with the SDK's logging pipeline while preserving
/// the Channel-based async I/O and log rotation capabilities of FileLogger.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private readonly FileLogger _fileLogger;
    private volatile bool _disposed;

    /// <summary>
    /// Creates a new FileLoggerProvider wrapping an existing FileLogger instance
    /// </summary>
    /// <param name="fileLogger">The FileLogger instance to delegate logging to</param>
    public FileLoggerProvider(FileLogger fileLogger)
    {
        _fileLogger = fileLogger ?? throw new ArgumentNullException(nameof(fileLogger));
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        new FileLoggerAdapter(this, _fileLogger, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        // FileLogger lifecycle managed externally (registered as singleton in DI,
        // disposed in Program.cs finally block)
    }

    /// <summary>
    /// Async dispose signals adapters to stop writing, preventing log loss during shutdown
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class FileLoggerAdapter : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly FileLogger _fileLogger;
        private readonly string _categoryName;

        public FileLoggerAdapter(FileLoggerProvider provider, FileLogger fileLogger, string categoryName)
        {
            _provider = provider;
            _fileLogger = fileLogger;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            !_provider._disposed && logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = $"[{_categoryName}] {formatter(state, exception)}";

            if (exception != null)
            {
                message += $"\n{exception}";
            }

            switch (logLevel)
            {
                case LogLevel.Error:
                case LogLevel.Critical:
                    _fileLogger.LogError(message);
                    break;
                case LogLevel.Warning:
                    _fileLogger.LogWarning(message);
                    break;
                default:
                    _fileLogger.LogInfo(message);
                    break;
            }
        }
    }
}
