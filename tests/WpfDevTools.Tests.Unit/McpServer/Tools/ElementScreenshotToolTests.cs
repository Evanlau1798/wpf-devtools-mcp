using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ElementScreenshotToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        // Arrange
        var tool = new ElementScreenshotTool(new SessionManager());
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new ElementScreenshotTool(new SessionManager());
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
    public async Task Execute_WithoutOutputMode_ShouldDefaultToMetadata()
    {
        const int processId = 12345;
        const string pipeName = "WpfDevTools_Test_ElementScreenshotDefault";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var requestCompletion = new TaskCompletionSource<InspectorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                requestCompletion.TrySetResult(request!);

                var response = new InspectorResponse
                {
                    Id = request!.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.Deserialize<JsonElement>("""{"success":true,"width":160,"height":80,"format":"png","byteLength":256}""")
                };

                await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
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
        var tool = new ElementScreenshotTool(sessionManager);

        try
        {
            var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new { processId, elementId = "myControl" }), CancellationToken.None));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            var request = await requestCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            request.Params.Should().NotBeNull();
            request.Params!.Value.TryGetProperty("outputMode", out var outputMode).Should().BeTrue();
            outputMode.GetString().Should().Be("metadata");
        }
        finally
        {
            sessionManager.Dispose();
            server.Dispose();
            await serverTask;
        }
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
}
