using System.Collections.Concurrent;
using WpfDevTools.Inspector.Events;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private readonly WatchEventBuffer? _watchEventBuffer;
    private readonly ConcurrentDictionary<string, byte> _emittedBindingEventKeys = new(StringComparer.Ordinal);

    private void ConfigureBindingEventBridge()
    {
        if (_watchEventBuffer is not null)
        {
            _bindingErrorTraceListener.SetWatchEventSink(EnqueueBindingError);
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
        if (!_emittedBindingEventKeys.TryAdd(sourceKey, 0))
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
