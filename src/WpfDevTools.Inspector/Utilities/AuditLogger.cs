using System.Diagnostics;

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
        _logger.Log(category, message, severity);
    }
}

/// <summary>
/// Static audit logger facade (for backward compatibility)
/// NOTE: Prefer using IAuditLoggerService with DI for new code
/// </summary>
public static class AuditLogger
{
    private static IAuditLoggerService _service = new AuditLoggerService(new TraceAuditLogger());
    private static readonly object _lock = new object();

    /// <summary>
    /// Initialize audit logger with specific implementation
    /// </summary>
    public static void Initialize(IAuditLogger logger)
    {
        lock (_lock)
        {
            _service = new AuditLoggerService(logger);
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

        Trace.WriteLine($"[AUDIT:{severityStr}] [{category}] {message}");
    }
}

/// <summary>
/// Windows Event Log audit logger (for production)
/// Requires event source registration or admin privileges
/// </summary>
public class EventLogAuditLogger : IAuditLogger
{
    private const string EventSource = "WpfDevTools";
    private const string EventLog = "Application";
    private static bool _sourceChecked = false;
    private static bool _sourceAvailable = false;
    private static readonly object _sourceLock = new object();

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
            EnsureEventSourceExists();

            if (!_sourceAvailable)
            {
                // Fallback to Trace if Event Log not available
                Trace.WriteLine($"[AUDIT] [{category}] {message}");
                return;
            }

            var eventType = severity switch
            {
                AuditSeverity.Error => EventLogEntryType.Error,
                AuditSeverity.Warning => EventLogEntryType.Warning,
                _ => EventLogEntryType.Information
            };

            var fullMessage = $"[{category}] {message}\nUser: {Environment.UserName}\nProcess: {Process.GetCurrentProcess().ProcessName}";

            System.Diagnostics.EventLog.WriteEntry(EventSource, fullMessage, eventType, 1001);
        }
        catch (Exception ex)
        {
            // Fallback to Trace on any error
            Trace.WriteLine($"[AUDIT] Failed to write to Event Log: {ex.Message}");
            Trace.WriteLine($"[AUDIT] [{category}] {message}");
        }
    }

    private static void EnsureEventSourceExists()
    {
        lock (_sourceLock)
        {
            if (_sourceChecked)
                return;

            _sourceChecked = true;

            try
            {
                if (!System.Diagnostics.EventLog.SourceExists(EventSource))
                {
                    // Creating event source requires admin privileges
                    // If this fails, we'll fallback to Trace
                    System.Diagnostics.EventLog.CreateEventSource(EventSource, EventLog);
                }
                _sourceAvailable = true;
            }
            catch (System.Security.SecurityException)
            {
                // No admin privileges, use Trace fallback
                _sourceAvailable = false;
                Trace.WriteLine($"[AUDIT] Event Log source '{EventSource}' not available, using Trace fallback");
            }
        }
    }
}
