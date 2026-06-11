using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class EventTraceTransitionStateTests
{
    [StaFact]
    public void BeginStart_RequestCancellationAndCompleteStart_ShouldTrackStartupTransition()
    {
        var state = new EventTraceTransitionState();
        var dispatcher = Dispatcher.CurrentDispatcher;

        state.BeginStart(dispatcher);
        state.IsStartTransitionInProgress.Should().BeTrue();
        state.StartTransitionDispatcher.Should().BeSameAs(dispatcher);
        state.CancelStartTransitionRequested.Should().BeFalse();

        state.RequestStartCancellation();
        state.CancelStartTransitionRequested.Should().BeTrue();

        state.CompleteStart();
        state.IsStartTransitionInProgress.Should().BeFalse();
        state.StartTransitionDispatcher.Should().BeNull();
        state.CancelStartTransitionRequested.Should().BeFalse();
    }

    [StaFact]
    public void TryBeginCleanup_WhenCleanupIsAlreadyActive_ShouldFailClosedUntilCompleted()
    {
        var state = new EventTraceTransitionState();
        var dispatcher = Dispatcher.CurrentDispatcher;

        state.TryBeginCleanup(dispatcher).Should().BeTrue();
        state.IsCleanupInProgress.Should().BeTrue();
        state.CleanupTransitionDispatcher.Should().BeSameAs(dispatcher);

        state.TryBeginCleanup(dispatcher).Should().BeFalse();

        state.CompleteCleanup();
        state.IsCleanupInProgress.Should().BeFalse();
        state.CleanupTransitionDispatcher.Should().BeNull();
        state.TryBeginCleanup(dispatcher).Should().BeTrue();
    }

    [StaFact]
    public void ResetAll_ShouldClearStartAndCleanupTransitions()
    {
        var state = new EventTraceTransitionState();
        var dispatcher = Dispatcher.CurrentDispatcher;

        state.BeginStart(dispatcher);
        state.RequestStartCancellation();
        state.TryBeginCleanup(dispatcher).Should().BeTrue();

        state.ResetAll();

        state.IsStartTransitionInProgress.Should().BeFalse();
        state.StartTransitionDispatcher.Should().BeNull();
        state.CancelStartTransitionRequested.Should().BeFalse();
        state.IsCleanupInProgress.Should().BeFalse();
        state.CleanupTransitionDispatcher.Should().BeNull();
    }
}