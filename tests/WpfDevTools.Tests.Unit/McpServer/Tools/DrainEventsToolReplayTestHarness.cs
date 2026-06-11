using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class DrainEventsToolReplayTests
{
    private sealed class ConnectedReplaySession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask,
        List<string> requestMethods,
        List<RecordedInspectorRequest> requests) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;
        public IReadOnlyList<string> RequestMethods { get; } = requestMethods;
        public IReadOnlyList<RecordedInspectorRequest> Requests { get; } = requests;

        public static Task<ConnectedReplaySession> CreateAsync(int processId, params string[] responses)
            => CreateAsync(processId, onRequestAsync: null, responses);

        public static async Task<ConnectedReplaySession> CreateAsync(
            int processId,
            Func<int, RecordedInspectorRequest, SessionManager, Task>? onRequestAsync,
            params string[] responses)
        {
            var sessionManager = new SessionManager();
            DisableSessionManagerCleanupTimer(sessionManager);
            sessionManager.AddSession(processId);

            var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var requestMethods = new List<string>();
            var requests = new List<RecordedInspectorRequest>();
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
                        requestMethods.Add(request.Method);
                        var recordedRequest = new RecordedInspectorRequest(request.Method, request.Params?.Clone());
                        requests.Add(recordedRequest);

                        if (onRequestAsync is not null)
                        {
                            await onRequestAsync(requestMethods.Count, recordedRequest, sessionManager);
                        }

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
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            });

            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplaceSessionManagerPipeClient(sessionManager, processId, client);

            return new ConnectedReplaySession(sessionManager, server, serverTask, requestMethods, requests);
        }

        public void Dispose()
        {
            try
            {
                SessionManager.Dispose();
                server.Dispose();
                try
                {
                    serverTask.GetAwaiter().GetResult();
                }
                catch (EndOfStreamException)
                {
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
            finally
            {
                SessionManager.Dispose();
                server.Dispose();
            }
        }

    }

    private sealed record RecordedInspectorRequest(string Method, JsonElement? Params);
}
