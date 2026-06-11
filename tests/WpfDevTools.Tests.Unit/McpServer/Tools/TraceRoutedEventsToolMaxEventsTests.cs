using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class TraceRoutedEventsToolMaxEventsTests
{
    [Fact]
    public async Task ExecuteAsync_WithMaxEvents_ShouldForwardLimitToInspector()
    {
        const int processId = 62027;
        InspectorRequest? capturedRequest = null;
        using var connected = await ConnectedTraceSession.CreateAsync(processId, request => capturedRequest = request);
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get", maxEvents = 2 }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Params!.Value.GetProperty("maxEvents").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidMaxEvents_ShouldRejectBeforeInspectorRequest()
    {
        const int processId = 62028;
        using var connected = await ConnectedTraceSession.CreateAsync(processId);
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get", maxEvents = 0 }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        connected.RequestCount.Should().Be(0);
    }

    private sealed class ConnectedTraceSession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask,
        Func<int> getRequestCount) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;
        public int RequestCount => getRequestCount();

        public static async Task<ConnectedTraceSession> CreateAsync(
            int processId,
            Action<InspectorRequest>? inspectRequest = null)
        {
            var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var requestCount = 0;
            var serverTask = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                try
                {
                    while (server.IsConnected)
                    {
                        var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                        var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
                        requestCount++;
                        inspectRequest?.Invoke(request);

                        var response = new InspectorResponse
                        {
                            Id = request.Id,
                            CorrelationId = request.CorrelationId,
                            Result = JsonSerializer.Deserialize<JsonElement>(
                                """{"success":true,"mode":"get","isTracing":false,"eventCount":0,"events":[],"handlerInvocationCount":0}""")
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

            var sessionManager = new SessionManager();
            DisableSessionManagerCleanupTimer(sessionManager);
            sessionManager.AddSession(processId);
            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplaceSessionManagerPipeClient(sessionManager, processId, client);

            return new ConnectedTraceSession(sessionManager, server, serverTask, () => requestCount);
        }

        public void Dispose()
        {
            try
            {
                SessionManager.Dispose();
                server.Dispose();
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
            finally
            {
                SessionManager.Dispose();
                server.Dispose();
            }
        }
    }
}
