using System.Windows;
using WpfDevTools.Inspector.Events;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class MvvmAnalyzer
{
    private readonly WatchEventBuffer? _watchEventBuffer;
    private readonly ValidationChangeTracker _validationChangeTracker;

    private ValidationChangeTracker.ValidationSnapshot CaptureValidationSnapshot(DependencyObject scope) =>
        _validationChangeTracker.CaptureSnapshot(scope);

    private void EnqueueValidationTransition(
        string scopeElementId,
        ValidationChangeTracker.ValidationSnapshot before,
        ValidationChangeTracker.ValidationSnapshot after)
    {
        if (_watchEventBuffer is null)
        {
            return;
        }

        var record = _validationChangeTracker.CreateTransitionEvent(scopeElementId, before, after);
        if (record is not null)
        {
            _watchEventBuffer.Enqueue(record);
        }
    }
}
