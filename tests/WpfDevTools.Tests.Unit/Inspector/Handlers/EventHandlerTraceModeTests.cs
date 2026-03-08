using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class EventHandlerTraceModeTests
{
    [Fact]
    public async Task TraceRoutedEvents_WithUppercaseGetMode_ShouldReturnGetPayload()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { mode = "GET" });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("mode").GetString().Should().Be("get");
    }

    [Fact]
    public async Task TraceRoutedEvents_WithWhitespaceWrappedGetMode_ShouldNormalizeValue()
    {
        var handler = new EventHandlers(new EventAnalyzer(new ElementFinder()));
        var @params = JsonSerializer.SerializeToElement(new { mode = "  GET  " });

        var result = await handler.HandleAsync("trace_routed_events", @params, CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("mode").GetString().Should().Be("get");
    }
}
