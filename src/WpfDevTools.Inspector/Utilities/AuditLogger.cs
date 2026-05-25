using System.Diagnostics;
using System.Security;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Audit logger service interface for dependency injection
/// </summary>
public interface IAuditLoggerService
{
    /// <summary>
    /// Log a security audit event
    /// </summary>
    /// <param name="category">Event category</param>
    /// <param name="message">Event message</param>
    /// <param name="severity">Event severity level</param>
    void LogSecurityEvent(string category, string message, AuditSeverity severity = AuditSeverity.Information);
}

/// <summary>
/// Instance-based audit logger service (recommended for DI)
/// </summary>
public class AuditLoggerService : IAuditLoggerService
{
    private readonly IAuditLogger _logger;

    /// <summary>
    /// Create a new AuditLoggerService instance
    /// </summary>
    /// <param name="logger">Underlying audit logger implementation</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null</exception>
    public AuditLoggerService(IAuditLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Log a security audit event
    /// </summary>
    /// <param name="category">Event category</param>
    /// <param name="message">Event message</param>
    /// <param name="severity">Event severity level</param>
    public void LogSecurityEvent(string category, string message, AuditSeverity severity = AuditSeverity.Information)
    {
        _logger.Log(category, SensitiveLogRedactor.Redact(message), severity);
    }
}

/// <summary>
/// Static audit logger facade (for backward compatibility)
/// NOTE: Prefer using IAuditLoggerService with DI for new code
/// </summary>
[Obsolete("Use IAuditLoggerService with dependency injection. The static AuditLogger facade is retained only for backward compatibility and uses process-wide mutable state.", false)]
public static class AuditLogger
{
    private static IAuditLoggerService _service = AuditLoggerDefaults.CreateService();
    private static readonly object _lock = new object();

    /// <summary>
    /// Initialize audit logger with specific implementation
    /// </summary>
    public static void Initialize(IAuditLogger logger)
    {
        SetLogger(logger);
    }

    internal static void InitializeForTesting(IAuditLogger logger)
    {
        SetLogger(logger);
    }

    internal static void ResetForTesting()
    {
        lock (_lock)
        {
            _service = AuditLoggerDefaults.CreateService();
        }
    }

    /// <summary>
    /// Log security audit event
    /// </summary>
    public static void LogSecurityEvent(string category, string message, AuditSeverity severity = AuditSeverity.Information)
    {
        lock (_lock)
        {
            _service.LogSecurityEvent(category, message, severity);
        }
    }

    private static void SetLogger(IAuditLogger logger)
    {
        lock (_lock)
        {
            _service = new AuditLoggerService(logger);
        }
    }
}

internal static class AuditLoggerDefaults
{
    public static IAuditLoggerService CreateService() => new AuditLoggerService(new TraceAuditLogger());
}

/// <summary>
/// Audit logger interface
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Log an audit event
    /// </summary>
    /// <param name="category">Event category</param>
    /// <param name="message">Event message</param>
    /// <param name="severity">Event severity level</param>
    void Log(string category, string message, AuditSeverity severity);
}

/// <summary>
/// Audit severity levels
/// </summary>
public enum AuditSeverity
{
    /// <summary>Informational event</summary>
    Information,
    /// <summary>Warning event</summary>
    Warning,
    /// <summary>Error event</summary>
    Error
}

/// <summary>
/// Trace-based audit logger (for development)
/// </summary>
public class TraceAuditLogger : IAuditLogger
{
    /// <summary>
    /// Log an audit event to System.Diagnostics.Trace
    /// </summary>
    /// <param name="category">Event category</param>
    /// <param name="message">Event message</param>
    /// <param name="severity">Event severity level</param>
    public void Log(string category, string message, AuditSeverity severity)
    {
        var severityStr = severity switch
        {
            AuditSeverity.Warning => "WARNING",
            AuditSeverity.Error => "ERROR",
            _ => "INFO"
        };

        Trace.WriteLine($"[AUDIT:{severityStr}] [{category}] {SensitiveLogRedactor.Redact(message)}");
    }
}

