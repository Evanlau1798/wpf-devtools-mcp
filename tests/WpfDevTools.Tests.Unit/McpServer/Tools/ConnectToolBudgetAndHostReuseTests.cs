using Xunit;
using FluentAssertions;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Pipes;
using System.Reflection;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Security;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public partial class ConnectToolTests
{
    [Fact]
    public async Task Execute_WhenNoExistingHost_ShouldFallbackToInjectionWithoutConsumingConnectBudget()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var injector = new BootstrapStartsPipeInjector();
        var tool = CreateTool(injector: injector);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), cts.Token);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("message").GetString().Should().Contain("get_ui_summary");
        resultJson.GetProperty("message").GetString().Should().Contain("get_element_snapshot");
        resultJson.GetProperty("message").GetString().Should().Contain("get_form_summary");
        injector.InjectWithBootstrapCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Execute_WhenPreInjectionProbeConsumesTime_ShouldPassRemainingBudgetIntoInjectionRequest()
    {
        EnsureDummyBootstrapperExists();

        var injector = new FakeProcessInjector
        {
            ShouldFailInjection = true,
            FailedError = InjectionError.BootstrapFailed,
            InjectionErrorMessage = "stop after inspecting timeout budget"
        };
        var probe = new PipeReadyProbe(
            (_, _) => false,
            () => DateTime.UtcNow,
            Thread.Sleep);
        var tool = CreateTool(
            injector: injector,
            pipeReadyProbe: probe);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        injector.LastInjectionRequest.Should().NotBeNull();
        injector.LastInjectionRequest!.TotalTimeout.Should().NotBeNull(
            "connect should propagate a single remaining deadline into the bootstrap injection request");
        injector.LastInjectionRequest.TotalTimeout!.Value.Should().BeLessThan(
            TimeSpan.FromSeconds(McpServerConfiguration.ConnectTimeoutSeconds),
            "the pre-injection probe already consumed part of the overall connect budget before injection started");
        injector.LastInjectionRequest.TotalTimeout.Value.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Execute_WhenInjectionConsumesTime_ShouldPassRemainingBudgetIntoFinalAttachPhase()
    {
        EnsureDummyBootstrapperExists();

        var injector = new FakeProcessInjector
        {
            InjectWithBootstrapHandler = (request, _) =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(250));
                return InjectionResult.CreateSuccess(
                    request.ProcessId,
                    request.InspectorDllPath,
                    bootstrapExitCode: 0,
                    pipeName: request.ExpectedPipeName);
            }
        };

        TimeSpan? observedAttachTimeout = null;
        var tool = CreateTool(
            injector: injector,
            connectInjectedSessionAsync: (_, timeout, _) =>
            {
                observedAttachTimeout = timeout;
                return Task.FromResult(NamedPipeConnectFailure.None);
            });

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        observedAttachTimeout.Should().NotBeNull(
            "connect should forward the remaining deadline into the final attach phase after bootstrap injection succeeds");
        observedAttachTimeout!.Value.Should().BeLessThan(
            TimeSpan.FromSeconds(McpServerConfiguration.ConnectTimeoutSeconds),
            "the final attach phase must consume from the same overall connect budget instead of starting with a fresh timeout");
        observedAttachTimeout.Value.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Execute_WithUnsupportedPackagingAndExistingSecureHost_ShouldReuseExistingHostWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var pipeName = CreateUniquePipeName($"WpfDevTools_{processId}");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-reuse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);
        var sharedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        using var hostAuthManager = new AuthenticationManager(() => sharedSecret);
        using var clientAuthManager = new AuthenticationManager(() => sharedSecret);
        var hostCertificateManager = new CertificateManager(certDirectory);
        var clientCertificateManager = new CertificateManager(certDirectory);
        using var host = new InspectorHost(processId, pipeName, hostAuthManager, hostCertificateManager);
        host.Start();

        try
        {
            using var sessionManager = new SessionManager(
                authManager: clientAuthManager,
                certManager: clientCertificateManager);
            var injector = new FakeProcessInjector();
            var executablePath = CreateSdkOnlyExecutablePath();
            var tool = CreateTool(
                sessionManager: sessionManager,
                injector: injector,
                processDetector: new FakeProcessDetector(executablePath: executablePath),
                pipeReadyProbe: CreateExactPipeReadyProbe(pipeName));

            try
            {
                var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

                var resultPayload = JsonSerializer.Serialize(result);
                var resultJson = JsonSerializer.Deserialize<JsonElement>(resultPayload);
                resultJson.GetProperty("success").GetBoolean().Should().BeTrue(resultPayload);
                resultJson.GetProperty("reusedExistingHost").GetBoolean().Should().BeTrue();
                injector.InjectWithBootstrapCallCount.Should().Be(0);
            }
            finally
            {
                DeleteSdkOnlyExecutablePath(executablePath);
            }
        }
        finally
        {
            host.Stop();
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_WithUnsupportedPackagingAndDelayedSecureHost_ShouldReuseExistingHostWithinConnectBudget()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var pipeName = CreateUniquePipeName($"WpfDevTools_{processId}");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-delayed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);
        var sharedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        using var hostAuthManager = new AuthenticationManager(() => sharedSecret);
        using var clientAuthManager = new AuthenticationManager(() => sharedSecret);
        var hostCertificateManager = new CertificateManager(certDirectory);
        var clientCertificateManager = new CertificateManager(certDirectory);
        using var host = new InspectorHost(processId, pipeName, hostAuthManager, hostCertificateManager);

        try
        {
            using var sessionManager = new SessionManager(
                authManager: clientAuthManager,
                certManager: clientCertificateManager);
            var injector = new FakeProcessInjector();
            var executablePath = CreateSdkOnlyExecutablePath();
            var tool = CreateTool(
                sessionManager: sessionManager,
                injector: injector,
                processDetector: new FakeProcessDetector(executablePath: executablePath),
                pipeReadyProbe: CreateExactPipeReadyProbe(pipeName));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                var startTask = Task.Run(async () =>
                {
                    await Task.Delay(600, cts.Token);
                    host.Start();
                }, cts.Token);

                var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), cts.Token);
                await startTask;

                var resultPayload = JsonSerializer.Serialize(result);
                var resultJson = JsonSerializer.Deserialize<JsonElement>(resultPayload);
                resultJson.GetProperty("success").GetBoolean().Should().BeTrue(resultPayload);
                resultJson.GetProperty("reusedExistingHost").GetBoolean().Should().BeTrue();
                injector.InjectWithBootstrapCallCount.Should().Be(0);
            }
            finally
            {
                DeleteSdkOnlyExecutablePath(executablePath);
            }
        }
        finally
        {
            host.Stop();
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_WhenIncompatibleExistingHostButRawInjectionAllowed_ShouldInjectFreshHost()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var pipeName = CreateUniquePipeName($"WpfDevTools_{processId}");
        using var incompatibleHost = CreateIncompatibleExistingHost(processId, pipeName);
        var injector = new FakeProcessInjector();
        var tool = CreateTool(
            injector: injector,
            pipeReadyProbe: CreateExactPipeReadyProbe(pipeName),
            connectInjectedSessionAsync: (_, _, _) => Task.FromResult(NamedPipeConnectFailure.None));

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        var payload = JsonSerializer.Serialize(result);
        var resultJson = JsonSerializer.Deserialize<JsonElement>(payload);
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue(payload);
        resultJson.TryGetProperty("reusedExistingHost", out _).Should().BeFalse();
        injector.InjectWithBootstrapCallCount.Should().Be(1);
    }

    private static NamedPipeServerStream CreateIncompatibleExistingHost(int processId, string pipeName)
    {
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        _ = Task.Run(async () =>
        {
            try
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
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        return server;
    }
}
