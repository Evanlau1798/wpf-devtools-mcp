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

public sealed class GetAffectedElementsToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithoutPropertyName_ShouldReturnStructuredError()
    {
        var tool = new GetAffectedElementsTool(new SessionManager());

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId = 12345 }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Contain("propertyName");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldForwardAffectedElementQueryParameters()
    {
        const int processId = 51033;
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        JsonElement? observedParams = null;

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
            request.Should().NotBeNull();
            request!.Method.Should().Be("get_affected_elements");
            observedParams = request.Params;

            var response = new InspectorResponse
            {
                Id = request.Id,
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    confidence = "best-effort",
                    matchStrategy = "simple-path-match",
                    requiresVerification = true,
                    affectedCount = 1,
                    affectedElements = new[]
                    {
                        new
                        {
                            elementId = "NameTextBox_1",
                            elementType = "TextBox",
                            elementName = "NameTextBox",
                            propertyName = "Text",
                            bindingPath = "Name",
                            currentValue = "Alice"
                        }
                    }
                })
            };

            await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
        });

        using var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);
        var tool = new GetAffectedElementsTool(sessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                elementId = "RootPanel_1",
                propertyName = "Name",
                viewModelType = "TestViewModel",
                recursive = true
            }),
            CancellationToken.None));

        await serverTask;

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        observedParams.Should().NotBeNull();
        observedParams!.Value.GetProperty("elementId").GetString().Should().Be("RootPanel_1");
        observedParams.Value.GetProperty("propertyName").GetString().Should().Be("Name");
        observedParams.Value.GetProperty("viewModelType").GetString().Should().Be("TestViewModel");
        observedParams.Value.GetProperty("recursive").GetBoolean().Should().BeTrue();
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
