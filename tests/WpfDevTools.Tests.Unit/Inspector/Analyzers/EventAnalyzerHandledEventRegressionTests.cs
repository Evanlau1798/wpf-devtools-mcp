using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class EventAnalyzerHandledEventRegressionTests
{
    [StaFact]
    public void FindRoutedEvent_WithClick_ShouldResolveButtonClickEvent()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var method = typeof(EventAnalyzer).GetMethod("FindRoutedEvent", BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        var routedEvent = method!.Invoke(analyzer, new object[] { button, "Click" }) as RoutedEvent;

        routedEvent.Should().BeSameAs(Button.ClickEvent);
    }

    [StaFact]
    public void RoutedEventBaseline_AddHandlerAndRaiseEvent_ShouldInvokeHandler()
    {
        var button = new Button();
        var captured = false;
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler((_, _) => captured = true), handledEventsToo: true);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

        captured.Should().BeTrue();
    }

    [StaFact]
    public void TraceRoutedEvents_WhenEventFires_ShouldCaptureIt()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        var elementId = finder.GenerateElementId(button);

        var startResult = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 200)));
        startResult.GetProperty("success").GetBoolean().Should().BeTrue();

        var fireResult = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.FireRoutedEvent(elementId, "Click", null)));
        fireResult.GetProperty("success").GetBoolean().Should().BeTrue(fireResult.GetRawText());

        Thread.Sleep(25);

        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()));
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0);
    }

    [StaFact]
    public void TraceRoutedEvents_WhenEventIsHandled_ShouldStillCaptureIt()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var button = new Button();
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler((_, e) => e.Handled = true));
        var elementId = finder.GenerateElementId(button);

        var startResult = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.TraceRoutedEvents(elementId, "Click", 200)));
        startResult.GetProperty("success").GetBoolean().Should().BeTrue();

        var fireResult = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.FireRoutedEvent(elementId, "Click", null)));
        fireResult.GetProperty("success").GetBoolean().Should().BeTrue(fireResult.GetRawText());

        Thread.Sleep(25);

        var payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(analyzer.GetEventTrace()));
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0,
            "tracing should capture handled routed events as well");
        payload.GetProperty("events")[0].GetProperty("handled").GetBoolean().Should().BeTrue();
    }
}
