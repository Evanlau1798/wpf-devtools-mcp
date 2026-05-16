using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("TimingSensitive")]
public sealed class NamedPipeClientConnectionProtocolTests
{
    [Fact]
    public void BuildFingerprint_ShouldBeSharedAcrossAssemblies()
    {
        var serverFingerprint = InspectorCompatibilityContract.GetBuildFingerprint(typeof(NamedPipeClient));
        var inspectorFingerprint = InspectorCompatibilityContract.GetBuildFingerprint(typeof(RequestDispatcher));

        serverFingerprint.Should().NotBeNullOrWhiteSpace();
        serverFingerprint.Should().Be(inspectorFingerprint);
    }

    [Fact]
    public async Task ConnectAsync_WhenConnectedServerProcessDoesNotMatchExpectedProcess_ShouldReturnFalse()
    {
        var expectedProcessId = Environment.ProcessId + 100000;
        var pipeName = global::WpfDevTools.Tests.Unit.TestHelpers.CreateUniquePipeName();

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            try
            {
                await server.WaitForConnectionAsync();
            }
            catch (IOException)
            {
                // Client disconnects immediately after detecting the wrong server process.
            }
        });

        using var client = new NamedPipeClient(
            expectedProcessId,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: true);

        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1);

        connected.Should().BeFalse();
        client.LastConnectFailure.Should().Be(NamedPipeConnectFailure.ServerProcessMismatch);
        await serverTask;
    }

    [Fact]
    public async Task ConnectAsync_WhenConnectedHostReportsIncompatibleBuild_ShouldReturnFalse()
    {
        var processId = Environment.ProcessId;
        var pipeName = global::WpfDevTools.Tests.Unit.TestHelpers.CreateUniquePipeName();

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

            request.Should().NotBeNull();
            request!.Method.Should().Be("ping");

            var response = new InspectorResponse
            {
                Id = request.Id,
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    status = "pong",
                    processId,
                    protocolVersion = InspectorCompatibilityContract.ProtocolVersion,
                    buildFingerprint = "stale-build"
                }),
                Error = null
            };

            await MessageFraming.WriteMessageAsync(
                server,
                JsonSerializer.Serialize(response),
                CancellationToken.None);
        });

        using var client = new NamedPipeClient(
            processId,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: true);

        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1);

        connected.Should().BeFalse();
        client.LastConnectFailure.Should().Be(NamedPipeConnectFailure.IncompatibleHost);
        await serverTask;
    }

    [Fact]
    public async Task ConnectAsync_WithDefaultPipeNameAndPlaintextClient_ShouldStillValidateHostCompatibility()
    {
        var processId = Environment.ProcessId;
        var pipeName = $"WpfDevTools_{processId}";

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

            request.Should().NotBeNull();
            request!.Method.Should().Be("ping");

            var response = new InspectorResponse
            {
                Id = request.Id,
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    status = "pong",
                    processId,
                    protocolVersion = InspectorCompatibilityContract.ProtocolVersion,
                    buildFingerprint = "plaintext-stale-build"
                }),
                Error = null
            };

            await MessageFraming.WriteMessageAsync(
                server,
                JsonSerializer.Serialize(response),
                CancellationToken.None);
        });

        using var client = new NamedPipeClient(processId);

        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1);

        connected.Should().BeFalse();
        client.LastConnectFailure.Should().Be(NamedPipeConnectFailure.IncompatibleHost);
        await serverTask;
    }
}
