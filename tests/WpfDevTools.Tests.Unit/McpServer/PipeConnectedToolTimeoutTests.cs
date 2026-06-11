using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public void CreatePipeTimeoutError_ShouldSanitizeUserFacingErrorAndPreserveDiagnostics()
    {
        const int processId = 24680;
        const string rawMessage = "Timeout waiting for pipe WpfDevTools_SecretTarget after 100ms at C:\\Users\\Someone\\AppData\\Local";

        var result = JsonSerializer.SerializeToElement(
            PipeConnectedToolBase.CreatePipeTimeoutError(processId, rawMessage, requiresReconnect: true));

        result.GetProperty("error").GetString().Should().Be(
            $"Inspector pipe request timed out for process {processId}. Reconnect before retrying because the pipe session may be stale.");
        result.GetRawText().Should().NotContain("WpfDevTools_SecretTarget");
        result.GetRawText().Should().NotContain("C:\\Users\\Someone");

        var diagnostic = result.GetProperty("errorData").GetProperty("transportDiagnostic");
        diagnostic.GetProperty("failureKind").GetString().Should().Be("Timeout");
        diagnostic.GetProperty("messageFingerprint").GetString().Should().NotBeNullOrWhiteSpace();
        diagnostic.GetProperty("messageFingerprint").GetString().Should().NotBe(CreateSha256Hex(rawMessage));
        diagnostic.GetProperty("fingerprintScope").GetString().Should().Be("process");
        diagnostic.GetProperty("rawMessageRedacted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CreatePipeTransportResetError_ShouldSanitizeUserFacingErrorAndPreserveDiagnostics()
    {
        const int processId = 24681;
        const string rawMessage = "Pipe WpfDevTools_PrivatePipe broke while reading C:\\Users\\Someone\\target.log";

        var result = JsonSerializer.SerializeToElement(
            PipeConnectedToolBase.CreatePipeTransportResetError(processId, rawMessage));

        result.GetProperty("error").GetString().Should().Be(
            $"Inspector pipe transport reset for process {processId}. Reconnect before retrying because the session is stale.");
        result.GetRawText().Should().NotContain("WpfDevTools_PrivatePipe");
        result.GetRawText().Should().NotContain("C:\\Users\\Someone");

        var diagnostic = result.GetProperty("errorData").GetProperty("transportDiagnostic");
        diagnostic.GetProperty("failureKind").GetString().Should().Be("TransportReset");
        diagnostic.GetProperty("messageFingerprint").GetString().Should().NotBeNullOrWhiteSpace();
        diagnostic.GetProperty("messageFingerprint").GetString().Should().NotBe(CreateSha256Hex(rawMessage));
        diagnostic.GetProperty("fingerprintScope").GetString().Should().Be("process");
        diagnostic.GetProperty("rawMessageRedacted").GetBoolean().Should().BeTrue();
    }

    private static string CreateSha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
