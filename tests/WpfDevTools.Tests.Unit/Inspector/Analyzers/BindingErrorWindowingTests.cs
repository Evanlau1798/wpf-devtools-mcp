using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

[Collection("BindingErrorTests")]
public sealed class BindingErrorWindowingTests : IDisposable
{
    public BindingErrorWindowingTests()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    public void Dispose()
    {
        BindingErrorTraceListener.ResetInstance();
    }

    [Fact]
    public void GetBindingErrors_WithMaxErrors_ShouldTruncateNewestResults()
    {
        var analyzer = new BindingAnalyzer();
        var listener = BindingErrorTraceListener.Instance;
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
        var analyzer = new BindingAnalyzer();
        var listener = BindingErrorTraceListener.Instance;
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
        var analyzer = new BindingAnalyzer();

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetBindingErrors(sinceTimestamp: "not-a-timestamp"));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
    }
}
