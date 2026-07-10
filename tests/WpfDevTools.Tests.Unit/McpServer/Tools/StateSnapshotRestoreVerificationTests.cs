using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed partial class StateSnapshotRestoreVerificationTests
{
    [Fact]
    public async Task RestoreStateSnapshot_ShouldExposeDependencyPropertyReadBackMismatch()
    {
        var processId = NextSyntheticProcessId();
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "set_dp_value" => new
                {
                    success = true,
                    propertyName = "Width",
                    oldValue = "240",
                    newValue = "120"
                },
                "get_dp_value_source" => new
                {
                    success = true,
                    propertyName = "Width",
                    currentValue = "240",
                    hadLocalValue = true,
                    localValue = "240",
                    baseValueSource = "LocalValue",
                    isExpression = false
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        const string snapshotId = "snapshot_dp_mismatch";
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(
            snapshotId,
            dependencyProperties:
            [
                new StoredDependencyPropertySnapshot(
                    "Button_1",
                    "Width",
                    HadLocalValue: true,
                    LocalValue: "120",
                    CurrentValue: "120",
                    BaseValueSource: "LocalValue")
            ]));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("restoredDependencyPropertyCount").GetInt32().Should().Be(1);
        var verification = result.GetProperty("restoredDependencyProperties")[0];
        verification.GetProperty("propertyName").GetString().Should().Be("Width");
        verification.GetProperty("verified").GetBoolean().Should().BeFalse();
        verification.GetProperty("expectedValue").GetString().Should().Be("120");
        verification.GetProperty("currentValue").GetString().Should().Be("240");
        result.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(warning => warning!.Contains("Width", StringComparison.Ordinal));
        result.GetProperty("nextSteps").EnumerateArray()
            .Select(item => item.GetProperty("tool").GetString())
            .Should().Contain(["get_binding_value_chain", "capture_state_snapshot"]);
        connected.RequestMethods.Should().Equal("set_dp_value", "get_dp_value_source");
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldExposeDependencyPropertyBaseValueSourceMismatch()
    {
        var processId = NextSyntheticProcessId();
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "set_dp_value" => new
                {
                    success = true,
                    propertyName = "Width",
                    oldValue = "240",
                    newValue = "120"
                },
                "get_dp_value_source" => new
                {
                    success = true,
                    propertyName = "Width",
                    currentValue = "120",
                    hadLocalValue = false,
                    baseValueSource = "Default",
                    isExpression = false
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        const string snapshotId = "snapshot_dp_base_value_source_mismatch";
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(
            snapshotId,
            dependencyProperties:
            [
                new StoredDependencyPropertySnapshot(
                    "Button_1",
                    "Width",
                    HadLocalValue: true,
                    LocalValue: "120",
                    CurrentValue: "120",
                    BaseValueSource: "LocalValue")
            ]));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        var verification = result.GetProperty("restoredDependencyProperties")[0];
        verification.GetProperty("verified").GetBoolean().Should().BeFalse();
        verification.GetProperty("expectedBaseValueSource").GetString().Should().Be("LocalValue");
        verification.GetProperty("currentBaseValueSource").GetString().Should().Be("Default");
        result.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(warning => warning!.Contains("Width", StringComparison.Ordinal));
        connected.RequestMethods.Should().Equal("set_dp_value", "get_dp_value_source");
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldExposeViewModelReadBackMismatch()
    {
        var processId = NextSyntheticProcessId();
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "modify_viewmodel" => new
                {
                    success = true,
                    propertyName = "Name",
                    oldValue = "Bob",
                    newValue = "Alice"
                },
                "get_viewmodel" => new
                {
                    success = true,
                    typeName = "SampleViewModel",
                    properties = new[]
                    {
                        new { name = "Name", type = "String", value = "Bob", canWrite = true }
                    }
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        const string snapshotId = "snapshot_vm_mismatch";
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(
            snapshotId,
            viewModelProperties:
            [
                new StoredViewModelPropertySnapshot(
                    "TextBox_1",
                    "Name",
                    "String",
                    "Alice",
                    CanRestore: true,
                    SkipReason: null)
            ]));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("restoredViewModelPropertyCount").GetInt32().Should().Be(1);
        var verification = result.GetProperty("restoredViewModelProperties")[0];
        verification.GetProperty("propertyName").GetString().Should().Be("Name");
        verification.GetProperty("verified").GetBoolean().Should().BeFalse();
        verification.GetProperty("expectedValue").GetString().Should().Be("Alice");
        verification.GetProperty("currentValue").GetString().Should().Be("Bob");
        result.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(warning => warning!.Contains("Name", StringComparison.Ordinal));
        connected.RequestMethods.Should().Equal("modify_viewmodel", "get_viewmodel");
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldRestoreViewModelBeforeVerifyingBoundDependencyProperties()
    {
        var processId = NextSyntheticProcessId();
        var viewModelName = "Bob";
        var textBoxText = "Bob";
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request =>
            {
                switch (request.Method)
                {
                    case "modify_viewmodel":
                        viewModelName = request.Params!.Value.GetProperty("value").GetString()!;
                        textBoxText = viewModelName;
                        return new
                        {
                            success = true,
                            propertyName = "Name",
                            oldValue = "Bob",
                            newValue = viewModelName
                        };
                    case "get_viewmodel":
                        return new
                        {
                            success = true,
                            typeName = "SampleViewModel",
                            properties = new[]
                            {
                                new { name = "Name", type = "String", value = viewModelName, canWrite = true }
                            }
                        };
                    case "restore_dp_expression":
                        return new
                        {
                            success = true,
                            propertyName = "Text",
                            restoredExpression = true
                        };
                    case "get_dp_value_source":
                        return new
                        {
                            success = true,
                            propertyName = "Text",
                            currentValue = textBoxText,
                            baseValueSource = "LocalValue",
                            isExpression = true
                        };
                    default:
                        return new { success = false, error = $"Unexpected method '{request.Method}'." };
                }
            });

        const string snapshotId = "snapshot_bound_dp_with_viewmodel";
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(
            snapshotId,
            dependencyProperties:
            [
                new StoredDependencyPropertySnapshot(
                    "TextBox_1",
                    "Text",
                    HadLocalValue: false,
                    LocalValue: null,
                    CurrentValue: "Alice",
                    BaseValueSource: "LocalValue",
                    IsExpression: true,
                    ExpressionRestoreToken: "binding-token",
                    ExpressionKind: "Binding")
            ],
            viewModelProperties:
            [
                new StoredViewModelPropertySnapshot(
                    "TextBox_1",
                    "Name",
                    "String",
                    "Alice",
                    CanRestore: true,
                    SkipReason: null)
            ]));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("warnings").GetArrayLength().Should().Be(0);
        connected.RequestMethods.Should().Equal(
            "modify_viewmodel",
            "get_viewmodel",
            "restore_dp_expression",
            "get_dp_value_source");
    }

    [Fact]
    public async Task RestoreStateSnapshot_ShouldPassCapturedValueWhenRestoringBindingExpression()
    {
        var processId = NextSyntheticProcessId();
        var boundSourceValue = "CodexE2E-DP";
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request =>
            {
                switch (request.Method)
                {
                    case "restore_dp_expression":
                        if (request.Params!.Value.TryGetProperty("targetValue", out var targetValue))
                        {
                            boundSourceValue = targetValue.GetString()!;
                        }

                        return new
                        {
                            success = true,
                            propertyName = "Text",
                            restoredExpression = true,
                            currentValue = boundSourceValue
                        };
                    case "get_dp_value_source":
                        return new
                        {
                            success = true,
                            propertyName = "Text",
                            currentValue = boundSourceValue,
                            baseValueSource = "TemplateBinding",
                            isExpression = true
                        };
                    default:
                        return new { success = false, error = $"Unexpected method '{request.Method}'." };
                }
            });

        const string snapshotId = "snapshot_bound_dp_source_polluted";
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(
            snapshotId,
            dependencyProperties:
            [
                new StoredDependencyPropertySnapshot(
                    "TextBox_1",
                    "Text",
                    HadLocalValue: false,
                    LocalValue: null,
                    CurrentValue: "",
                    BaseValueSource: "TemplateBinding",
                    IsExpression: true,
                    ExpressionRestoreToken: "binding-token",
                    ExpressionKind: "Binding")
            ]));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("warnings").GetArrayLength().Should().Be(0);
        connected.RequestMethods.Should().Equal("restore_dp_expression", "get_dp_value_source");
    }

    private static StoredStateSnapshot CreateSnapshot(
        string snapshotId,
        IReadOnlyList<StoredDependencyPropertySnapshot>? dependencyProperties = null,
        IReadOnlyList<StoredViewModelPropertySnapshot>? viewModelProperties = null) =>
        new(
            snapshotId,
            SnapshotName: null,
            ElementId: null,
            dependencyProperties ?? [],
            viewModelProperties ?? [],
            Focus: null,
            BindingErrors: [],
            HasBindingErrorBaseline: true,
            ValidationErrors: [],
            HasValidationBaseline: true,
            DateTimeOffset.UtcNow);

    private static async Task<ConnectedStateSession> CreateConnectedSessionAsync(
        int processId,
        Func<InspectorRequest, object> responder)
    {
        var pipeName = CreateUniquePipeName();
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
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        });

        var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplaceSessionManagerPipeClient(sessionManager, processId, client);

        return new ConnectedStateSession(sessionManager, server, serverTask, requestMethods);
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
