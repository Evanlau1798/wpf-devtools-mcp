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

public sealed class StateSnapshotToolTests
{
    [Fact]
    public async Task CaptureStateSnapshot_ShouldPersistSnapshotAndExposeSummary()
    {
        const int processId = 51001;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = true,
                    propertyName = "Width",
                    currentValue = "120",
                    hadLocalValue = true,
                    localValue = "120",
                    baseValueSource = "Local"
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    typeName = "SampleViewModel",
                    properties = new[]
                    {
                        new { name = "Name", type = "String", value = "Alice" }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    focusKind = "Logical",
                    focusedElementId = "TextBox_42"
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                })
            });

        var tool = new CaptureStateSnapshotTool(connected.SessionManager);
        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Button_1",
            propertyNames = new[] { "Width" },
            viewModelPropertyNames = new[] { "Name" },
            includeFocus = true
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("snapshotId").GetString().Should().NotBeNullOrWhiteSpace();
        json.GetProperty("snapshotSummary").GetProperty("dependencyPropertyCount").GetInt32().Should().Be(1);
        json.GetProperty("snapshotSummary").GetProperty("viewModelPropertyCount").GetInt32().Should().Be(1);
        json.GetProperty("snapshotSummary").GetProperty("capturedFocus").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldReplayStoredOperations()
    {
        const int processId = 51002;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = true,
                    propertyName = "Width",
                    currentValue = "120",
                    hadLocalValue = true,
                    localValue = "120",
                    baseValueSource = "Local"
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    focusKind = "Logical",
                    focusedElementId = "TextBox_42"
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                }),
                JsonSerializer.Serialize(new { success = true, propertyName = "Width", oldValue = "240", newValue = "120" }),
                JsonSerializer.Serialize(new { success = true, focused = true })
            });

        var captureTool = new CaptureStateSnapshotTool(connected.SessionManager);
        var captureResult = JsonSerializer.SerializeToElement(await captureTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Button_1",
            propertyNames = new[] { "Width" },
            includeFocus = true
        }), CancellationToken.None));
        var snapshotId = captureResult.GetProperty("snapshotId").GetString();

        var restoreTool = new RestoreStateSnapshotTool(connected.SessionManager);
        var restoreResult = await restoreTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            snapshotId
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(restoreResult);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("restoredDependencyPropertyCount").GetInt32().Should().Be(1);
        json.GetProperty("restoredFocus").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RestoreStateSnapshot_WithUnknownSnapshotId_ShouldReturnError()
    {
        var tool = new RestoreStateSnapshotTool(new SessionManager());

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 51003,
            snapshotId = "missing"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("hint").GetString().Should().Contain("capture_state_snapshot");
        json.GetProperty("error").GetString().Should().Contain("snapshotId");
    }

    [Fact]
    public async Task CaptureStateSnapshot_WithMissingViewModelProperty_ShouldReturnStructuredPropertyNotFound()
    {
        const int processId = 51004;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = true,
                    typeName = "SampleViewModel",
                    properties = new[]
                    {
                        new { name = "Name", type = "String", value = "Alice", canWrite = true }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                })
            });

        var tool = new CaptureStateSnapshotTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Button_1",
            viewModelPropertyNames = new[] { "MissingProperty" }
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("PropertyNotFound");
        json.GetProperty("hint").GetString().Should().Contain("propertyName");
        json.GetProperty("error").GetString().Should().Contain("MissingProperty");
    }

    [Fact]
    public async Task CaptureStateSnapshot_WhenInspectorStepFails_ShouldReturnStructuredError()
    {
        const int processId = 51005;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Element not found: 'Button_1'",
                    errorCode = "ElementNotFound",
                    hint = "Call get_visual_tree or get_logical_tree first to confirm the target elementId."
                })
            });

        var tool = new CaptureStateSnapshotTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Button_1",
            propertyNames = new[] { "Width" }
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("ElementNotFound");
        json.GetProperty("hint").GetString().Should().Contain("elementId");
        json.GetProperty("error").GetString().Should().Contain("get_dp_value_source");
    }

    private static async Task<ConnectedStateSession> CreateConnectedSessionAsync(int processId, IReadOnlyList<string> resultJsonSequence)
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
                foreach (var resultJson in resultJsonSequence)
                {
                    var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                    var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

                    var response = new InspectorResponse
                    {
                        Id = request!.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.Deserialize<JsonElement>(resultJson),
                        Error = null
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
        field.Should().NotBeNull();

        var pipeClients = field!.GetValue(sessionManager) as Dictionary<int, NamedPipeClient>;
        pipeClients.Should().NotBeNull();

        if (pipeClients!.TryGetValue(processId, out var existingClient))
        {
            existingClient.Dispose();
        }

        pipeClients[processId] = replacement;
    }

    private sealed class ConnectedStateSession(
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
