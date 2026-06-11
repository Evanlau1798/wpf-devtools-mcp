using System.Collections.Concurrent;
using FluentAssertions;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("AnalyzerStaticState")]
public sealed class DependencyPropertyAnalyzerWatchRegistrationRaceTests : IDisposable
{
    public DependencyPropertyAnalyzerWatchRegistrationRaceTests()
    {
        ResetDetachWatcherAction();
        DependencyPropertyAnalyzer.StopAllWatchers();
        DependencyPropertyAnalyzer.ResetMonitoring();
    }

    [StaFact]
    public void WatchChanges_WhenSameKeyIsRegisteredConcurrently_ShouldAttachOnlyOneWatcher()
    {
        const int concurrentRegistrations = 32;
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        var results = new ConcurrentBag<object>();
        using var startBarrier = new Barrier(concurrentRegistrations);

        var tasks = Enumerable.Range(0, concurrentRegistrations)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    startBarrier.SignalAndWait(TimeSpan.FromSeconds(5));
                    results.Add(analyzer.WatchChanges("Width", elementId));
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        PumpDispatcherUntil(Task.WhenAll(tasks), button.Dispatcher, TimeSpan.FromSeconds(10));

        results.Count(IsSuccessfulResult).Should().Be(1);

        DependencyPropertyAnalyzer.StopAllWatchers();
        analyzer.ClearChangeLog();

        button.Width = 240;
        Thread.Sleep(50);

        dynamic logResult = analyzer.GetChangeLog();
        int changeCount = logResult.changeCount;
        changeCount.Should().Be(0);
    }

    public void Dispose()
    {
        ResetDetachWatcherAction();
        DependencyPropertyAnalyzer.StopAllWatchers();
        DependencyPropertyAnalyzer.ResetMonitoring();
        ResetDetachWatcherAction();
    }

    private static bool IsSuccessfulResult(object result) =>
        result.GetType().GetProperty("success")?.GetValue(result) is true;

    private static void ResetDetachWatcherAction()
    {
        DependencyPropertyAnalyzer.DetachWatcherAction =
            static (descriptor, element, handler) => descriptor.RemoveValueChanged(element, handler);
    }

    private static void PumpDispatcherUntil(Task task, Dispatcher dispatcher, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!task.IsCompleted)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "concurrent watcher registration should complete within the dispatcher pump budget");

            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        task.GetAwaiter().GetResult();
    }
}
