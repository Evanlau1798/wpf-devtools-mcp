using Microsoft.Extensions.Logging;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Adapts the existing FileLogger to Microsoft.Extensions.Logging ILoggerProvider.
/// Enables integration with the SDK's logging pipeline while preserving
/// the Channel-based async I/O and log rotation capabilities of FileLogger.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLogger _fileLogger;

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
        new FileLoggerAdapter(_fileLogger, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
        // FileLogger lifecycle managed externally (registered as singleton in DI)
    }

    private sealed class FileLoggerAdapter : ILogger
    {
        private readonly FileLogger _fileLogger;
        private readonly string _categoryName;

        public FileLoggerAdapter(FileLogger fileLogger, string categoryName)
        {
            _fileLogger = fileLogger;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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
