using System.Diagnostics;
using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DependencyPropertyWaitForChangeTests
{
    [StaFact]
    public void WaitForChange_ShouldReturnTimedOut_WhenValueDoesNotChange()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(
            analyzer.WaitForChange("Width", elementId, timeoutMs: 200, pollIntervalMs: 50));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("changed").GetBoolean().Should().BeFalse();
        result.GetProperty("timedOut").GetBoolean().Should().BeTrue();
        result.GetProperty("currentValue").GetString().Should().Be("100");
    }

    [StaFact]
    public void WaitForChange_WhenCancelledDuringPoll_ShouldStopBeforeFullPollInterval()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);
        using var cancellation = new CancellationTokenSource(50);
        var stopwatch = Stopwatch.StartNew();

        var act = () => analyzer.WaitForChange(
            "Width",
            elementId,
            timeoutMs: 1000,
            pollIntervalMs: 500,
            cancellationToken: cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(300);
    }

    [StaFact]
    public void WaitForChange_ShouldBoundPollDelayToRemainingTimeout()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(
            analyzer.WaitForChange("Width", elementId, timeoutMs: 120, pollIntervalMs: 500));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("timedOut").GetBoolean().Should().BeTrue();
        result.GetProperty("elapsedMs").GetInt64().Should().BeLessThan(300);
    }

    [StaFact]
    public void WaitForChange_ShouldRejectInvalidPollingArguments()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        var timeoutResult = JsonSerializer.SerializeToElement(
            analyzer.WaitForChange("Width", elementId, timeoutMs: 0, pollIntervalMs: 50));
        var intervalResult = JsonSerializer.SerializeToElement(
            analyzer.WaitForChange("Width", elementId, timeoutMs: 100, pollIntervalMs: 10));

        timeoutResult.GetProperty("success").GetBoolean().Should().BeFalse();
        timeoutResult.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        intervalResult.GetProperty("success").GetBoolean().Should().BeFalse();
        intervalResult.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
    }

    [StaFact]
    public void WaitForChange_WhenExpectedValueAlreadyMatches_ShouldReturnImmediatelyWithoutChange()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 100 };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.SerializeToElement(
            analyzer.WaitForChange(
                "Width",
                elementId,
                timeoutMs: 500,
                pollIntervalMs: 50,
                expectedValue: JsonSerializer.SerializeToElement(100.0)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("changed").GetBoolean().Should().BeFalse();
        result.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        result.GetProperty("matchedExpectedValueAtStart").GetBoolean().Should().BeTrue();
        result.GetProperty("completionReason").GetString().Should().Be("ExpectedValueAlreadySatisfied");
    }
}
