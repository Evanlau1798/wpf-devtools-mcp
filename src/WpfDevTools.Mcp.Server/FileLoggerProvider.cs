using Microsoft.Extensions.Logging;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Adapts the existing FileLogger to Microsoft.Extensions.Logging ILoggerProvider.
/// Enables integration with the SDK's logging pipeline while preserving
/// the Channel-based async I/O and log rotation capabilities of FileLogger.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider, IAsyncDisposable, ISupportExternalScope
{
    private readonly FileLogger _fileLogger;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
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
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    /// <inheritdoc />
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

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            _provider._scopeProvider.Push(state);

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
            {
                return;
            }

            var message = $"[{_categoryName}] {formatter(state, exception)}";
            var scopes = CaptureScopes();
            var structuredState = state as IEnumerable<KeyValuePair<string, object?>>;

            _fileLogger.LogStructured(MapLevel(logLevel), message, new
            {
                category = _categoryName,
                eventId = eventId.Id,
                eventName = eventId.Name,
                state = structuredState,
                scopes,
                exception = exception?.ToString()
            });
        }

        private List<string> CaptureScopes()
        {
            var scopes = new List<string>();
            _provider._scopeProvider.ForEachScope((scope, state) => state.Add(scope?.ToString() ?? string.Empty), scopes);
            return scopes;
        }

        private static string MapLevel(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Critical => "CRITICAL",
            LogLevel.Error => "ERROR",
            LogLevel.Warning => "WARNING",
            _ => "INFO"
        };
    }
}
