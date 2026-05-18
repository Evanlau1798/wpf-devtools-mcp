using WpfDevTools.Inspector.Events;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private readonly WatchEventBuffer? _watchEventBuffer;
    private readonly object _bindingEventDedupLock = new();
    private readonly HashSet<string> _emittedBindingEventKeys = new(StringComparer.Ordinal);
    private readonly Queue<string> _emittedBindingEventKeyOrder = new();
    private Action<BindingErrorInfo>? _bindingEventSink;

    private void ConfigureBindingEventBridge()
    {
        if (_watchEventBuffer is not null)
        {
            _bindingEventSink = EnqueueBindingError;
            _bindingErrorTraceListener.SetWatchEventSink(_bindingEventSink);
        }
    }

    /// <summary>
    /// Clears the binding event bridge when this analyzer owns the current trace sink.
    /// </summary>
    public void Dispose()
    {
        if (_bindingEventSink is { } sink)
        {
            _bindingErrorTraceListener.ClearWatchEventSink(sink);
            _bindingEventSink = null;
        }
    }

    private void EnqueueBindingErrors(IReadOnlyList<BindingErrorInfo> errors)
    {
        if (_watchEventBuffer is null)
        {
            return;
        }

        foreach (var error in errors)
        {
            EnqueueBindingError(error);
        }
    }

    private void EnqueueBindingError(BindingErrorInfo error)
    {
        if (_watchEventBuffer is null)
        {
            return;
        }

        var sourceKey = BuildBindingEventKey(error);
        if (!TryRememberBindingEventKey(sourceKey))
        {
            return;
        }

        _watchEventBuffer.Enqueue(new WatchEventRecord(
            EventType: "BindingError",
            TimestampUtc: new DateTimeOffset(error.Timestamp, TimeSpan.Zero),
            SourceKey: sourceKey,
            ElementId: error.ElementId ?? error.SuggestedElementId ?? "BindingTrace",
            PropertyName: error.PropertyName,
            EventName: null,
            NewValue: error.Message,
            ValueType: error.EventType,
            SenderType: error.Origin,
            SenderName: error.BindingPath,
            RoutingStrategy: null,
            Handled: null,
            OriginalSourceType: null));
    }

    private bool TryRememberBindingEventKey(string sourceKey)
    {
        var capacity = _watchEventBuffer?.Capacity ?? 0;
        if (capacity <= 0)
        {
            return true;
        }

        lock (_bindingEventDedupLock)
        {
            if (!_emittedBindingEventKeys.Add(sourceKey))
            {
                return false;
            }

            _emittedBindingEventKeyOrder.Enqueue(sourceKey);
            while (_emittedBindingEventKeys.Count > capacity && _emittedBindingEventKeyOrder.Count > 0)
            {
                _emittedBindingEventKeys.Remove(_emittedBindingEventKeyOrder.Dequeue());
            }

            return true;
        }
    }

    private static string BuildBindingEventKey(BindingErrorInfo error)
    {
        return string.Join(
            "::",
            "binding",
            error.Origin,
            error.ElementId ?? error.SuggestedElementId ?? "BindingTrace",
            error.PropertyName ?? string.Empty,
            error.BindingPath ?? string.Empty,
            error.SourceId.ToString(),
            error.Message);
    }
}
