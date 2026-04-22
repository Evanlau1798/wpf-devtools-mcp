using Xunit;
using FluentAssertions;
using System.Text.Json;
using System.IO.Pipes;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public partial class ConnectToolTests
{
    [Fact]
    public async Task Execute_AlreadyConnected_ShouldReturnSuccessImmediately()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var acceptTask = Task.Run(async () => await server.WaitForConnectionAsync());

        using var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        using var pipeClient = new NamedPipeClient(processId, pipeName);

        var connected = await pipeClient.ConnectAsync(
            TimeSpan.FromSeconds(5),
            maxRetries: 3,
            cancellationToken: CancellationToken.None);
        connected.Should().BeTrue();
        await acceptTask;

        ReplacePipeClient(sessionManager, processId, pipeClient);

        var tool = CreateTool(sessionManager: sessionManager);
        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("message").GetString().Should().Contain("Already connected");
        sessionManager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(processId);
    }


    [Fact]
    public async Task Execute_AlreadyConnected_ShouldBypassRateLimit()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var acceptTask = Task.Run(async () => await server.WaitForConnectionAsync());

        using var sessionManager = new SessionManager(maxRequestsPerMinute: 1);
        sessionManager.AddSession(processId);
        using var pipeClient = new NamedPipeClient(processId, pipeName);

        var connected = await pipeClient.ConnectAsync(
            TimeSpan.FromSeconds(5),
            maxRetries: 3,
            cancellationToken: CancellationToken.None);
        connected.Should().BeTrue();
        await acceptTask;

        ReplacePipeClient(sessionManager, processId, pipeClient);

        var tool = CreateTool(sessionManager: sessionManager);

        var firstResult = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(
            await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None)));
        var secondResult = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(
            await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None)));

        firstResult.GetProperty("success").GetBoolean().Should().BeTrue();
        firstResult.GetProperty("message").GetString().Should().Contain("Already connected");
        secondResult.GetProperty("success").GetBoolean().Should().BeTrue();
        secondResult.GetProperty("message").GetString().Should().Contain("Already connected");
    }

    [Fact]
    public async Task Execute_BootstrapInjectionFailure_ShouldPropagateError()
    {
        EnsureDummyBootstrapperExists();
        var injector = new FakeProcessInjector
        {
            ValidationResult = InjectionError.None,
            ShouldFailInjection = true,
            InjectionErrorMessage = "CLR hosting initialization failed",
            FailedStage = BootstrapStage.ClrHosting,
            FailedExitCode = 0x11
        };
        var tool = CreateTool(injector: injector);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("CLR hosting");
        resultJson.GetProperty("stage").GetString().Should().Be("ClrHosting");
    }

    [Fact]
    public async Task Execute_BootstrapPipeTimeout_ShouldReturnPipeError()
    {
        EnsureDummyBootstrapperExists();
        var injector = new FakeProcessInjector
        {
            ValidationResult = InjectionError.None,
            ShouldFailInjection = true,
            InjectionErrorMessage = "Named Pipe did not become ready",
            FailedStage = BootstrapStage.PipeReady,
            FailedExitCode = 0,
            FailedError = InjectionError.PipeReadyTimeout
        };
        var tool = CreateTool(injector: injector);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Pipe");
    }

    [Fact]
    public async Task Execute_WhenPipeConnectionCancelled_ShouldCleanupSession()
    {
        EnsureDummyBootstrapperExists();
        using var sessionManager = new SessionManager();
        var tool = CreateTool(sessionManager: sessionManager);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        sessionManager.HasSession(12345).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_WhenPipeConnectionFails_ShouldCleanupSession()
    {
        var processId = Random.Shared.Next(700_000, 999_999);
        using var sessionManager = new SessionManager();
        var tool = CreateTool(sessionManager: sessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        sessionManager.HasSession(processId).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_WithDisconnectedStaleSession_ShouldRemoveItAndAttemptFreshInjection()
    {
        EnsureDummyBootstrapperExists();
        using var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);

        var injector = new FakeProcessInjector
        {
            ShouldFailInjection = true,
            InjectionErrorMessage = "Fresh injection attempted"
        };

        var tool = CreateTool(sessionManager: sessionManager, injector: injector);
        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Fresh injection attempted");
        sessionManager.HasSession(12345).Should().BeFalse();
        injector.InjectWithBootstrapCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Execute_RateLimitExceeded_ShouldReturnError()
    {
        using var sessionManager = new SessionManager(maxRequestsPerMinute: 2);
        var tool = CreateTool(
            sessionManager: sessionManager,
            injector: new FakeProcessInjector { ValidationResult = InjectionError.NotWpfApplication });

        await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
        await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Rate limit");
    }

    [Fact]
    public async Task Execute_WhenInjectedSessionHitsSessionLimit_ShouldReturnStructuredSessionLimitError()
    {
        EnsureDummyBootstrapperExists();
        using var sessionManager = new SessionManager();
        for (var index = 0; index < McpServerConfiguration.MaxSessions; index++)
        {
            sessionManager.AddSession(NextSyntheticProcessId());
        }

        var tool = CreateTool(sessionManager: sessionManager, injector: new FakeProcessInjector());

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId = NextSyntheticProcessId() }),
            CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("SessionLimitExceeded");
    }

    [Fact]
    public async Task Execute_WhenExistingHostAttachHitsSessionLimit_ShouldReturnStructuredSessionLimitError()
    {
        var processId = Environment.ProcessId;
        using var sessionManager = new SessionManager();
        for (var index = 0; index < McpServerConfiguration.MaxSessions; index++)
        {
            sessionManager.AddSession(NextSyntheticProcessId());
        }

        using var host = new WpfDevTools.Inspector.Host.InspectorHost(processId);
        host.Start();

        var tool = new ConnectTool(
            sessionManager,
            new FakeProcessInjector(),
            new FakeProcessDetector(),
            _ => { },
            () => false,
            pipeReadyProbe: new PipeReadyProbe((_, _) => true, () => DateTime.UtcNow, _ => { }),
            isRawInjectionTargetAllowed: _ => true);

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("SessionLimitExceeded");
    }

    [Fact]
    public async Task Execute_WhenSessionIsConcurrentlyCreatedAndAlreadyConnected_ShouldReturnIdempotentSuccess()
    {
        EnsureDummyBootstrapperExists();
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var acceptTask = Task.Run(async () => await server.WaitForConnectionAsync());
        var pipeClient = new NamedPipeClient(processId, pipeName);
        (await pipeClient.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1, cancellationToken: CancellationToken.None)).Should().BeTrue();
        await acceptTask;

        using var sessionManager = new SessionManager();
        var injector = new FakeProcessInjector
        {
            InjectWithBootstrapHandler = (request, _) =>
            {
                sessionManager.AddSession(processId);
                ReplacePipeClient(sessionManager, processId, pipeClient);

                return InjectionResult.CreateSuccess(
                    request.ProcessId,
                    request.InspectorDllPath,
                    bootstrapExitCode: 0,
                    pipeName: request.ExpectedPipeName);
            }
        };

        var tool = CreateTool(sessionManager: sessionManager, injector: injector);

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("message").GetString().Should().Contain("Already connected");
    }

    [Fact]
    public async Task Execute_WhenSessionIsConcurrentlyCreatedButNotConnected_ShouldReturnSessionConflict()
    {
        EnsureDummyBootstrapperExists();
        var processId = NextSyntheticProcessId();
        using var sessionManager = new SessionManager();
        var injector = new FakeProcessInjector
        {
            InjectWithBootstrapHandler = (request, _) =>
            {
                sessionManager.AddSession(processId);
                return InjectionResult.CreateSuccess(
                    request.ProcessId,
                    request.InspectorDllPath,
                    bootstrapExitCode: 0,
                    pipeName: request.ExpectedPipeName);
            }
        };

        var tool = CreateTool(sessionManager: sessionManager, injector: injector);

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));

        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("SessionConflict");
    }

    [Fact]
    public async Task Execute_WithoutProcessId_WhenTargetAlreadyConnected_ShouldKeepAutoDiscoveryMetadata()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var acceptTask = Task.Run(async () => await server.WaitForConnectionAsync());
        var pipeClient = new NamedPipeClient(processId, pipeName);
        (await pipeClient.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1, cancellationToken: CancellationToken.None)).Should().BeTrue();
        await acceptTask;

        using var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        ReplacePipeClient(sessionManager, processId, pipeClient);

        var tool = CreateTool(
            sessionManager: sessionManager,
            processDetector: new SingleAutoDiscoveryProcessDetector(processId));

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("autoDiscovered").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("candidateCount").GetInt32().Should().Be(1);
        resultJson.GetProperty("processId").GetInt32().Should().Be(processId);
    }

    [Fact]
    public async Task Execute_WithoutProcessId_WhenTargetAlreadyConnected_ShouldBypassRateLimitAndKeepAutoDiscoveryMetadata()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var acceptTask = Task.Run(async () => await server.WaitForConnectionAsync());
        var pipeClient = new NamedPipeClient(processId, pipeName);
        (await pipeClient.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1, cancellationToken: CancellationToken.None)).Should().BeTrue();
        await acceptTask;

        using var sessionManager = new SessionManager(maxRequestsPerMinute: 1);
        sessionManager.AddSession(processId);
        ReplacePipeClient(sessionManager, processId, pipeClient);

        var tool = CreateTool(
            sessionManager: sessionManager,
            processDetector: new SingleAutoDiscoveryProcessDetector(processId));

        var firstResult = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(
            await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None)));
        var secondResult = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(
            await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None)));

        firstResult.GetProperty("success").GetBoolean().Should().BeTrue();
        firstResult.GetProperty("autoDiscovered").GetBoolean().Should().BeTrue();
        secondResult.GetProperty("success").GetBoolean().Should().BeTrue();
        secondResult.GetProperty("autoDiscovered").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithoutProcessId_WhenDuplicateConnectedSessionCollapsesToSuccess_ShouldKeepAutoDiscoveryMetadata()
    {
        EnsureDummyBootstrapperExists();
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var acceptTask = Task.Run(async () => await server.WaitForConnectionAsync());
        var pipeClient = new NamedPipeClient(processId, pipeName);
        (await pipeClient.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1, cancellationToken: CancellationToken.None)).Should().BeTrue();
        await acceptTask;

        using var sessionManager = new SessionManager();
        var injector = new FakeProcessInjector
        {
            InjectWithBootstrapHandler = (request, _) =>
            {
                sessionManager.AddSession(processId);
                ReplacePipeClient(sessionManager, processId, pipeClient);

                return InjectionResult.CreateSuccess(
                    request.ProcessId,
                    request.InspectorDllPath,
                    bootstrapExitCode: 0,
                    pipeName: request.ExpectedPipeName);
            }
        };

        var tool = CreateTool(
            sessionManager: sessionManager,
            injector: injector,
            processDetector: new SingleAutoDiscoveryProcessDetector(processId));

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("autoDiscovered").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("candidateCount").GetInt32().Should().Be(1);
        resultJson.GetProperty("processId").GetInt32().Should().Be(processId);
    }

    [Fact]
    public async Task Execute_WhenSessionManagerDisposesBeforeInjectedConnect_ShouldReturnServerShuttingDown()
    {
        EnsureDummyBootstrapperExists();
        var processId = NextSyntheticProcessId();
        using var sessionManager = new SessionManager();
        var injector = new FakeProcessInjector
        {
            InjectWithBootstrapHandler = (request, _) =>
            {
                sessionManager.Dispose();
                return InjectionResult.CreateSuccess(
                    request.ProcessId,
                    request.InspectorDllPath,
                    bootstrapExitCode: 0,
                    pipeName: request.ExpectedPipeName);
            }
        };

        var tool = CreateTool(sessionManager: sessionManager, injector: injector);

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("ServerShuttingDown");
    }

    [Fact]
    public async Task Execute_WhenSessionManagerDisposesAfterTransportPreparationButBeforeInjection_ShouldReturnServerShuttingDownWithoutInjecting()
    {
        EnsureDummyBootstrapperExists();
        var processId = NextSyntheticProcessId();
        using var sessionManager = new SessionManager();
        var injector = new FakeProcessInjector();
        var validatorCallCount = 0;

        var tool = CreateTool(
            sessionManager: sessionManager,
            injector: injector,
            dllPathValidator: _ =>
            {
                validatorCallCount++;
                if (validatorCallCount == 1)
                {
                    sessionManager.Dispose();
                }
            });

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("ServerShuttingDown");
        injector.InjectWithBootstrapCallCount.Should().Be(0);
        validatorCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Execute_WhenSessionManagerIsAlreadyDisposed_ShouldReturnServerShuttingDown()
    {
        var processId = NextSyntheticProcessId();
        var sessionManager = new SessionManager();
        sessionManager.Dispose();
        var tool = CreateTool(sessionManager: sessionManager, injector: new FakeProcessInjector());

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("ServerShuttingDown");
    }

    [Fact]
    public async Task Execute_WhenSessionManagerDisposesDuringSecureTransportPreparation_ShouldReturnServerShuttingDown()
    {
        var processId = NextSyntheticProcessId();
        using var sessionManager = new SessionManager();
        var detector = new DisposeOnGetProcessInfoDetector(sessionManager, processId);
        var tool = CreateTool(sessionManager: sessionManager, processDetector: detector, injector: new FakeProcessInjector());

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("ServerShuttingDown");
    }

    [Fact]
    public async Task Execute_ShouldForwardCancellationTokenToBootstrapInjector()
    {
        EnsureDummyBootstrapperExists();

        var injector = new FakeProcessInjector
        {
            ShouldFailInjection = true,
            InjectionErrorMessage = "Stop after injector call"
        };
        var tool = CreateTool(injector: injector);
        using var cts = new CancellationTokenSource();

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), cts.Token);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        injector.InjectWithBootstrapCallCount.Should().Be(1);
        injector.LastInjectWithBootstrapCancellationToken.CanBeCanceled.Should().BeTrue(
            "single-flight connect should still give the injector a cancelable token even when the shared operation owns the actual cancellation source");
        injector.LastInjectWithBootstrapCancellationToken.Should().NotBe(CancellationToken.None);
    }

    private sealed class DisposeOnGetProcessInfoDetector(SessionManager sessionManager, int expectedProcessId) : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId)
        {
            processId.Should().Be(expectedProcessId);
            sessionManager.Dispose();
            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "DisposedDuringConnect",
                Architecture = ProcessArchitecture.X64,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true,
                IsElevated = false
            };
        }
    }

    private sealed class SingleAutoDiscoveryProcessDetector(int processId) : WpfProcessDetector
    {
        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
            => [CreateProcessInfo(processId)];

        public override WpfProcessInfo? GetProcessInfo(int requestedProcessId)
            => requestedProcessId == processId ? CreateProcessInfo(processId) : null;

        private static WpfProcessInfo CreateProcessInfo(int processId)
        {
            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "AutoConnectedApp",
                WindowTitle = "AutoConnectedApp Window",
                Architecture = ProcessArchitecture.X64,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true,
                IsElevated = false
            };
        }
    }
}
