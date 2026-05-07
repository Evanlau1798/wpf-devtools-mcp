using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class ConnectToolErrorCodeTests : IDisposable
{
    private string? _dummyBootstrapperPath;

    [Theory]
    [InlineData(InjectionError.Timeout)]
    [InlineData(InjectionError.PipeReadyTimeout)]
    public async Task Execute_WhenBootstrapPhaseTimesOut_ShouldReturnStableTimeoutErrorCode(InjectionError injectionError)
    {
        EnsureDummyBootstrapperExists();
        var tool = new ConnectTool(
            new SessionManager(),
            new FailingProcessInjector(injectionError),
            new FakeProcessDetector(),
            _ => { },
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("Timeout");
    }

    [Theory]
    [InlineData(
        InjectionError.Timeout,
        BootstrapStage.LoadLibrary,
        "Injection timed out before the bootstrap phase could start.")]
    [InlineData(
        InjectionError.Timeout,
        BootstrapStage.ManagedEntrypoint,
        "Injection timed out before the bootstrap phase could start.")]
    [InlineData(
        InjectionError.PipeReadyTimeout,
        BootstrapStage.PipeReady,
        "Bootstrap completed, but the remaining connect budget was exhausted before the Inspector Named Pipe readiness check could start.")]
    public async Task Execute_WhenSharedBudgetIsExhaustedBeforePhaseStarts_ShouldReturnSanitizedTimeoutMessage(
        InjectionError injectionError,
        BootstrapStage failedStage,
        string expectedMessage)
    {
        EnsureDummyBootstrapperExists();
        var tool = new ConnectTool(
            new SessionManager(),
            new FailingProcessInjector(
                injectionError,
                expectedMessage,
                failedStage,
                InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart),
            new FakeProcessDetector(),
            _ => { },
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("Timeout");
        json.GetProperty("error").GetString().Should().Be(expectedMessage);
    }

    [Fact]
    public async Task Execute_WhenNativeBootstrapMechanismFails_ShouldReturnGenericSanitizedFailureDetail()
    {
        EnsureDummyBootstrapperExists();
        const string sensitiveMechanismDetail = "LoadLibraryW failed to load the native bootstrapper DLL into the target process.";
        var tool = new ConnectTool(
            new SessionManager(),
            new FailingProcessInjector(
                InjectionError.BootstrapFailed,
                sensitiveMechanismDetail,
                BootstrapStage.LoadLibrary,
                timeoutReason: null,
                bootstrapExitCode: InjectionMechanismFailure.LoadBootstrapperFailed),
            new FakeProcessDetector(),
            _ => { },
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
        json.GetProperty("stage").GetString().Should().Be("LoadLibrary");
        json.GetProperty("error").GetString().Should().Be("Bootstrap failed while starting the injected inspector.");
        json.GetProperty("error").GetString().Should().NotContain("LoadLibraryW");
    }

    [Fact]
    public void DescribePipeConnectFailure_WhenAuthenticationFails_ShouldReturnSecurityError()
    {
        var result = ConnectTool.DescribePipeConnectFailure(
            NamedPipeConnectFailure.AuthenticationFailed,
            processId: 12345);

        result.ErrorCode.Should().Be("SecurityError");
        result.Error.Should().Contain("Authentication failed");
        result.Hint.Should().Contain("shared secret");
    }

    [Fact]
    public void DescribePipeConnectFailure_WhenSecureTransportFails_ShouldReturnSecurityError()
    {
        var result = ConnectTool.DescribePipeConnectFailure(
            NamedPipeConnectFailure.SecureTransportFailed,
            processId: 12345);

        result.ErrorCode.Should().Be("SecurityError");
        result.Error.Should().Contain("Secure transport");
        result.Hint.Should().Contain("certificate");
    }

    [Fact]
    public void DescribePipeConnectFailure_WhenServerProcessDoesNotMatch_ShouldReturnSecurityError()
    {
        var result = ConnectTool.DescribePipeConnectFailure(
            NamedPipeConnectFailure.ServerProcessMismatch,
            processId: 12345);

        result.ErrorCode.Should().Be("SecurityError");
        result.Error.Should().Contain("not hosted by the requested target process");
        result.Hint.Should().Contain("different local process");
    }

    [Fact]
    public void DescribePipeConnectFailure_WhenExistingHostIsIncompatible_ShouldReturnCompatibilityError()
    {
        var result = ConnectTool.DescribePipeConnectFailure(
            NamedPipeConnectFailure.IncompatibleHost,
            processId: 12345);

        result.ErrorCode.Should().Be("CompatibilityError");
        result.Error.Should().Contain("incompatible");
        result.Hint.Should().Contain("current protocol and build");
    }

    public void Dispose()
    {
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = EnsureSharedDummyBootstrapperExists();
    }

    private static PipeReadyProbe CreateNoExistingHostPipeReadyProbe()
        => new((_, _) => false, () => DateTime.UtcNow, _ => { });

    private sealed class FakeProcessDetector : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId) => new()
        {
            ProcessId = processId,
            ProcessName = "TestApp",
            Architecture = ProcessArchitecture.X64,
            Runtime = TargetRuntime.NetCore,
            IsWpfApplication = true
        };
    }

    private sealed class FailingProcessInjector(
        InjectionError injectionError,
        string errorMessage = "Bootstrap timed out",
        BootstrapStage? failedStage = null,
        InjectionTimeoutReason? timeoutReason = null,
        int? bootstrapExitCode = null) : IProcessInjector
    {
        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => InjectionResult.CreateSuccess(processId, dllPath);

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult InjectWithBootstrap(InjectionRequest request, CancellationToken cancellationToken = default)
            => InjectionResult.CreateFailure(
                request.ProcessId,
                injectionError,
                errorMessage,
                failedAtStage: failedStage,
                bootstrapExitCode: bootstrapExitCode,
                timeoutReason: timeoutReason);
    }
}
