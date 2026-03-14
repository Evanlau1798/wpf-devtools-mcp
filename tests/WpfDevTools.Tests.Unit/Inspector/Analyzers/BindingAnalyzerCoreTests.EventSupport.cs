using System.Diagnostics;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("BindingErrorTests")]
public sealed class BindingAnalyzerCoreTests_EventSupport : IDisposable
{
    public BindingAnalyzerCoreTests_EventSupport()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    [Fact]
    public void GetBindingErrors_WithTraceListenerError_ShouldEnqueueBindingErrorIntoSharedWatchBuffer()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 16, new WatchEventDeduplicator());
        var analyzer = new BindingAnalyzer(finder, buffer);

        BindingErrorTraceListener.Instance.TraceEvent(
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

    [StaFact]
    public void GetBindingErrors_WithLivePathError_ShouldEnqueueStructuredBindingErrorEvent()
    {
        var finder = new ElementFinder();
        var buffer = new WatchEventBuffer(capacity: 16, new WatchEventDeduplicator());
        var analyzer = new BindingAnalyzer(finder, buffer);
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

    public void Dispose()
    {
        BindingErrorTraceListener.ResetInstance();
    }
}
