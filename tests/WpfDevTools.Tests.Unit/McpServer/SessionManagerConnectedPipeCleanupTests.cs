using System.IO.Pipes;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SessionManagerConnectedPipeCleanupTests
{
    [Fact]
    public async Task CleanupDeadSessions_ShouldRemoveSessionsForExitedProcessesEvenWhenPipeClientStillReportsConnected()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var serverLifetime = new CancellationTokenSource();
        var serverTask = Task.Run(async () =>
        {
            try
            {
                await server.WaitForConnectionAsync(serverLifetime.Token);
                await Task.Delay(Timeout.InfiniteTimeSpan, serverLifetime.Token);
            }
            catch (IOException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        using var manager = new SessionManager();
        DisableCleanupTimer(manager);
        manager.AddSession(processId);

        try
        {
            using var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplacePipeClient(manager, processId, client);

            InvokeCleanupDeadSessions(manager);

            manager.HasSession(processId).Should().BeFalse(
                "cleanup must trust process liveness over stale pipe connection state");
        }
        finally
        {
            serverLifetime.Cancel();
            server.Dispose();
            await serverTask;
        }
    }

    private static void InvokeCleanupDeadSessions(SessionManager manager)
    {
        manager.CleanupDeadSessions();
    }

    private static void DisableCleanupTimer(SessionManager manager)
    {
        manager._cleanupTimer.Should().NotBeNull();
        manager._cleanupTimer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        var pipeClients = sessionManager._pipeClients;
        pipeClients.Should().NotBeNull();
        if (pipeClients!.TryGetValue(processId, out var existingClient))
        {
            existingClient.Dispose();
        }

        pipeClients[processId] = replacement;
    }
}
