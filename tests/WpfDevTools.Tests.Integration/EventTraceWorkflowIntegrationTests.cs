using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfIntegration")]
public class EventTraceWorkflowIntegrationTests
{
    private readonly WpfApplicationFixture _fixture;

    public EventTraceWorkflowIntegrationTests(WpfApplicationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TraceRoutedEvents_WithStartMode_ShouldReturnBeforeDurationExpires()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        string elementId = string.Empty;

        _fixture.RunOnUIThread(() =>
        {
            var button = new Button { Content = "Trace Me" };
            var panel = new StackPanel();
            panel.Children.Add(button);

            var window = Application.Current.MainWindow;
            window.Content = panel;
            window.Show();
            window.Activate();
            panel.Measure(new Size(400, 200));
            panel.Arrange(new Rect(0, 0, 400, 200));
            panel.UpdateLayout();

            elementId = finder.GenerateElementId(button);
        });

        var parameters = JsonSerializer.SerializeToElement(new { elementId, eventName = "Click", duration = 500, mode = "start" });
        var stopwatch = Stopwatch.StartNew();

        var result = await handler.HandleAsync("trace_routed_events", parameters, CancellationToken.None);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(250),
            "start mode should not block the session for the whole capture window");
        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
        payload.GetProperty("mode").GetString().Should().Be("start");
        payload.GetProperty("isTracing").GetBoolean().Should().BeTrue();

        await Task.Delay(550);
    }

    [Fact]
    public async Task TraceRoutedEvents_WithStartMode_ThenGetMode_ShouldCaptureFiredEvent()
    {
        var finder = new ElementFinder();
        var analyzer = new EventAnalyzer(finder);
        var handler = new EventHandlers(analyzer);
        string elementId = string.Empty;

        _fixture.RunOnUIThread(() =>
        {
            var button = new Button { Content = "Trace Me" };
            var panel = new StackPanel();
            panel.Children.Add(button);

            var window = Application.Current.MainWindow;
            window.Content = panel;
            window.Show();
            window.Activate();
            panel.Measure(new Size(400, 200));
            panel.Arrange(new Rect(0, 0, 400, 200));
            panel.UpdateLayout();

            elementId = finder.GenerateElementId(button);
        });

        var startParameters = JsonSerializer.SerializeToElement(new { elementId, eventName = "Click", duration = 500, mode = "start" });
        var fireParameters = JsonSerializer.SerializeToElement(new { elementId, eventName = "Click" });
        var getParameters = JsonSerializer.SerializeToElement(new { mode = "get" });

        var startResult = await handler.HandleAsync("trace_routed_events", startParameters, CancellationToken.None);
        JsonSerializer.SerializeToElement(startResult).GetProperty("success").GetBoolean().Should().BeTrue();

        var fireResult = await handler.HandleAsync("fire_routed_event", fireParameters, CancellationToken.None);
        JsonSerializer.SerializeToElement(fireResult).GetProperty("success").GetBoolean().Should().BeTrue();

        await Task.Delay(50);

        var traceResult = await handler.HandleAsync("trace_routed_events", getParameters, CancellationToken.None);
        var tracePayload = JsonSerializer.SerializeToElement(traceResult);

        tracePayload.GetProperty("success").GetBoolean().Should().BeTrue(tracePayload.GetRawText());
        tracePayload.GetProperty("mode").GetString().Should().Be("get");
        tracePayload.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0);
        tracePayload.GetProperty("events")[0].GetProperty("handled").GetBoolean().Should().BeFalse();

        await Task.Delay(550);
    }
}
