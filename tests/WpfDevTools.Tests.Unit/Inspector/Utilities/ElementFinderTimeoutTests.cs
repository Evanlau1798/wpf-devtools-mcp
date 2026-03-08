using System.Threading;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public class ElementFinderTimeoutTests
{
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
