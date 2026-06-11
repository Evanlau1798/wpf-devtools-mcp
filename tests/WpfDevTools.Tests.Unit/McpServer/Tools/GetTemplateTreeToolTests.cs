using System.IO.Pipes;
using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class GetTemplateTreeToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        // Arrange
        var tool = new GetTemplateTreeTool(new SessionManager());
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("NotConnected");
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new GetTemplateTreeTool(new SessionManager());
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task Execute_WithValidParameters_ShouldForwardElementIdAndDepthToInspector()
    {
        // Arrange
        const int processId = 43123;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request =>
            {
                request.Method.Should().Be("get_template_tree");
                request.Params.HasValue.Should().BeTrue();

                var payload = request.Params!.Value;
                payload.GetProperty("elementId").GetString().Should().Be("myControl");
                payload.GetProperty("depth").GetInt32().Should().Be(3);

                return new
                {
                    success = true,
                    tree = new { elementId = "myControl", type = "Button", childCount = 0, children = Array.Empty<object>() }
                };
            });

        var tool = new GetTemplateTreeTool(connected.SessionManager);
        var parameters = new { processId, elementId = "myControl", depth = 3 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("tree").GetProperty("elementId").GetString().Should().Be("myControl");
    }

    [Fact]
    public async Task Execute_WithPayloadCaps_ShouldForwardCapsToInspector()
    {
        const int processId = 43124;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request =>
            {
                request.Method.Should().Be("get_template_tree");
                request.Params.HasValue.Should().BeTrue();

                var payload = request.Params!.Value;
                payload.GetProperty("elementId").GetString().Should().Be("myControl");
                payload.GetProperty("depth").GetInt32().Should().Be(4);
                payload.GetProperty("maxNodes").GetInt32().Should().Be(25);
                payload.GetProperty("maxChildrenPerNode").GetInt32().Should().Be(5);

                return new
                {
                    success = true,
                    returnedNodeCount = 25,
                    omittedNodeCount = 12,
                    truncated = true
                };
            });

        var tool = new GetTemplateTreeTool(connected.SessionManager);
        var parameters = new { processId, elementId = "myControl", depth = 4, maxNodes = 25, maxChildrenPerNode = 5 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithDepthAboveLimit_ShouldReturnStructuredInvalidArgumentError()
    {
        // Arrange
        var tool = new GetTemplateTreeTool(new SessionManager());

        // Act
        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId = 12345, elementId = "myControl", depth = 101 }),
            CancellationToken.None);

        // Assert
        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        resultJson.GetProperty("hint").GetString().Should().Contain("depth");
    }

    private static async Task<ConnectedTemplateTreeSession> CreateConnectedSessionAsync(
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
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);

        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        return new ConnectedTemplateTreeSession(sessionManager, server, serverTask);
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        ReplaceSessionManagerPipeClient(sessionManager, processId, replacement);
    }

    private sealed class ConnectedTemplateTreeSession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;

        public void Dispose()
        {
            try
            {
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
