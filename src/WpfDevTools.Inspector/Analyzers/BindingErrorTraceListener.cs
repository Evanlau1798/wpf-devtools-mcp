using System.Collections.Concurrent;
using System.Diagnostics;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// TraceListener that captures WPF data binding errors from PresentationTraceSources.
/// Thread-safe singleton that collects errors into a bounded concurrent queue.
/// </summary>
public sealed class BindingErrorTraceListener : TraceListener
{
    private static readonly object LifecycleLock = new();
    private static Lazy<BindingErrorTraceListener> _instance =
        new Lazy<BindingErrorTraceListener>(() => new BindingErrorTraceListener(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    private static SourceLevels? _originalSwitchLevel;

    private readonly ConcurrentQueue<BindingErrorInfo> _errors = new();
    private Action<BindingErrorInfo>? _watchEventSink;

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

    internal static BindingErrorTraceListener CreateForTesting()
    {
        return new BindingErrorTraceListener();
    }

    /// <summary>
    /// Install the trace listener on PresentationTraceSources.DataBindingSource.
    /// Safe to call multiple times - will not add duplicate listeners.
    /// </summary>
    public static void Install()
    {
        lock (LifecycleLock)
        {
            var source = PresentationTraceSources.DataBindingSource;
            _originalSwitchLevel ??= source.Switch.Level;
            RemoveAllRegistrations(source, Instance);
            source.Listeners.Add(Instance);
            source.Switch.Level = SourceLevels.Error;
        }
    }

    /// <summary>
    /// Remove the trace listener from PresentationTraceSources.DataBindingSource
    /// </summary>
    public static void Uninstall()
    {
        lock (LifecycleLock)
        {
            var source = PresentationTraceSources.DataBindingSource;
            RemoveAllRegistrations(source, Instance);
            RestoreOriginalSwitchLevel(source);
        }
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
    public int ErrorCount => _errors.Count;

    /// <summary>
    /// Clears all captured binding errors
    /// </summary>
    public void ClearErrors()
    {
        while (_errors.TryDequeue(out _)) { }
    }

    internal void SetWatchEventSink(Action<BindingErrorInfo>? sink)
    {
        Volatile.Write(ref _watchEventSink, sink);
    }

    internal void ClearWatchEventSink(Action<BindingErrorInfo> sink)
    {
        Interlocked.CompareExchange(ref _watchEventSink, null, sink);
    }

    private void EnqueueError(BindingErrorInfo error)
    {
        _errors.Enqueue(error);
        var sink = Volatile.Read(ref _watchEventSink);
        sink?.Invoke(error);

        // Trim oldest errors when exceeding capacity
        while (_errors.Count > MaxErrors && _errors.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Reset the singleton instance (for testing purposes only).
    /// Creates a new instance and clears all errors.
    /// </summary>
    internal static void ResetInstance()
    {
        lock (LifecycleLock)
        {
            var source = PresentationTraceSources.DataBindingSource;

            // Uninstall the old instance first
            if (_instance.IsValueCreated)
            {
                try
                {
                    RemoveAllRegistrations(source, _instance.Value);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"BindingErrorTraceListener: Failed to remove listener (may not be available in test contexts): {ex.Message}");
                }
            }

            RestoreOriginalSwitchLevel(source);

            // Create a new Lazy instance
            _instance = new Lazy<BindingErrorTraceListener>(
                () => new BindingErrorTraceListener(),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    private static void RestoreOriginalSwitchLevel(TraceSource source)
    {
        if (_originalSwitchLevel is not { } originalLevel)
        {
            return;
        }

        source.Switch.Level = originalLevel;
        _originalSwitchLevel = null;
    }

    private static void RemoveAllRegistrations(TraceSource source, BindingErrorTraceListener listener)
    {
        while (source.Listeners.Cast<TraceListener>().Any(current => ReferenceEquals(current, listener)))
        {
            source.Listeners.Remove(listener);
        }
    }
}
