using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host.Handlers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Handlers;

public sealed class PerformanceHandlersTests
{
    [Fact]
    public async Task FindBindingLeaks_ShouldForwardCancellation()
    {
        var handler = new PerformanceHandlers(new PerformanceAnalyzer(new ElementFinder()));
        var parameters = JsonSerializer.SerializeToElement(new { samplingDurationMs = 15000 });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => handler.HandleAsync("find_binding_leaks", parameters, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
