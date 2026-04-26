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
}
