using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class BindingErrorWindowingTests
{
    private static (BindingAnalyzer Analyzer, BindingErrorTraceListener Listener) CreateBindingErrorAnalyzer()
    {
        var listener = BindingErrorTraceListener.CreateForTesting();
        return (new BindingAnalyzer(new ElementFinder(), null, listener), listener);
    }

    [Fact]
    public void GetBindingErrors_WithMaxErrors_ShouldTruncateNewestResults()
    {
        var (analyzer, listener) = CreateBindingErrorAnalyzer();
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40, "First error");
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40, "Second error");

        var result = JsonSerializer.SerializeToElement(analyzer.GetBindingErrors(maxErrors: 1));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(1);
        result.GetProperty("errors")[0].GetProperty("message").GetString().Should().Be("Second error");
    }

    [Fact]
    public void GetBindingErrors_WithSinceTimestamp_ShouldReturnNewerErrorsOnly()
    {
        var (analyzer, listener) = CreateBindingErrorAnalyzer();
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40, "Old error");
        var cutoff = DateTime.UtcNow.AddMilliseconds(10);
        Thread.Sleep(20);
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40, "New error");

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetBindingErrors(sinceTimestamp: cutoff.ToString("O")));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(1);
        result.GetProperty("errors")[0].GetProperty("message").GetString().Should().Be("New error");
    }

    [Fact]
    public void GetBindingErrors_WithInvalidSinceTimestamp_ShouldReturnStructuredInvalidArgument()
    {
        var (analyzer, _) = CreateBindingErrorAnalyzer();

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetBindingErrors(sinceTimestamp: "not-a-timestamp"));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
    }
}
