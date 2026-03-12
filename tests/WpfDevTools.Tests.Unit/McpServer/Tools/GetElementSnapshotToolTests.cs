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

public sealed class GetElementSnapshotToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldAggregateElementDiagnosticsIntoSingleSnapshot()
    {
        const int processId = 51030;
        var observedMethods = new List<string>();
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = true,
                    tree = new
                    {
                        elementId = "TextBox_1",
                        type = "TextBox",
                        name = "NameTextBox",
                        childCount = 0
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    chain = new[]
                    {
                        new { dataContextType = "TestViewModel" }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    bindings = new[]
                    {
                        new { propertyName = "Text", path = "Name", status = "Active", currentValue = "Alice" }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 1,
                    errors = new[]
                    {
                        new { errorContent = "Name is required", elementType = "TextBox", elementName = "NameTextBox" }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    hasStyle = true,
                    styleCount = 1,
                    styles = new[]
                    {
                        new { styleType = "Implicit", targetType = "TextBox", setterCount = 3 }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    actualWidth = 120.0,
                    actualHeight = 24.0,
                    horizontalAlignment = "Stretch",
                    verticalAlignment = "Top"
                }),
                JsonSerializer.Serialize(new { success = true, propertyName = "Text", currentValue = "Alice", baseValueSource = "LocalValue" }),
                JsonSerializer.Serialize(new { success = false, error = "DependencyProperty 'Content' not found", errorCode = "PropertyNotFound", hint = "Verify the propertyName is valid for the target element type." }),
                JsonSerializer.Serialize(new { success = true, propertyName = "Visibility", currentValue = "Visible", baseValueSource = "Default" }),
                JsonSerializer.Serialize(new { success = true, propertyName = "IsEnabled", currentValue = "True", baseValueSource = "Default" }),
                JsonSerializer.Serialize(new { success = true, propertyName = "Opacity", currentValue = "1", baseValueSource = "Default" })
            },
            observedMethods);

        var tool = new GetElementSnapshotTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "TextBox_1"
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("elementId").GetString().Should().Be("TextBox_1");
        result.GetProperty("elementType").GetString().Should().Be("TextBox");
        result.GetProperty("elementName").GetString().Should().Be("NameTextBox");
        result.GetProperty("dataContextType").GetString().Should().Be("TestViewModel");
        result.GetProperty("bindings").GetArrayLength().Should().Be(1);
        result.GetProperty("validationErrors").GetArrayLength().Should().Be(1);
        result.GetProperty("properties").GetProperty("Text").GetProperty("currentValue").GetString().Should().Be("Alice");
        observedMethods.Should().BeEquivalentTo(
            [
                "get_visual_tree",
                "get_datacontext_chain",
                "get_bindings",
                "get_validation_errors",
                "get_applied_styles",
                "get_layout_info",
                "get_dp_value_source",
                "get_dp_value_source",
                "get_dp_value_source",
                "get_dp_value_source",
                "get_dp_value_source"
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ExecuteAsync_WhenIdentityLookupFails_ShouldReturnStructuredError()
    {
        const int processId = 51031;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Element not found: 'TextBox_1'",
                    errorCode = "ElementNotFound",
                    hint = "Call get_visual_tree first to confirm the target elementId."
                })
            });

        var tool = new GetElementSnapshotTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "TextBox_1"
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("ElementNotFound");
    }

    private static async Task<ConnectedStateSession> CreateConnectedSessionAsync(
        int processId,
        IReadOnlyList<string> resultJsonSequence,
        List<string>? observedMethods = null)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var responses = new Queue<string>(resultJsonSequence);
        const string FallbackResponseJson = """{"success":false,"error":"DependencyProperty fallback","errorCode":"PropertyNotFound","hint":"Test fallback response"}""";

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
                    observedMethods?.Add(request.Method);
                    var response = new InspectorResponse
                    {
                        Id = request.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.Deserialize<JsonElement>(responses.Count > 0 ? responses.Dequeue() : FallbackResponseJson),
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

        return new ConnectedStateSession(sessionManager, server, serverTask);
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

    private sealed class ConnectedStateSession(SessionManager sessionManager, NamedPipeServerStream server, Task serverTask) : IDisposable
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
