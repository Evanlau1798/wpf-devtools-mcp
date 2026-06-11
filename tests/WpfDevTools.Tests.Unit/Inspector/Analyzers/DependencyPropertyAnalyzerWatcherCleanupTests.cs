using FluentAssertions;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("AnalyzerStaticState")]
public sealed class DependencyPropertyAnalyzerWatcherCleanupTests : IDisposable
{
    private readonly Action<System.ComponentModel.DependencyPropertyDescriptor, System.Windows.DependencyObject, EventHandler> _originalDetachWatcherAction;

    public DependencyPropertyAnalyzerWatcherCleanupTests()
    {
        _originalDetachWatcherAction = DependencyPropertyAnalyzer.DetachWatcherAction;
        DependencyPropertyAnalyzer.StopAllWatchers();
        DependencyPropertyAnalyzer.ResetMonitoring();
        DependencyPropertyAnalyzer.DetachWatcherAction = static (descriptor, element, handler) => descriptor.RemoveValueChanged(element, handler);
    }

    public void Dispose()
    {
        DependencyPropertyAnalyzer.DetachWatcherAction = _originalDetachWatcherAction;
        DependencyPropertyAnalyzer.StopAllWatchers();
        DependencyPropertyAnalyzer.ResetMonitoring();
    }

    [StaFact]
    public void StopAllWatchers_WhenCalledOffThread_ShouldMarshalDetachmentToOwningDispatcher()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        var observedThreadId = -1;

        DependencyPropertyAnalyzer.DetachWatcherAction = (descriptor, element, handler) =>
        {
            observedThreadId = Environment.CurrentManagedThreadId;
            descriptor.RemoveValueChanged(element, handler);
        };
        analyzer.WatchChanges("Width", elementId);

        var backgroundCleanup = Task.Run(() => DependencyPropertyAnalyzer.StopAllWatchers());
        PumpDispatcherUntil(backgroundCleanup, button.Dispatcher, TimeSpan.FromSeconds(2));

        observedThreadId.Should().Be(button.Dispatcher.Thread.ManagedThreadId);
    }

    [StaFact]
    public void StopAllWatchers_WhenDetachmentFails_ShouldKeepWatcherRegisteredForRetry()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        var attempts = 0;

        analyzer.WatchChanges("Width", elementId);
        analyzer.ClearChangeLog();

        DependencyPropertyAnalyzer.DetachWatcherAction = (descriptor, element, handler) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("Simulated detach failure");
            }

            descriptor.RemoveValueChanged(element, handler);
        };

        DependencyPropertyAnalyzer.StopAllWatchers();

        DependencyPropertyAnalyzer.DetachWatcherAction = (descriptor, element, handler) =>
        {
            attempts++;
            _originalDetachWatcherAction(descriptor, element, handler);
        };
        DependencyPropertyAnalyzer.StopAllWatchers();

        button.Width = 120;

        dynamic logResult = analyzer.GetChangeLog();
        int changeCount = logResult.changeCount;
        changeCount.Should().Be(0);
        attempts.Should().Be(2);
    }

    private static void PumpDispatcherUntil(Task task, Dispatcher dispatcher, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!task.IsCompleted)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "background watcher cleanup should complete within the dispatcher pump budget");

            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        task.GetAwaiter().GetResult();
    }
}
