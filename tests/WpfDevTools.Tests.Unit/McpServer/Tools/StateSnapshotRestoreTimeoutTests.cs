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
public sealed class StateSnapshotRestoreTimeoutTests
{
    [Fact]
    public async Task RestoreStateSnapshot_WhenCancellationInterruptsFocusRestore_ShouldReturnPartialResultAndKeepSnapshot()
    {
        var processId = NextSyntheticProcessId();
        const string snapshotId = "snapshot_restore_timeout";
        using var focusRequestObserved = new ManualResetEventSlim(false);
        using var releaseFocusResponse = new ManualResetEventSlim(false);
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
                    hadLocalValue = true,
                    localValue = "120",
                    baseValueSource = "LocalValue",
                    isExpression = false
                },
                "focus_element" => BlockFocusRestore(focusRequestObserved, releaseFocusResponse),
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(snapshotId));
        connected.SessionManager.SetActiveSnapshotId(processId, snapshotId);

        using var cancellation = new CancellationTokenSource();
        var restoreTask = new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), cancellation.Token);

        focusRequestObserved.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        cancellation.Cancel();

        try
        {
            var result = JsonSerializer.SerializeToElement(
                await restoreTask.WaitAsync(TimeSpan.FromSeconds(5)));

            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("errorCode").GetString().Should().Be("Timeout");
            result.GetProperty("restoreIncomplete").GetBoolean().Should().BeTrue();
            result.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
            result.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
            result.GetProperty("processId").GetInt32().Should().Be(processId);
            result.GetProperty("snapshotId").GetString().Should().Be(snapshotId);
            result.GetProperty("restoredDependencyPropertyCount").GetInt32().Should().Be(1);
            result.GetProperty("restoredDependencyProperties")[0].GetProperty("verified").GetBoolean().Should().BeTrue();
            result.GetProperty("restoredFocus").GetBoolean().Should().BeFalse();
            result.GetProperty("warnings").EnumerateArray()
                .Select(item => item.GetString())
                .Should().Contain(warning => warning!.Contains("interrupted", StringComparison.OrdinalIgnoreCase));

            connected.SessionManager.TryGetStateSnapshot(processId, snapshotId, out _).Should().BeTrue();
            connected.SessionManager.TryGetNavigationState(processId, out var navigationState).Should().BeTrue();
            navigationState!.ActiveSnapshotId.Should().Be(snapshotId);
        }
        finally
        {
            releaseFocusResponse.Set();
        }
    }

    private static object BlockFocusRestore(
        ManualResetEventSlim focusRequestObserved,
        ManualResetEventSlim releaseFocusResponse)
    {
        focusRequestObserved.Set();
        releaseFocusResponse.Wait(TimeSpan.FromSeconds(10));
        return new { success = true, focused = true };
    }

    private static StoredStateSnapshot CreateSnapshot(string snapshotId) =>
        new(
            snapshotId,
            SnapshotName: null,
            ElementId: null,
            DependencyProperties:
            [
                new StoredDependencyPropertySnapshot(
                    "Button_1",
                    "Width",
                    HadLocalValue: true,
                    LocalValue: "120",
                    CurrentValue: "120",
                    BaseValueSource: "LocalValue")
            ],
            ViewModelProperties: [],
            Focus: new StoredFocusSnapshot("Logical", "TextBox_1"),
            BindingErrors: [],
            HasBindingErrorBaseline: true,
            ValidationErrors: [],
            HasValidationBaseline: true,
            CapturedAtUtc: DateTimeOffset.UtcNow);

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
                    var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
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
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or EndOfStreamException)
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
