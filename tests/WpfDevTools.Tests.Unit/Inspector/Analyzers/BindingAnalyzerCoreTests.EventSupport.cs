using System.Diagnostics;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingAnalyzerCoreTests_EventSupport
{
    private static (BindingAnalyzer Analyzer, BindingErrorTraceListener Listener) CreateBindingErrorAnalyzer(
        ElementFinder finder,
        WatchEventBuffer buffer)
    {
        var listener = BindingErrorTraceListener.CreateForTesting();
        return (new BindingAnalyzer(finder, buffer, listener), listener);
    }

    [Fact]
    public void GetBindingErrors_WithTraceListenerError_ShouldEnqueueBindingErrorIntoSharedWatchBuffer()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 16, new WatchEventDeduplicator());
        var (analyzer, listener) = CreateBindingErrorAnalyzer(finder, buffer);

        listener.TraceEvent(
            null,
            "System.Windows.Data",
            TraceEventType.Error,
            40,
            "BindingExpression path error: 'Missing' property not found");

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors());

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        buffer.GetSnapshot().Should().Contain(record =>
            record.EventType == "BindingError"
            && record.NewValue!.Contains("Missing"));
    }

    [Fact]
    public void Dispose_WhenBindingSinkIsCurrent_ShouldStopForwardingTraceEventsToDisposedBuffer()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 16, new WatchEventDeduplicator());
        var (analyzer, listener) = CreateBindingErrorAnalyzer(finder, buffer);

        ((IDisposable)analyzer).Dispose();
        listener.TraceEvent(
            null,
            "System.Windows.Data",
            TraceEventType.Error,
            41,
            "BindingExpression path error: 'Disposed' property not found");

        buffer.GetSnapshot().Should().BeEmpty();
    }

    [Fact]
    public void Dispose_WhenNewerSinkIsCurrent_ShouldNotClearNewerSink()
    {
        var listener = BindingErrorTraceListener.CreateForTesting();
        var olderBuffer = new WatchEventBuffer(capacity: 16, new WatchEventDeduplicator());
        var newerBuffer = new WatchEventBuffer(capacity: 16, new WatchEventDeduplicator());
        var olderAnalyzer = new BindingAnalyzer(new ElementFinder(), olderBuffer, listener);
        var newerAnalyzer = new BindingAnalyzer(new ElementFinder(), newerBuffer, listener);

        ((IDisposable)olderAnalyzer).Dispose();
        listener.TraceEvent(
            null,
            "System.Windows.Data",
            TraceEventType.Error,
            42,
            "BindingExpression path error: 'Current' property not found");

        olderBuffer.GetSnapshot().Should().BeEmpty();
        newerBuffer.GetSnapshot().Should().ContainSingle(record =>
            record.EventType == "BindingError"
            && record.NewValue!.Contains("Current"));
        if (newerAnalyzer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [StaFact]
    public void GetBindingErrors_WithLivePathError_ShouldEnqueueStructuredBindingErrorEvent()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 16, new WatchEventDeduplicator());
        var (analyzer, _) = CreateBindingErrorAnalyzer(finder, buffer);
        var textBox = new TextBox
        {
            DataContext = new { Present = "ok" }
        };
        var elementId = finder.GenerateElementId(textBox);
        textBox.SetBinding(TextBox.TextProperty, new Binding("Missing"));

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors());

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().BeGreaterThan(0);
        buffer.GetSnapshot().Should().Contain(record =>
            record.EventType == "BindingError"
            && record.ElementId == elementId
            && record.PropertyName == "Text");
    }
}
