using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using System.Text.Json;
using System.Windows.Controls;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

/// <summary>
/// Tests for EventAnalyzer concurrency issues
/// </summary>
public class EventAnalyzerConcurrencyTests
{
    [StaFact]
    public async Task TraceRoutedEvents_WhenCallersAreReleasedTogether_ShouldReturnSuccessfulResults()
    {
        const int callerCount = 10;
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);
        using var ready = new CountdownEvent(callerCount);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, callerCount)
            .Select(_ => Task.Run(async () =>
            {
                ready.Signal();
                await start.Task;
                return analyzer.TraceRoutedEvents(elementId, "Click", 50);
            }))
            .ToArray();

        ready.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        start.SetResult();

        var results = await Task.WhenAll(tasks);
        var payloads = results.Select(result => JsonSerializer.SerializeToElement(result)).ToArray();
        payloads.Should().Contain(result => result.GetProperty("success").GetBoolean());
        foreach (var failure in payloads.Where(result => !result.GetProperty("success").GetBoolean()))
        {
            failure.GetProperty("errorCode").GetString().Should().Be("OperationFailed");
            failure.GetProperty("hint").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }
}
