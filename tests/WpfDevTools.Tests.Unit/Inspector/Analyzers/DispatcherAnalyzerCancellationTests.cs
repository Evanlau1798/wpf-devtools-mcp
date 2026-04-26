using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("TimingSensitive")]
public sealed class DispatcherAnalyzerCancellationTests
{
    [Fact]
    public void InvokeOnDispatcher_WhenRequestCancelsBeforePendingWorkStarts_ShouldAbortWork()
    {
        Dispatcher? dispatcher = null;
        using var dispatcherReady = new ManualResetEventSlim(false);
        using var blockingStarted = new ManualResetEventSlim(false);
        using var releaseBlock = new ManualResetEventSlim(false);

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

        var executed = 0;
        using var cts = new CancellationTokenSource();
        using var requestScope = DispatcherRequestContext.Push(cts.Token);
        var probe = new DispatcherProbe();

        try
        {
            cts.Cancel();

            var act = () => probe.Invoke(dispatcher!, () => Interlocked.Increment(ref executed));

            act.Should().Throw<OperationCanceledException>();
        }
        finally
        {
            releaseBlock.Set();
            dispatcher!.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }

        executed.Should().Be(0);
    }

    private sealed class DispatcherProbe : DispatcherAnalyzerBase
    {
        public void Invoke(Dispatcher dispatcher, Action action)
        {
            InvokeOnDispatcher(dispatcher, action, TimeSpan.FromSeconds(5));
        }
    }
}
