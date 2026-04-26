using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

[Collection("TimingSensitive")]
public sealed class EventHandlerTraceMaxEventsTests
{
    [StaFact]
    public async Task TraceRoutedEvents_GetMode_WithMaxEvents_ShouldReturnTruncationMetadata()
    {
        var finder = new ElementFinder();
        using var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        var button = new Button { Name = "TraceMaxEventsButton" };
        var elementId = finder.GenerateElementId(button);

        var startResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new
            {
                mode = "start",
                elementId,
                eventName = "Click",
                duration = 1000,
                allowShortStartDuration = true
            }),
            CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        SpinWait.SpinUntil(
            () => JsonSerializer.SerializeToElement(analyzer.GetEventHandlers(elementId, "Click"))
                .GetProperty("handlerCount").GetInt32() == 1,
            TimeSpan.FromSeconds(1)).Should().BeTrue();

        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));
        button.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent, button));

        var getResult = await handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", maxEvents = 1 }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(getResult);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().Be(1);
        payload.GetProperty("returnedEventCount").GetInt32().Should().Be(1);
        payload.GetProperty("totalEventCount").GetInt32().Should().Be(2);
        payload.GetProperty("eventsTruncated").GetBoolean().Should().BeTrue();
        payload.GetProperty("maxEvents").GetInt32().Should().Be(1);
        payload.GetProperty("events").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task TraceRoutedEvents_GetMode_WithInvalidMaxEvents_ShouldReject()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));

        Func<Task> act = () => handler.HandleAsync(
            "trace_routed_events",
            JsonSerializer.SerializeToElement(new { mode = "get", maxEvents = 0 }),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*maxEvents*positive*");
    }
}
