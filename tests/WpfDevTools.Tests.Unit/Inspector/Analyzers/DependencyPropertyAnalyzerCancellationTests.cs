using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("AnalyzerStaticState")]
public sealed class DependencyPropertyAnalyzerCancellationTests
{
    [Fact]
    public void GetValueSource_WithSettleBindings_WhenRequestCancellationAlreadyCanceled_ShouldAbortDispatcherRead()
    {
        Dispatcher? dispatcher = null;
        string? elementId = null;
        Button? button = null;
        var dispatcherReady = new ManualResetEventSlim(false);
        using var finder = new ElementFinder();

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            button = new Button { Width = 100 };
            elementId = finder.GenerateElementId(button);

            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        elementId.Should().NotBeNull();
        button.Should().NotBeNull();

        var analyzer = new DependencyPropertyAnalyzer(finder);
        using var cancellation = new CancellationTokenSource();
        using var requestScope = DispatcherRequestContext.Push(cancellation.Token);

        try
        {
            cancellation.Cancel();

            var act = () => analyzer.GetValueSource(
                "Width",
                elementId,
                settleBindings: true);

            act.Should().Throw<OperationCanceledException>();
        }
        finally
        {
            dispatcher!.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
            dispatcherReady.Dispose();
        }
    }
}
