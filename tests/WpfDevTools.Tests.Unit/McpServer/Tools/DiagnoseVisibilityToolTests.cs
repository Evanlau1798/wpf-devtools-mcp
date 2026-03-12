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

public sealed class DiagnoseVisibilityToolTests
{
    [Fact]
    public async Task ExecuteAsync_WhenAncestorIsCollapsed_ShouldReportRootCauseAndSuggestedFix()
    {
        const int processId = 52010;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            [
                """{"success":true,"elementId":"Text_1","isUserVisible":false,"checks":[{"key":"ancestor:Panel_1","passed":false}],"rootCause":"Ancestor HiddenByAncestorPanel has Visibility=Collapsed.","suggestedFix":"Set HiddenByAncestorPanel Visibility to Visible."}"""
            ]);

        var tool = new DiagnoseVisibilityTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Text_1"
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isUserVisible").GetBoolean().Should().BeFalse();
        result.GetProperty("rootCause").GetString().Should().Contain("HiddenByAncestorPanel");
        result.GetProperty("suggestedFix").GetString().Should().Contain("Visibility");
    }

    [Fact]
    public async Task ExecuteAsync_WhenElementIsVisible_ShouldReportNoBlockers()
    {
        const int processId = 52011;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            [
                """{"success":true,"elementId":"Text_1","isUserVisible":true,"checks":[{"key":"selfVisibility","passed":true}],"rootCause":null,"suggestedFix":null}"""
            ]);

        var tool = new DiagnoseVisibilityTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Text_1"
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("isUserVisible").GetBoolean().Should().BeTrue();
        result.GetProperty("checks").GetArrayLength().Should().BeGreaterThan(0);
        result.GetProperty("rootCause").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static async Task<ConnectedVisibilitySession> CreateConnectedSessionAsync(int processId, IReadOnlyList<string> responses)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var queue = new Queue<string>(responses);

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

                    var resultJson = queue.Count > 0
                        ? queue.Dequeue()
                        : """{"success":false,"error":"Unexpected request","errorCode":"OperationFailed","hint":"Add more mocked inspector responses for this test."}""";

                    var response = new InspectorResponse
                    {
                        Id = request.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.Deserialize<JsonElement>(resultJson),
                        Error = null
                    };

                    await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
                }
            }
            catch (EndOfStreamException)
            {
            }
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        return new ConnectedVisibilitySession(sessionManager, server, serverTask);
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

    private sealed class ConnectedVisibilitySession(SessionManager sessionManager, NamedPipeServerStream server, Task serverTask) : IDisposable
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
