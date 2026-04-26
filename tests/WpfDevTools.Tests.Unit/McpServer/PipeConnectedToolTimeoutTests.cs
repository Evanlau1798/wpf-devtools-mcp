using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("TimingSensitive")]
public sealed class PipeConnectedToolTimeoutTests : IDisposable
{
    private readonly SessionManager _sessionManager = new();

    public void Dispose()
    {
        _sessionManager.Dispose();
    }

    [Fact]
    public async Task Execute_WhenInFlightPipeRequestTimesOut_ShouldReturnTimeoutRecoveryPayload()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowServerCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            requestReceived.SetResult();
            await allowServerCompletion.Task;
        });

        var client = new NamedPipeClient(
            processId,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: false,
            requestTimeout: TimeSpan.FromMilliseconds(100));
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        _sessionManager.AttachSession(processId, client);

        try
        {
            var tool = new GenericPipeTool(_sessionManager, "ping");
            var resultTask = tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

            await requestReceived.Task;
            var result = JsonSerializer.SerializeToElement(await resultTask);

            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("errorCode").GetString().Should().Be("Timeout");
            result.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
            result.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
            result.GetProperty("recovery").GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        }
        finally
        {
            allowServerCompletion.SetResult();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Execute_WhenPipeTransportResetsDuringRequest_ShouldReturnReconnectRecoveryPayload()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            requestReceived.SetResult();
            server.Dispose();
        });

        var client = new NamedPipeClient(
            processId,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: false,
            requestTimeout: TimeSpan.FromSeconds(5));
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        _sessionManager.AttachSession(processId, client);

        var tool = new GenericPipeTool(_sessionManager, "ping");
        var resultTask = tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        await requestReceived.Task;
        var result = JsonSerializer.SerializeToElement(await resultTask);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("TransportReset");
        result.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        result.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        result.GetProperty("suggestedAction").GetString().Should().Contain("Reconnect");
        result.GetProperty("recovery").GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        result.GetProperty("recovery").GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
    }
}
