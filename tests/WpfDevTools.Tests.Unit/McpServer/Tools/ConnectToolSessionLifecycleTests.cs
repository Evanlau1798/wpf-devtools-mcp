using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Enums;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public partial class ConnectToolTests
{
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
}
