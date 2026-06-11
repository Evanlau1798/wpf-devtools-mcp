using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class FindElementsToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnStructuredNotConnectedError()
    {
        var tool = new FindElementsTool(new SessionManager());

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 53001,
            typeName = "Button"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("NotConnected");
    }

    [Fact]
    public async Task Execute_WithSearchOptions_ShouldForwardParametersToInspector()
    {
        const int processId = 53002;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request =>
            {
                request.Method.Should().Be("find_elements");
                request.Params.HasValue.Should().BeTrue();

                var payload = request.Params!.Value;
                payload.GetProperty("matchMode").GetString().Should().Be("contains");
                payload.GetProperty("maxTraversalNodes").GetInt32().Should().Be(75);
                payload.GetProperty("typeNames").EnumerateArray()
                    .Select(item => item.GetString())
                    .Should().Equal("TextBox", "ComboBox");

                return new { success = true, resultCount = 0, truncated = false, results = Array.Empty<object>() };
            });

        var tool = new FindElementsTool(connected.SessionManager);
        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            typeNames = new[] { "TextBox", "ComboBox" },
            matchMode = "contains",
            maxTraversalNodes = 75
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    private static async Task<ConnectedFindElementsSession> CreateConnectedSessionAsync(
        int processId,
        Func<InspectorRequest, object> responder)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                var result = responder(request!);

                var response = new InspectorResponse
                {
                    Id = request!.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.SerializeToElement(result),
                    Error = null
                };

                await MessageFraming.WriteMessageAsync(
                    server,
                    JsonSerializer.Serialize(response),
                    CancellationToken.None);
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
        DisableCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        return new ConnectedFindElementsSession(sessionManager, server, serverTask);
    }

    private static void DisableCleanupTimer(SessionManager sessionManager)
    {
        DisableSessionManagerCleanupTimer(sessionManager);
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        ReplaceSessionManagerPipeClient(sessionManager, processId, replacement);
    }

    private sealed class ConnectedFindElementsSession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;

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
}
