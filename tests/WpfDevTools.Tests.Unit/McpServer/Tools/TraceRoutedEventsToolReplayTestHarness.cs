using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class TraceRoutedEventsToolReplayTests
{
    private sealed class ConnectedTraceReplaySession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;

        public static async Task<ConnectedTraceReplaySession> CreateAsync(
            int processId,
            string[] responses,
            Action<InspectorRequest>? inspectRequest = null,
            Func<DateTimeOffset>? utcNowProvider = null)
        {
            var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var responseQueue = new Queue<string>(responses);

            var serverTask = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                try
                {
                    while (server.IsConnected && responseQueue.Count > 0)
                    {
                        var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                        var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
                        inspectRequest?.Invoke(request);

                        var response = new InspectorResponse
                        {
                            Id = request.Id,
                            CorrelationId = request.CorrelationId,
                            Result = JsonSerializer.Deserialize<JsonElement>(responseQueue.Dequeue())
                        };

                        await MessageFraming.WriteMessageAsync(
                            server,
                            JsonSerializer.Serialize(response),
                            CancellationToken.None);
                    }
                }
                catch (EndOfStreamException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            });

            var sessionManager = new SessionManager(
                McpServerConfiguration.RateLimitRequestsPerMinute,
                authManager: null,
                certManager: null,
                utcNowProvider: utcNowProvider);
            DisableCleanupTimer(sessionManager);
            sessionManager.AddSession(processId);
            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplacePipeClient(sessionManager, processId, client);

            return new ConnectedTraceReplaySession(sessionManager, server, serverTask);
        }

        public static Task<ConnectedTraceReplaySession> CreateAsync(int processId, params string[] responses) =>
            CreateAsync(processId, responses, inspectRequest: null, utcNowProvider: null);

        public void Dispose()
        {
            try
            {
                SessionManager.Dispose();
                server.Dispose();
                serverTask.GetAwaiter().GetResult();
            }
            finally
            {
                SessionManager.Dispose();
                server.Dispose();
            }
        }

        private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
        {
            ReplaceSessionManagerPipeClient(sessionManager, processId, replacement);
        }

        private static void DisableCleanupTimer(SessionManager sessionManager)
        {
            DisableSessionManagerCleanupTimer(sessionManager);
        }
    }
}
