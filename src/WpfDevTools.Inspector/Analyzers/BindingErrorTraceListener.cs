using System.Collections.Concurrent;
using System.Diagnostics;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// TraceListener that captures WPF data binding errors from PresentationTraceSources.
/// Thread-safe singleton that collects errors into a bounded concurrent queue.
/// </summary>
public sealed class BindingErrorTraceListener : TraceListener
{
    private static Lazy<BindingErrorTraceListener> _instance =
        new Lazy<BindingErrorTraceListener>(() => new BindingErrorTraceListener(),
            LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly ConcurrentQueue<BindingErrorInfo> _errors = new();
    private int _errorCount;

    /// <summary>
    /// Maximum number of errors to retain in the queue.
    /// Oldest errors are discarded when this limit is exceeded.
    /// </summary>
    public const int MaxErrors = 1000;

    /// <summary>
    /// Gets the singleton instance of the trace listener
    /// </summary>
    public static BindingErrorTraceListener Instance => _instance.Value;

    private BindingErrorTraceListener() { }

    /// <summary>
    /// Install the trace listener on PresentationTraceSources.DataBindingSource.
    /// Safe to call multiple times - will not add duplicate listeners.
    /// </summary>
    public static void Install()
    {
        var source = PresentationTraceSources.DataBindingSource;
        if (!source.Listeners.Contains(Instance))
        {
            source.Listeners.Add(Instance);
            source.Switch.Level = SourceLevels.Error;
        }
    }

    /// <summary>
    /// Remove the trace listener from PresentationTraceSources.DataBindingSource
    /// </summary>
    public static void Uninstall()
    {
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Remove(Instance);
    }

    /// <summary>
    /// Required by TraceListener - not used for structured binding errors
    /// </summary>
    public override void Write(string? message)
    {
        // Intentionally empty - binding errors come through TraceEvent
    }

    /// <summary>
    /// Required by TraceListener - not used for structured binding errors
    /// </summary>
    public override void WriteLine(string? message)
    {
        // Intentionally empty - binding errors come through TraceEvent
    }

    /// <summary>
    /// Captures trace events from PresentationTraceSources.DataBindingSource
    /// </summary>
    public override void TraceEvent(
        TraceEventCache? eventCache,
        string source,
        TraceEventType eventType,
        int id,
        string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var error = new BindingErrorInfo
        {
            Timestamp = DateTime.UtcNow,
            Message = message!,
            EventType = eventType.ToString(),
            SourceId = id
        };

        EnqueueError(error);
    }

    /// <summary>
    /// Captures formatted trace events from PresentationTraceSources.DataBindingSource
    /// </summary>
    public override void TraceEvent(
        TraceEventCache? eventCache,
        string source,
        TraceEventType eventType,
        int id,
        string? format,
        params object?[]? args)
    {
        string? message;
        try
        {
            message = args != null && format != null
                ? string.Format(format, args)
                : format;
        }
        catch (FormatException)
        {
            // If format fails, use raw format string
            message = format;
        }

        TraceEvent(eventCache, source, eventType, id, message);
    }

    /// <summary>
    /// Returns a snapshot of all captured binding errors
    /// </summary>
    public IReadOnlyList<BindingErrorInfo> GetErrors()
    {
        return _errors.ToArray();
    }

    /// <summary>
    /// Returns the current number of captured errors
    /// </summary>
    public int ErrorCount => _errorCount;

    /// <summary>
    /// Clears all captured binding errors
    /// </summary>
    public void ClearErrors()
    {
        while (_errors.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _errorCount, 0);
    }

    private void EnqueueError(BindingErrorInfo error)
    {
        _errors.Enqueue(error);
        Interlocked.Increment(ref _errorCount);

        // Trim oldest errors when exceeding capacity
        while (_errors.Count > MaxErrors && _errors.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _errorCount);
        }
    }

    /// <summary>
    /// Reset the singleton instance (for testing purposes only).
    /// Creates a new instance and clears all errors.
    /// </summary>
    internal static void ResetInstance()
    {
        // Uninstall the old instance first
        if (_instance.IsValueCreated)
        {
            try
            {
                var source = PresentationTraceSources.DataBindingSource;
                source.Listeners.Remove(_instance.Value);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BindingErrorTraceListener: Failed to remove listener (may not be available in test contexts): {ex.Message}");
            }
        }

        // Create a new Lazy instance
        _instance = new Lazy<BindingErrorTraceListener>(
            () => new BindingErrorTraceListener(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
