using System.Windows.Threading;

namespace WpfDevTools.Inspector.Analyzers;

internal sealed class EventTraceTransitionState
{
    public bool IsStartTransitionInProgress { get; private set; }

    public Dispatcher? StartTransitionDispatcher { get; private set; }

    public bool CancelStartTransitionRequested { get; private set; }

    public bool IsCleanupInProgress { get; private set; }

    public Dispatcher? CleanupTransitionDispatcher { get; private set; }

    public void BeginStart(Dispatcher? dispatcher)
    {
        IsStartTransitionInProgress = true;
        StartTransitionDispatcher = dispatcher;
        CancelStartTransitionRequested = false;
    }

    public void RequestStartCancellation()
    {
        if (IsStartTransitionInProgress)
        {
            CancelStartTransitionRequested = true;
        }
    }

    public void CompleteStart()
    {
        IsStartTransitionInProgress = false;
        StartTransitionDispatcher = null;
        CancelStartTransitionRequested = false;
    }

    public bool TryBeginCleanup(Dispatcher? dispatcher)
    {
        if (IsCleanupInProgress)
        {
            return false;
        }

        IsCleanupInProgress = true;
        CleanupTransitionDispatcher = dispatcher;
        return true;
    }

    public void CompleteCleanup()
    {
        IsCleanupInProgress = false;
        CleanupTransitionDispatcher = null;
    }

    public void ResetAll()
    {
        CompleteStart();
        CompleteCleanup();
    }
}