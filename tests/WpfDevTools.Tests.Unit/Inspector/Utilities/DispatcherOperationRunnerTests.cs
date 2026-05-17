using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

[Collection("TimingSensitive")]
public sealed class DispatcherOperationRunnerTests
{
    [Fact]
    public void Invoke_WhenStartedOperationTimesOut_ShouldAllowLateCompletionWithoutDispatcherException()
    {
        Dispatcher? dispatcher = null;
        Exception? dispatcherException = null;
        Exception? operationException = null;
        using var dispatcherReady = new ManualResetEventSlim(false);
        using var actionStarted = new ManualResetEventSlim(false);
        using var releaseAction = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, args) =>
            {
                dispatcherException = args.Exception;
                args.Handled = true;
            };
            dispatcher.Hooks.OperationCompleted += (_, args) =>
            {
                if (args.Operation.Task.IsFaulted)
                {
                    operationException = args.Operation.Task.Exception?.GetBaseException();
                }
            };

            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        try
        {
            var act = () => DispatcherOperationRunner.Invoke(
                dispatcher!,
                () =>
                {
                    actionStarted.Set();
                    releaseAction.Wait();
                    return 42;
                },
                TimeSpan.FromMilliseconds(100),
                CancellationToken.None,
                "test dispatcher operation");

            act.Should().Throw<TimeoutException>();
            actionStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            releaseAction.Set();
            dispatcher!.Invoke(() => { }, DispatcherPriority.Background);

            dispatcherException.Should().BeNull();
            operationException.Should().BeNull();
        }
        finally
        {
            releaseAction.Set();
            dispatcher!.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
    }
}
