using System.Collections.Concurrent;
using System.IO;
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

public sealed class StateSnapshotDependencyPropertySafetyTests
{
    [Fact]
    public async Task CaptureStateSnapshot_ShouldCaptureRestoreHandle_ForBindingBackedDependencyProperty()
    {
        const int processId = 51020;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "get_dp_value_source" => (object)new
                {
                    success = true,
                    propertyName = "Text",
                    currentValue = "Alice",
                    hadLocalValue = true,
                    localValue = "{Binding Name}",
                    baseValueSource = "LocalValue",
                    isExpression = true
                },
                "capture_dp_expression_restore" => new
                {
                    success = true,
                    canRestore = true,
                    restoreToken = "expr_token_1",
                    expressionKind = "Binding"
                },
                "get_binding_errors" => EmptyErrors(),
                "get_validation_errors" => EmptyErrors(),
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        var tool = new CaptureStateSnapshotTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "TextBox_1",
            propertyNames = new[] { "Text" }
        }), CancellationToken.None));

        var snapshotId = result.GetProperty("snapshotId").GetString();
        connected.SessionManager.TryGetStateSnapshot(processId, snapshotId!, out var snapshot).Should().BeTrue();
        snapshot!.DependencyProperties.Should().ContainSingle();
        snapshot.DependencyProperties[0].CanRestore.Should().BeTrue();
        snapshot.DependencyProperties[0].ExpressionRestoreToken.Should().Be("expr_token_1");
        snapshot.DependencyProperties[0].ExpressionKind.Should().Be("Binding");
        snapshot.DependencyProperties[0].SkipReason.Should().BeNull();
        result.GetProperty("snapshotSummary").GetProperty("restorableDependencyPropertyCount").GetInt32().Should().Be(1);
        result.GetProperty("snapshotSummary").GetProperty("skippedDependencyPropertyCount").GetInt32().Should().Be(0);
        connected.RequestMethods.Should().ContainInOrder(
            "get_dp_value_source",
            "capture_dp_expression_restore");
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldRestoreBindingBackedDependencyProperty_WhenRestoreHandleWasCaptured()
    {
        const int processId = 51021;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "get_dp_value_source" => new
                {
                    success = true,
                    propertyName = "Text",
                    currentValue = "Alice",
                    hadLocalValue = true,
                    localValue = "{Binding Name}",
                    baseValueSource = "LocalValue",
                    isExpression = true
                },
                "capture_dp_expression_restore" => new
                {
                    success = true,
                    canRestore = true,
                    restoreToken = "expr_token_2",
                    expressionKind = "Binding"
                },
                "restore_dp_expression" => new
                {
                    success = true,
                    propertyName = "Text",
                    restoredExpression = true,
                    expressionKind = "Binding",
                    currentValue = "Alice"
                },
                "get_binding_errors" => EmptyErrors(),
                "get_validation_errors" => EmptyErrors(),
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        var captureTool = new CaptureStateSnapshotTool(connected.SessionManager);
        var captureResult = JsonSerializer.SerializeToElement(await captureTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "TextBox_1",
            propertyNames = new[] { "Text" }
        }), CancellationToken.None));

        var snapshotId = captureResult.GetProperty("snapshotId").GetString();
        var restoreTool = new RestoreStateSnapshotTool(connected.SessionManager);
        var restoreResult = JsonSerializer.SerializeToElement(await restoreTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            snapshotId
        }), CancellationToken.None));

        restoreResult.GetProperty("success").GetBoolean().Should().BeTrue();
        restoreResult.GetProperty("restoredDependencyPropertyCount").GetInt32().Should().Be(1);
        restoreResult.GetProperty("skippedDependencyPropertyCount").GetInt32().Should().Be(0);
        restoreResult.GetProperty("warnings").GetArrayLength().Should().Be(0);
        connected.RequestMethods.Should().ContainInOrder(
            "get_dp_value_source",
            "capture_dp_expression_restore",
            "restore_dp_expression");
    }

    [Fact]
    public async Task CaptureStateSnapshot_ShouldKeepNonBindingExpressionAsSkippedCapabilityBoundary()
    {
        const int processId = 51022;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "get_dp_value_source" => (object)new
                {
                    success = true,
                    propertyName = "Visibility",
                    currentValue = "Collapsed",
                    hadLocalValue = true,
                    localValue = "{DynamicResource HiddenVisibility}",
                    baseValueSource = "LocalValue",
                    isExpression = true
                },
                "capture_dp_expression_restore" => new
                {
                    success = true,
                    canRestore = false,
                    reason = "Property 'Visibility' uses an expression that is not restorable through BindingOperations.SetBinding in the current session."
                },
                "get_binding_errors" => EmptyErrors(),
                "get_validation_errors" => EmptyErrors(),
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        var tool = new CaptureStateSnapshotTool(connected.SessionManager);
        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "GhostPanel",
            propertyNames = new[] { "Visibility" }
        }), CancellationToken.None));

        var snapshotId = result.GetProperty("snapshotId").GetString();
        connected.SessionManager.TryGetStateSnapshot(processId, snapshotId!, out var snapshot).Should().BeTrue();
        snapshot!.DependencyProperties[0].CanRestore.Should().BeFalse();
        snapshot.DependencyProperties[0].ExpressionRestoreToken.Should().BeNull();
        snapshot.DependencyProperties[0].SkipReason.Should().Contain("not restorable");
        result.GetProperty("snapshotSummary").GetProperty("skippedDependencyPropertyCount").GetInt32().Should().Be(1);
    }

    private static object EmptyErrors() => new
    {
        success = true,
        errorCount = 0,
        errors = Array.Empty<object>()
    };

    private static async Task<ConnectedStateSession> CreateConnectedSessionAsync(
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
        var requestMethods = new ConcurrentQueue<string>();

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();

            try
            {
                while (server.IsConnected)
                {
                    string requestJson;
                    try
                    {
                        requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }

                    var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                    request.Should().NotBeNull();
                    requestMethods.Enqueue(request!.Method);

                    var response = new InspectorResponse
                    {
                        Id = request.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.SerializeToElement(responder(request)),
                        Error = null
                    };

                    await MessageFraming.WriteMessageAsync(
                        server,
                        JsonSerializer.Serialize(response),
                        CancellationToken.None);
                }
            }
            catch (IOException)
            {
            }
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);

        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        return new ConnectedStateSession(sessionManager, server, serverTask, requestMethods);
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
        Task serverTask,
        ConcurrentQueue<string> requestMethods) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;
        public IReadOnlyCollection<string> RequestMethods => requestMethods.ToArray();

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
