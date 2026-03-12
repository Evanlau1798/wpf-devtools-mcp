using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class SceneSummaryToolTests
{
    [Fact]
    public async Task GetUiSummaryTool_ShouldPassThroughStructuredSummaryPayload()
    {
        const int processId = 60210;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"rootElementId":"Window_1","semanticNodeCount":2,"summaryText":"- Button SaveButton","nodes":[{"elementId":"Button_1"}]}""");

        var tool = new GetUiSummaryTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            depth = 2
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("semanticNodeCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetUiSummaryTool_ShouldForwardDepthModeToInspectorRequest()
    {
        const int processId = 60212;
        const string pipeName = "WpfDevTools_Test_SceneDepthMode";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var requestCompletion = new TaskCompletionSource<InspectorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
            requestCompletion.TrySetResult(request!);

            var response = new InspectorResponse
            {
                Id = request!.Id,
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.Deserialize<JsonElement>("""{"success":true,"rootElementId":"Window_1","semanticNodeCount":1,"summaryText":"- TextBox Box","nodes":[{"elementId":"TextBox_1"}]}""")
            };

            await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        try
        {
            var tool = new GetUiSummaryTool(sessionManager);
            var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
            {
                processId,
                depth = 2,
                depthMode = "semantic"
            }), CancellationToken.None));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            var request = await requestCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            request.Params.Should().NotBeNull();
            request.Params!.Value.TryGetProperty("depthMode", out var depthMode).Should().BeTrue();
            depthMode.GetString().Should().Be("semantic");
        }
        finally
        {
            sessionManager.Dispose();
            server.Dispose();
            await serverTask;
        }
    }

    [Fact]
    public async Task GetFormSummaryTool_ShouldPassThroughStructuredSummaryPayload()
    {
        const int processId = 60211;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"formScope":"StackPanel_1","summary":{"totalInputs":2,"emptyInputs":1,"errorCount":1,"isSubmittable":false},"inputs":[],"commands":[]}""");

        var tool = new GetFormSummaryTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summary").GetProperty("totalInputs").GetInt32().Should().Be(2);
    }

    private static async Task<ConnectedSceneSummarySession> CreateConnectedSessionAsync(int processId, string responseJson)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                while (server.IsConnected)
                {
                    var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                    var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                    request.Should().NotBeNull();
                    if (request is null)
                    {
                        throw new InvalidOperationException("Expected a valid InspectorRequest payload.");
                    }

                    var response = new InspectorResponse
                    {
                        Id = request.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.Deserialize<JsonElement>(responseJson),
                        Error = null
                    };

                    await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        return new ConnectedSceneSummarySession(sessionManager, server, serverTask);
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        var field = typeof(SessionManager).GetField("_pipeClients", BindingFlags.Instance | BindingFlags.NonPublic);
        var pipeClients = field!.GetValue(sessionManager) as Dictionary<int, NamedPipeClient>;
        if (pipeClients!.TryGetValue(processId, out var existingClient))
        {
            existingClient.Dispose();
        }

        pipeClients[processId] = replacement;
    }

    private sealed class ConnectedSceneSummarySession(SessionManager sessionManager, NamedPipeServerStream server, Task serverTask) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;

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
    }
}
