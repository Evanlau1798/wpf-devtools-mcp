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
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class StateSnapshotRestoreContractTests
{
    [Fact]
    public async Task RestoreStateSnapshot_ShouldSkipReadOnlyViewModelPropertiesWithoutFailing()
    {
        const int processId = 51010;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "get_viewmodel" => (object)new
                {
                    success = true,
                    typeName = "SampleViewModel",
                    properties = new[]
                    {
                        new { name = "CanSave", type = "Boolean", value = "False", canWrite = false }
                    }
                },
                "get_binding_errors" => (object)new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                },
                "get_validation_errors" => (object)new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        var captureTool = new CaptureStateSnapshotTool(connected.SessionManager);
        var captureResult = JsonSerializer.SerializeToElement(await captureTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            viewModelPropertyNames = new[] { "CanSave" }
        }), CancellationToken.None));

        var snapshotId = captureResult.GetProperty("snapshotId").GetString();
        var restoreTool = new RestoreStateSnapshotTool(connected.SessionManager);
        var restoreResult = JsonSerializer.SerializeToElement(await restoreTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            snapshotId
        }), CancellationToken.None));

        restoreResult.GetProperty("success").GetBoolean().Should().BeTrue();
        restoreResult.GetProperty("restoredViewModelPropertyCount").GetInt32().Should().Be(0);
        restoreResult.GetProperty("skippedViewModelPropertyCount").GetInt32().Should().Be(1);
        restoreResult.GetProperty("warnings").GetArrayLength().Should().Be(0);
        restoreResult.GetProperty("skippedViewModelProperties")[0].GetProperty("propertyName").GetString().Should().Be("CanSave");
        restoreResult.GetProperty("skippedViewModelProperties")[0].GetProperty("verified").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var navigationState).Should().BeTrue();
        navigationState!.ActiveSnapshotId.Should().BeNull();
        connected.RequestMethods.Should().Equal("get_viewmodel", "get_binding_errors", "get_validation_errors", "get_viewmodel");
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldRemainFailedWhenWritablePropertyRestoreActuallyFails()
    {
        const int processId = 51011;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "get_viewmodel" => (object)new
                {
                    success = true,
                    typeName = "SampleViewModel",
                    properties = new[]
                    {
                        new { name = "Name", type = "String", value = "Alice", canWrite = true }
                    }
                },
                "modify_viewmodel" => (object)new { success = false, error = "Setter failed." },
                "get_binding_errors" => (object)new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                },
                "get_validation_errors" => (object)new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        var captureTool = new CaptureStateSnapshotTool(connected.SessionManager);
        var captureResult = JsonSerializer.SerializeToElement(await captureTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            viewModelPropertyNames = new[] { "Name" }
        }), CancellationToken.None));

        var snapshotId = captureResult.GetProperty("snapshotId").GetString();
        var restoreTool = new RestoreStateSnapshotTool(connected.SessionManager);
        var restoreResult = JsonSerializer.SerializeToElement(await restoreTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            snapshotId
        }), CancellationToken.None));

        restoreResult.GetProperty("success").GetBoolean().Should().BeFalse();
        restoreResult.GetProperty("restoredViewModelPropertyCount").GetInt32().Should().Be(0);
        restoreResult.GetProperty("skippedViewModelPropertyCount").GetInt32().Should().Be(0);
        restoreResult.GetProperty("warnings")[0].GetString().Should().Contain("Name");
        connected.SessionManager.TryGetNavigationState(processId, out var navigationState).Should().BeTrue();
        navigationState!.ActiveSnapshotId.Should().Be(snapshotId);
        connected.RequestMethods.Should().Equal("get_viewmodel", "get_binding_errors", "get_validation_errors", "modify_viewmodel");
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldFailWhenSkippedReadOnlyPropertyCannotBeVerifiedAfterRestore()
    {
        const int processId = 51012;
        var getViewModelCallCount = 0;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "get_viewmodel" => (object)(++getViewModelCallCount switch
                {
                    1 => new
                    {
                        success = true,
                        typeName = "SampleViewModel",
                        properties = new[]
                        {
                            new { name = "CanSave", type = "Boolean", value = "False", canWrite = false }
                        }
                    },
                    _ => new
                    {
                        success = true,
                        typeName = "SampleViewModel",
                        properties = new[]
                        {
                            new { name = "CanSave", type = "Boolean", value = "True", canWrite = false }
                        }
                    }
                }),
                "get_binding_errors" => (object)new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                },
                "get_validation_errors" => (object)new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        var captureTool = new CaptureStateSnapshotTool(connected.SessionManager);
        var captureResult = JsonSerializer.SerializeToElement(await captureTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            viewModelPropertyNames = new[] { "CanSave" }
        }), CancellationToken.None));

        var snapshotId = captureResult.GetProperty("snapshotId").GetString();
        var restoreTool = new RestoreStateSnapshotTool(connected.SessionManager);
        var restoreResult = JsonSerializer.SerializeToElement(await restoreTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            snapshotId
        }), CancellationToken.None));

        restoreResult.GetProperty("success").GetBoolean().Should().BeFalse();
        restoreResult.GetProperty("skippedViewModelPropertyCount").GetInt32().Should().Be(1);
        restoreResult.GetProperty("skippedViewModelProperties")[0].GetProperty("verified").GetBoolean().Should().BeFalse();
        restoreResult.GetProperty("warnings")[0].GetString().Should().Contain("CanSave");
        connected.RequestMethods.Should().Equal("get_viewmodel", "get_binding_errors", "get_validation_errors", "get_viewmodel");
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldClassifyComplexNullReferenceAsSkippedCapabilityBoundary()
    {
        const int processId = 51013;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "get_viewmodel" => (object)new
                {
                    success = true,
                    typeName = "SampleViewModel",
                    properties = new[]
                    {
                        new { name = "SelectedTask", type = "TaskItem", value = (string?)null, canWrite = true }
                    }
                },
                "get_binding_errors" => (object)new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                },
                "get_validation_errors" => (object)new
                {
                    success = true,
                    errorCount = 0,
                    errors = Array.Empty<object>()
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        var captureTool = new CaptureStateSnapshotTool(connected.SessionManager);
        var captureResult = JsonSerializer.SerializeToElement(await captureTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            viewModelPropertyNames = new[] { "SelectedTask" }
        }), CancellationToken.None));

        var snapshotId = captureResult.GetProperty("snapshotId").GetString();
        var restoreTool = new RestoreStateSnapshotTool(connected.SessionManager);
        var restoreResult = JsonSerializer.SerializeToElement(await restoreTool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            snapshotId
        }), CancellationToken.None));

        restoreResult.GetProperty("success").GetBoolean().Should().BeTrue();
        restoreResult.GetProperty("restoredViewModelPropertyCount").GetInt32().Should().Be(0);
        restoreResult.GetProperty("skippedViewModelPropertyCount").GetInt32().Should().Be(1);
        restoreResult.GetProperty("warnings").GetArrayLength().Should().Be(0);
        restoreResult.GetProperty("skippedViewModelProperties")[0].GetProperty("restoreDisposition").GetString().Should().Be("SkippedComplexReference");
        restoreResult.GetProperty("skippedViewModelProperties")[0].GetProperty("reason").GetString().Should().Contain("complex reference");
        connected.RequestMethods.Should().Equal("get_viewmodel", "get_binding_errors", "get_validation_errors", "get_viewmodel");
    }

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
