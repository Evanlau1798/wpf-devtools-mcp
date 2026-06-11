using System.Threading;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public class ElementFinderTimeoutTests
{
    [Fact]
    public void InvokeOnDispatcher_WhenRequestCancellationAlreadyCanceled_ShouldNotExecuteAction()
    {
        Dispatcher? dispatcher = null;
        var dispatcherReady = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        var executed = 0;
        using var cancellation = new CancellationTokenSource();
        using var requestScope = DispatcherRequestContext.Push(cancellation.Token);

        try
        {
            cancellation.Cancel();

            var act = () => ElementFinder.InvokeOnDispatcher(
                dispatcher,
                () => Interlocked.Increment(ref executed),
                TimeSpan.FromSeconds(5));

            act.Should().Throw<OperationCanceledException>();
        }
        finally
        {
            dispatcher!.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
            dispatcherReady.Dispose();
        }

        executed.Should().Be(0);
    }

    [Fact]
    public void InvokeOnDispatcher_WithBlockedDispatcher_ShouldRespectTimeout()
    {
        Dispatcher? dispatcher = null;
        var dispatcherReady = new ManualResetEventSlim(false);
        var blockingStarted = new ManualResetEventSlim(false);
        var releaseBlock = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(() =>
            {
                blockingStarted.Set();
                releaseBlock.Wait();
            }), DispatcherPriority.Send);

            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        blockingStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        try
        {
            var act = () => ElementFinder.InvokeOnDispatcher(
                dispatcher,
                static () => 42,
                TimeSpan.FromMilliseconds(100));

            act.Should().Throw<TimeoutException>();
        }
        finally
        {
            releaseBlock.Set();
            dispatcher!.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
            dispatcherReady.Dispose();
            blockingStarted.Dispose();
            releaseBlock.Dispose();
        }
    }
}
