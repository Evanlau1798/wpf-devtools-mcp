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
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
        var metadata = result.GetProperty("truncationMetadata");
        metadata.GetProperty("totalResultCount").GetInt32().Should().Be(2);
        metadata.GetProperty("returnedResultCount").GetInt32().Should().Be(1);
        metadata.GetProperty("reasons").EnumerateArray()
            .Select(reason => reason.GetString())
            .Should().Contain("ResultLimit");
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
    public void GetBindingErrors_WithSinceTimestampEqualToErrorTimestamp_ShouldIncludeBoundaryError()
    {
        var (analyzer, listener) = CreateBindingErrorAnalyzer();
        listener.TraceEvent(null, "System.Windows.Data", TraceEventType.Error, 40, "Boundary error");
        var boundary = listener.GetErrors().Single().Timestamp.ToUniversalTime().ToString("O");

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetBindingErrors(sinceTimestamp: boundary));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("errorCount").GetInt32().Should().Be(1);
        result.GetProperty("errors")[0].GetProperty("message").GetString().Should().Be("Boundary error");
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

    [Fact]
    public void GetBindingErrors_WithTimezoneLessSinceTimestamp_ShouldReturnStructuredInvalidArgument()
    {
        var (analyzer, _) = CreateBindingErrorAnalyzer();

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetBindingErrors(sinceTimestamp: "2026-03-11T12:00:00"));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("hint").GetString().Should().Contain("Z");
    }

    [Theory]
    [InlineData("2026-03-11T12:00:00Z")]
    [InlineData("2026-03-11T12:00:00+05:00")]
    public void GetBindingErrors_WithExplicitTimezoneSinceTimestamp_ShouldSucceed(string sinceTimestamp)
    {
        var (analyzer, _) = CreateBindingErrorAnalyzer();

        var result = JsonSerializer.SerializeToElement(
            analyzer.GetBindingErrors(sinceTimestamp: sinceTimestamp));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
