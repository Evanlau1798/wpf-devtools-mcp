using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector;

public sealed class RequestDispatcherTimeoutRecoveryTests
{
    [Fact]
    public async Task DispatchAsync_WhenRequestIsCanceled_ShouldReturnTimeoutRecoveryData()
    {
        var dispatcher = new RequestDispatcher(new FileLogger());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new InspectorRequest
        {
            Id = "cancel-recovery-01",
            Method = "get_visual_tree",
            Params = JsonSerializer.SerializeToElement(new { })
        };

        var response = await dispatcher.DispatchAsync(request, cts.Token);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.Timeout);
        response.Error.Data.Should().NotBeNull();

        var data = response.Error.Data!.Value;
        data.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        data.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        data.GetProperty("suggestedAction").GetString().Should().Contain("Reconnect");
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerTimesOut_ShouldReturnTimeoutRecoveryData()
    {
        var dispatcher = new RequestDispatcher(new FileLogger());
        dispatcher.AddSimpleHandlerForTesting("throw_timeout", static (_, _) =>
            throw new TimeoutException("Simulated dispatcher timeout."));

        var response = await dispatcher.DispatchAsync(new InspectorRequest
        {
            Id = "timeout-recovery-01",
            Method = "throw_timeout",
            Params = JsonSerializer.SerializeToElement(new { })
        }, CancellationToken.None);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.Timeout);
        response.Error.Data.Should().NotBeNull();
        response.Error.Data!.Value.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        response.Error.Data.Value.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
    }
}
