using System.IO;
using System.IO.Pipes;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class ConnectedWaitSessionBuilderTests
{
    [Fact]
    public void Dispose_WhenServerTaskFaultedByBrokenPipeDuringTeardown_ShouldNotThrow()
    {
        var sessionManager = new SessionManager();
        using var server = new NamedPipeServerStream(
            $"WpfDevTools_Test_{Guid.NewGuid():N}",
            PipeDirection.InOut);
        var connected = new ConnectedWaitSession(
            sessionManager,
            server,
            Task.FromException(new IOException("Pipe is broken.")),
            []);

        var dispose = connected.Dispose;
        dispose.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_WhenClientDisconnectsBeforePendingServerResponse_ShouldNotThrow()
    {
        const int processId = 7631;
        var requestSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var connected = await ConnectedWaitSessionBuilder.CreateAsync(
            processId,
            new object(),
            async (request, _) =>
            {
                requestSeen.TrySetResult();
                await releaseResponse.Task.ConfigureAwait(false);
                return new { success = true };
            });

        var client = connected.SessionManager.GetPipeClient(processId)!;
        var requestTask = client.SendRequestAsync(
            "get_dp_value_source",
            "disconnect-before-response",
            new { propertyName = "Text" },
            CancellationToken.None);

        await requestSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.Dispose();
        releaseResponse.SetResult();

        await requestTask
            .Awaiting(task => task.WaitAsync(TimeSpan.FromSeconds(5)))
            .Should()
            .ThrowAsync<Exception>();

        var dispose = connected.Dispose;
        dispose.Should().NotThrow();
    }
}
