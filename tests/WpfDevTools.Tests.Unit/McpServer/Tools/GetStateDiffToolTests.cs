using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("ToolCallHelperState")]
public sealed class GetStateDiffToolTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCompareSnapshotAgainstCurrentState()
    {
        const int processId = 51020;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            new[]
            {
                JsonSerializer.Serialize(new
                {
                    success = true,
                    propertyName = "Text",
                    currentValue = "Alice",
                    hadLocalValue = true,
                    localValue = "Alice",
                    baseValueSource = "LocalValue"
                }),
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
                    focusKind = "Logical",
                    focusedElementId = "NameTextBox"
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 1,
                    errors = new[]
                    {
                        new { elementId = "TextBox_1", propertyName = "Text", bindingPath = "InvalidPropertyName", message = "Binding path error" }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 1,
                    errors = new[]
                    {
                        new { errorContent = "Name is required", isRuleError = false, ruleType = "DataErrorValidationRule", elementType = "TextBox", elementName = "NameTextBox" }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    propertyName = "Text",
                    currentValue = "Bob",
                    hadLocalValue = true,
                    localValue = "Bob",
                    baseValueSource = "LocalValue"
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    typeName = "SampleViewModel",
                    properties = new[]
                    {
                        new { name = "Name", type = "String", value = "Bob", canWrite = true }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 1,
                    errors = new[]
                    {
                        new { elementId = "TextBox_2", propertyName = "Text", bindingPath = "Name", message = "Null DataContext" }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 1,
                    errors = new[]
                    {
                        new { errorContent = "Age must be greater than 0", isRuleError = false, ruleType = "DataErrorValidationRule", elementType = "TextBox", elementName = "AgeTextBox" }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    errorCount = 1,
                    errors = new[]
                    {
                        new { errorContent = "Age must be greater than 0", isRuleError = false, ruleType = "DataErrorValidationRule", elementType = "TextBox", elementName = "AgeTextBox" }
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    success = true,
                    focusKind = "Logical",
                    focusedElementId = "SaveButton"
                }),
            });

        var captureTool = new CaptureStateSnapshotTool(connected.SessionManager);
        var captureResult = JsonSerializer.SerializeToElement(await captureTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "NameTextBox",
            propertyNames = new[] { "Text" },
            viewModelPropertyNames = new[] { "Name" },
            includeFocus = true
        }), CancellationToken.None));

        var snapshotId = captureResult.GetProperty("snapshotId").GetString();
        var tool = new GetStateDiffTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            snapshotId,
            trigger = "click_element(SaveButton)"
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("trigger").GetString().Should().Be("click_element(SaveButton)");
        result.GetProperty("propertyChanges").GetArrayLength().Should().Be(1);
        result.GetProperty("viewModelChanges").GetArrayLength().Should().Be(1);
        result.GetProperty("newBindingErrors").GetArrayLength().Should().Be(1);
        result.GetProperty("resolvedBindingErrors").GetArrayLength().Should().Be(1);
        result.GetProperty("validationChanges").GetArrayLength().Should().Be(2);
        result.GetProperty("focusChange").GetProperty("changed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownSnapshot_ShouldReturnStructuredError()
    {
        var tool = new GetStateDiffTool(new SessionManager());

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 51021,
            snapshotId = "missing"
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("hint").GetString().Should().Contain("capture_state_snapshot");
    }

    [Fact]
    public async Task GetStateDiff_Navigation_WithChanges_ShouldSuggestRestore()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_123",
                propertyChanges = new[] { new { propertyName = "Text" } },
                viewModelChanges = Array.Empty<object>(),
                newBindingErrors = Array.Empty<object>(),
                resolvedBindingErrors = Array.Empty<object>(),
                validationChanges = Array.Empty<object>(),
                focusChange = (object?)null
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("snapshotId", "snapshot_123")),
            CancellationToken.None,
            toolName: "get_state_diff");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("restore_state_snapshot");
        nextSteps[0].GetProperty("params").GetProperty("snapshotId").GetString().Should().Be("snapshot_123");
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
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
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
        ReplaceSessionManagerPipeClient(sessionManager, processId, replacement);
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