internal interface IEventLogOperations
{
    bool SourceExists(string source);

    void CreateEventSource(string source, string logName);

    void WriteEntry(string source, string message, EventLogEntryType eventType, int eventId);
}

internal sealed class SystemEventLogOperations : IEventLogOperations
{
    public bool SourceExists(string source) => EventLog.SourceExists(source);

    public void CreateEventSource(string source, string logName) => EventLog.CreateEventSource(source, logName);

    public void WriteEntry(string source, string message, EventLogEntryType eventType, int eventId) =>
        EventLog.WriteEntry(source, message, eventType, eventId);
}

/// <summary>
/// Windows Event Log audit logger (for production)
/// Requires event source registration or admin privileges
/// </summary>
public class EventLogAuditLogger : IAuditLogger
{
    private const string EventSource = "WpfDevTools";
    private const string EventLog = "Application";
    private static readonly TimeSpan SourceAvailabilityRetryInterval = TimeSpan.FromMinutes(5);

    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly IEventLogOperations _eventLogOperations;
    private readonly object _sourceLock = new object();
    private bool _sourceAvailable;
    private DateTimeOffset _nextSourceCheckUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Create a Windows Event Log audit logger.
    /// </summary>
    public EventLogAuditLogger()
        : this(() => DateTimeOffset.UtcNow, new SystemEventLogOperations())
    {
    }

    internal EventLogAuditLogger(Func<DateTimeOffset> utcNowProvider, IEventLogOperations eventLogOperations)
    {
        _utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
        _eventLogOperations = eventLogOperations ?? throw new ArgumentNullException(nameof(eventLogOperations));
    }

    /// <summary>
    /// Log an audit event to Windows Event Log
    /// </summary>
    /// <param name="category">Event category</param>
    /// <param name="message">Event message</param>
    /// <param name="severity">Event severity level</param>
    public void Log(string category, string message, AuditSeverity severity)
    {
        try
        {
            if (!EnsureEventSourceExists())
            {
                // Fallback to Trace if Event Log not available
                Trace.WriteLine($"[AUDIT] [{category}] {SensitiveLogRedactor.Redact(message)}");
                return;
            }

            var eventType = severity switch
            {
                AuditSeverity.Error => EventLogEntryType.Error,
                AuditSeverity.Warning => EventLogEntryType.Warning,
                _ => EventLogEntryType.Information
            };

            var fullMessage = $"[{category}] {SensitiveLogRedactor.Redact(message)}\nUser: {Environment.UserName}\nProcess: {Process.GetCurrentProcess().ProcessName}";

            _eventLogOperations.WriteEntry(EventSource, fullMessage, eventType, 1001);
        }
        catch (Exception ex)
        {
            // Fallback to Trace on any error
            Trace.WriteLine($"[AUDIT] Failed to write to Event Log: {SensitiveLogRedactor.Redact(ex.Message)}");
            Trace.WriteLine($"[AUDIT] [{category}] {SensitiveLogRedactor.Redact(message)}");
        }
    }

    private bool EnsureEventSourceExists()
    {
        lock (_sourceLock)
        {
            if (_sourceAvailable)
                return true;

            var now = _utcNowProvider();
            if (now < _nextSourceCheckUtc)
                return false;

            try
            {
                if (!_eventLogOperations.SourceExists(EventSource))
                {
                    // Creating event source requires admin privileges
                    // If this fails, we'll fallback to Trace
                    _eventLogOperations.CreateEventSource(EventSource, EventLog);
                }
                _sourceAvailable = true;
                return true;
            }
            catch (SecurityException)
            {
                // No admin privileges or source registration access. Use Trace fallback and
                // retry later so long-running processes can recover after source registration.
                _sourceAvailable = false;
                _nextSourceCheckUtc = now + SourceAvailabilityRetryInterval;
                Trace.WriteLine(
                    $"[AUDIT] Event Log source '{EventSource}' not available, using Trace fallback; retrying after {SourceAvailabilityRetryInterval.TotalMinutes:0} minutes");
                return false;
            }
        }
    }
}
