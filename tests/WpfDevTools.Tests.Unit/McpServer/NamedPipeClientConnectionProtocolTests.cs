using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
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
    [NamedPipeMitmScenario(
        "wrong-server-pid",
        "A pipe owned by a different process ID must be rejected before protocol trust is established.")]
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
    [NamedPipeMitmScenario(
        "stale-build-fingerprint",
        "A host reporting a stale protocol or build fingerprint must fail compatibility validation.")]
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

    [Fact]
    [NamedPipeMitmScenario(
        "custom-pipe-name-bypass",
        "A custom pipe name must not bypass authentication and compatibility checks for existing-host reuse.")]
    public async Task ConnectExistingHostSession_WithCustomPipeAndAuthenticatedStaleFakeHost_ShouldFailClosed()
    {
        var processId = Environment.ProcessId;
        var pipeName = global::WpfDevTools.Tests.Unit.TestHelpers.CreateUniquePipeName();
        var rootSecret = new byte[32];
        RandomNumberGenerator.Fill(rootSecret);

        using var authManager = new AuthenticationManager(() => Convert.ToBase64String(rootSecret));
        using var sessionManager = new SessionManager(authManager: authManager);
        var processSecretBase64 = sessionManager.GetAuthenticationSecretBase64(processId, pipeName);
        processSecretBase64.Should().NotBeNullOrWhiteSpace();

        var processSecret = Convert.FromBase64String(processSecretBase64!);
        try
        {
            var serverTask = RunAuthenticatedFakeHostAsync(
                pipeName,
                processSecret,
                async server => await RespondWithStaleCompatibilityPingAsync(server, processId));

            var failure = await sessionManager.ConnectExistingHostSessionAsync(
                processId,
                pipeName,
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            failure.Should().Be(NamedPipeConnectFailure.IncompatibleHost);
            sessionManager.GetPipeClient(processId).Should().BeNull(
                "a fake host that only knows the HMAC secret must not become an attached MCP session");

            await serverTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootSecret);
            CryptographicOperations.ZeroMemory(processSecret);
        }
    }

    private static async Task RunAuthenticatedFakeHostAsync(
        string pipeName,
        byte[] expectedSecret,
        Func<NamedPipeServerStream, Task> afterAuthentication)
    {
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        byte[]? challenge = null;
        var response = new byte[32];
        try
        {
            await server.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));

            challenge = new byte[32];
            RandomNumberGenerator.Fill(challenge);
            await server.WriteAsync(challenge, 0, challenge.Length);
            await server.FlushAsync();

            var totalRead = 0;
            while (totalRead < response.Length)
            {
                var read = await server.ReadAsync(
                    response,
                    totalRead,
                    response.Length - totalRead).WaitAsync(TimeSpan.FromSeconds(5));
                if (read == 0)
                {
                    return;
                }

                totalRead += read;
            }

            using var calculator = new ResponseCalculator(expectedSecret);
            var authenticated = calculator.VerifyResponse(challenge, response);
            await server.WriteAsync(new[] { authenticated ? (byte)1 : (byte)0 }, 0, 1);
            await server.FlushAsync();

            if (authenticated)
            {
                await afterAuthentication(server);
            }
        }
        finally
        {
            if (challenge != null)
            {
                CryptographicOperations.ZeroMemory(challenge);
            }

            CryptographicOperations.ZeroMemory(response);
        }
    }

    private static async Task RespondWithStaleCompatibilityPingAsync(
        NamedPipeServerStream server,
        int processId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            var requestJson = await MessageFraming.ReadMessageAsync(server, timeout.Token);
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
                    buildFingerprint = "fake-stale-build"
                }),
                Error = null
            };

            await MessageFraming.WriteMessageAsync(
                server,
                JsonSerializer.Serialize(response),
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Pre-fix clients skip compatibility validation on custom pipe names.
        }
        catch (IOException)
        {
            // The client may fail closed and reset the pipe after reading the stale response.
        }
    }
}
