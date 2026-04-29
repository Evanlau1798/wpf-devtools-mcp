using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Security;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class ConnectToolSecurityErrorTests
{
    [Fact]
    public async Task Execute_WhenSecureTransportInitializationFails_ShouldReturnSanitizedMessage()
    {
        var blockedPath = Path.GetTempFileName();
        try
        {
            using var sessionManager = new SessionManager(
                authManager: null,
                certManager: new CertificateManager(blockedPath));
            var injector = new RecordingProcessInjector();
            var tool = new ConnectTool(
                sessionManager,
                injector,
                processDetector: new FixedProcessDetector(new WpfProcessInfo
                {
                    ProcessId = 4242,
                    ProcessName = "TestApp",
                    Architecture = ProcessArchitecture.X64,
                    Runtime = TargetRuntime.NetCore,
                    IsWpfApplication = true,
                    IsElevated = false,
                    ExecutablePath = blockedPath
                }),
                dllPathValidator: _ => { },
                isCurrentProcessElevated: () => false,
                isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 4242 }), CancellationToken.None);
            var json = ToJsonElement(result);

            json.GetProperty("success").GetBoolean().Should().BeFalse();
            json.GetProperty("errorCode").GetString().Should().Be("SecureTransportInitializationFailed");
            json.GetProperty("error").GetString().Should().Be("Failed to prepare secure transport artifacts. Check server logs for details.");
            json.GetProperty("error").GetString().Should().NotContain(blockedPath);
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            File.Delete(blockedPath);
        }
    }

    [Fact]
    public async Task Execute_WhenInjectorReturnsSensitiveInternalDetail_ShouldSanitizeResponse()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var inspectorPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Inspector.net8.0-windows.dll");
        var bootstrapperPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Bootstrapper.x64.dll");
        File.WriteAllText(inspectorPath, "placeholder");
        File.WriteAllText(bootstrapperPath, "placeholder");

        try
        {
            using var sessionManager = new SessionManager();
            var injector = new RecordingProcessInjector
            {
                Failure = InjectionResult.CreateFailure(
                    4242,
                    InjectionError.Unknown,
                    "Unexpected error: secret failure details")
            };
            var tool = new ConnectTool(
                sessionManager,
                injector,
                processDetector: new FixedProcessDetector(new WpfProcessInfo
                {
                    ProcessId = 4242,
                    ProcessName = "TestApp",
                    Architecture = ProcessArchitecture.X64,
                    Runtime = TargetRuntime.NetCore,
                    IsWpfApplication = true,
                    IsElevated = false,
                    ExecutablePath = Path.Combine(tempDirectory.FullName, "TestApp.exe")
                }),
                dllPathValidator: _ => { },
                isCurrentProcessElevated: () => false,
                inspectorCandidateResolver: _ => [inspectorPath],
                bootstrapperCandidateResolver: _ => [bootstrapperPath],
                isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 4242 }), CancellationToken.None);
            var json = ToJsonElement(result);

            json.GetProperty("success").GetBoolean().Should().BeFalse();
            json.GetProperty("errorCode").GetString().Should().Be("Unknown");
            json.GetProperty("error").GetString().Should().Be("Injection failed due to an unexpected internal error. Check server logs for details.");
            json.GetProperty("error").GetString().Should().NotContain("secret failure details");
            json.TryGetProperty("exitCode", out _).Should().BeFalse();
            injector.InjectWithBootstrapCallCount.Should().Be(1);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WhenBootstrapReturnsUnknownExitCodeDetail_ShouldSanitizeResponse()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var inspectorPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Inspector.net8.0-windows.dll");
        var bootstrapperPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Bootstrapper.x64.dll");
        File.WriteAllText(inspectorPath, "placeholder");
        File.WriteAllText(bootstrapperPath, "placeholder");

        try
        {
            using var sessionManager = new SessionManager();
            var injector = new RecordingProcessInjector
            {
                Failure = InjectionResult.CreateFailure(
                    4242,
                    InjectionError.BootstrapFailed,
                    "Unknown bootstrap exit code: 0xDEAD",
                    failedAtStage: BootstrapStage.Unknown,
                    bootstrapExitCode: 0xDEAD)
            };
            var tool = CreateTool(sessionManager, injector, inspectorPath, bootstrapperPath, tempDirectory.FullName);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 4242 }), CancellationToken.None);
            var json = ToJsonElement(result);

            json.GetProperty("success").GetBoolean().Should().BeFalse();
            json.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
            json.GetProperty("error").GetString().Should().Be("Bootstrap failed while starting the injected inspector.");
            json.GetProperty("error").GetString().Should().NotContain("0xDEAD");
            json.TryGetProperty("exitCode", out _).Should().BeFalse();
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WhenPipeReadyTimeoutContainsPipeName_ShouldSanitizeResponse()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var inspectorPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Inspector.net8.0-windows.dll");
        var bootstrapperPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Bootstrapper.x64.dll");
        File.WriteAllText(inspectorPath, "placeholder");
        File.WriteAllText(bootstrapperPath, "placeholder");

        try
        {
            using var sessionManager = new SessionManager();
            var injector = new RecordingProcessInjector
            {
                Failure = InjectionResult.CreateFailure(
                    4242,
                    InjectionError.PipeReadyTimeout,
                    "Bootstrap completed (exit code 0) but Named Pipe 'WpfDevTools_4242' did not become ready within timeout.",
                    failedAtStage: BootstrapStage.PipeReady,
                    bootstrapExitCode: 0)
            };
            var tool = CreateTool(sessionManager, injector, inspectorPath, bootstrapperPath, tempDirectory.FullName);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 4242 }), CancellationToken.None);
            var json = ToJsonElement(result);

            json.GetProperty("success").GetBoolean().Should().BeFalse();
            json.GetProperty("errorCode").GetString().Should().Be("Timeout");
            json.GetProperty("error").GetString().Should().Be("Bootstrap completed, but the Inspector Named Pipe did not become ready before the timeout expired.");
            json.GetProperty("error").GetString().Should().NotContain("WpfDevTools_4242");
            json.TryGetProperty("exitCode", out _).Should().BeFalse();
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WhenMcpTargetPolicyDeniesProcess_ShouldNotReturnExecutablePath()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var executablePath = Path.Combine(tempDirectory.FullName, "PrivateApp.exe");
            File.WriteAllText(executablePath, "placeholder");
            using var sessionManager = new SessionManager();
            var injector = new RecordingProcessInjector();
            var tool = new ConnectTool(
                sessionManager,
                injector,
                processDetector: new FixedProcessDetector(new WpfProcessInfo
                {
                    ProcessId = 4242,
                    ProcessName = "PrivateApp",
                    Architecture = ProcessArchitecture.X64,
                    Runtime = TargetRuntime.NetCore,
                    IsWpfApplication = true,
                    IsElevated = false,
                    ExecutablePath = executablePath
                }),
                dllPathValidator: _ => { },
                isCurrentProcessElevated: () => false,
                workingSetResolver: null,
                inspectorCandidateResolver: null,
                bootstrapperCandidateResolver: null,
                pipeReadyProbe: null,
                isRawInjectionTargetAllowed: null,
                targetPolicy: _ => new McpTargetAuthorization(
                    IsAllowed: false,
                    Error: "Target is blocked by the MCP target allowlist.",
                    Hint: "Review the target process before allowlisting it."),
                connectInjectedSessionAsync: null,
                connectTimeout: null);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 4242 }), CancellationToken.None);
            var json = ToJsonElement(result);

            json.GetProperty("success").GetBoolean().Should().BeFalse();
            json.GetProperty("errorCode").GetString().Should().Be("SecurityError");
            json.GetRawText().Should().NotContain(executablePath.Replace("\\", "\\\\"));
            json.TryGetProperty("targetExecutablePath", out _).Should().BeFalse();
            json.GetProperty("targetProcessName").GetString().Should().Be("PrivateApp");
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WhenSharedBudgetFailureHasSensitiveDetail_ShouldSanitizeResponse()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var inspectorPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Inspector.net8.0-windows.dll");
        var bootstrapperPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Bootstrapper.x64.dll");
        File.WriteAllText(inspectorPath, "placeholder");
        File.WriteAllText(bootstrapperPath, "placeholder");

        try
        {
            using var sessionManager = new SessionManager();
            var injector = new RecordingProcessInjector
            {
                Failure = InjectionResult.CreateFailure(
                    4242,
                    InjectionError.Timeout,
                    "Timeout before phase at C:\\Users\\Someone\\secret with pipe WpfDevTools_Private",
                        failedAtStage: null,
                        bootstrapExitCode: null,
                    timeoutReason: InjectionTimeoutReason.SharedBudgetExhaustedBeforePhaseStart)
            };
            var tool = CreateTool(sessionManager, injector, inspectorPath, bootstrapperPath, tempDirectory.FullName);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 4242 }), CancellationToken.None);
            var json = ToJsonElement(result);

            json.GetProperty("success").GetBoolean().Should().BeFalse();
            json.GetProperty("errorCode").GetString().Should().Be("Timeout");
            json.GetProperty("error").GetString().Should().Be("Injection timed out before the bootstrap phase could start.");
            json.GetRawText().Should().NotContain("C:\\\\Users\\\\Someone");
            json.GetRawText().Should().NotContain("WpfDevTools_Private");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WhenDllSignatureVerificationFails_ShouldSurfaceSecurityErrorAtToolBoundary()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var inspectorPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Inspector.net8.0-windows.dll");
        var bootstrapperPath = Path.Combine(tempDirectory.FullName, "WpfDevTools.Bootstrapper.x64.dll");
        const string signatureError =
            "Inspector DLL is not digitally signed or has an invalid signature. " +
            "In development, use a DEBUG build which auto-skips signature verification for local DLLs. " +
            "In production, sign the DLL with Authenticode.";
        File.WriteAllText(inspectorPath, "placeholder");
        File.WriteAllText(bootstrapperPath, "placeholder");

        try
        {
            using var scope = ToolCallHelper.BeginTestScope();
            using var sessionManager = new SessionManager();
            var injector = new RecordingProcessInjector();
            var tool = new ConnectTool(
                sessionManager,
                injector,
                processDetector: new FixedProcessDetector(new WpfProcessInfo
                {
                    ProcessId = 4242,
                    ProcessName = "TestApp",
                    Architecture = ProcessArchitecture.X64,
                    Runtime = TargetRuntime.NetCore,
                    IsWpfApplication = true,
                    IsElevated = false,
                    ExecutablePath = Path.Combine(tempDirectory.FullName, "TestApp.exe")
                }),
                dllPathValidator: _ => throw new System.Security.Cryptography.CryptographicException(signatureError),
                isCurrentProcessElevated: () => false,
                inspectorCandidateResolver: _ => [inspectorPath],
                bootstrapperCandidateResolver: _ => [bootstrapperPath],
                isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

            var result = await ToolCallHelper.ExecuteAndWrapAsync(
                tool.ExecuteAsync,
                ToJsonElement(new { processId = 4242 }),
                CancellationToken.None);

            result.IsError.Should().BeTrue();
            var json = result.StructuredContent!.Value;
            json.GetProperty("errorCode").GetString().Should().Be("SecurityError");
            json.GetProperty("error").GetString().Should().Be("Security verification failed");
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private static JsonElement ToJsonElement(object value)
        => JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value));

    private static ConnectTool CreateTool(
        SessionManager sessionManager,
        RecordingProcessInjector injector,
        string inspectorPath,
        string bootstrapperPath,
        string executableDirectory)
    {
        return new ConnectTool(
            sessionManager,
            injector,
            processDetector: new FixedProcessDetector(new WpfProcessInfo
            {
                ProcessId = 4242,
                ProcessName = "TestApp",
                Architecture = ProcessArchitecture.X64,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true,
                IsElevated = false,
                ExecutablePath = Path.Combine(executableDirectory, "TestApp.exe")
            }),
            dllPathValidator: _ => { },
            isCurrentProcessElevated: () => false,
            inspectorCandidateResolver: _ => [inspectorPath],
            bootstrapperCandidateResolver: _ => [bootstrapperPath],
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);
    }

    private sealed class FixedProcessDetector : WpfProcessDetector
    {
        private readonly WpfProcessInfo _processInfo;

        public FixedProcessDetector(WpfProcessInfo processInfo)
        {
            _processInfo = processInfo;
        }

        public override WpfProcessInfo? GetProcessInfo(int processId)
            => processId == _processInfo.ProcessId ? _processInfo : null;
    }

    private sealed class RecordingProcessInjector : IProcessInjector
    {
        public int InjectWithBootstrapCallCount { get; private set; }
        public InjectionResult? Failure { get; init; }

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => throw new NotSupportedException();

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult InjectWithBootstrap(InjectionRequest request, CancellationToken cancellationToken = default)
        {
            InjectWithBootstrapCallCount++;
            return Failure ?? InjectionResult.CreateSuccess(request.ProcessId, request.InspectorDllPath);
        }
    }
}