using System.IO;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// End-to-end bootstrap injection tests.
/// ConnectTool_ThenPingTool requires native bootstrapper DLLs built.
/// ConnectTool_WhenBootstrapFails uses fault injection with FakeProcessDetector (no TestApp needed).
/// </summary>
[Collection("WpfAndBootstrapIntegration")]
public class BootstrapInjectionTests : IDisposable
{
    private static readonly TimeSpan LiveTestAppStartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LiveSmokeTimeout = TimeSpan.FromSeconds(60);
    private readonly ITestOutputHelper _output;
    private System.Diagnostics.Process? _testApp;
    private DummyBootstrapperArtifact? _dummyBootstrapperArtifact;

    public BootstrapInjectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private System.Diagnostics.Process StartTestApp(
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        return TestAppProcessLauncher.StartAndWaitForMainWindow(
            TestAppProcessLauncher.FindTestAppExe(),
            LiveTestAppStartupTimeout,
            environmentVariables);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectTool_ThenPingTool_ShouldSucceed()
    {
        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the live bootstrap smoke test must fail fast when native bootstrapper artifacts are missing; " +
            "build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        using var smokeTimeoutCts = new CancellationTokenSource(LiveSmokeTimeout);
        _testApp = StartTestApp();

        using var liveSession = SecureLiveSession.Create("WpfDevTools_BootstrapSmoke");
        var sessionManager = liveSession.SessionManager;
        var injector = new ProcessInjector();
        var detector = new WpfProcessDetector();
        var connectTool = new ConnectTool(sessionManager, injector, detector,
            dllPathValidator: TrustedLocalReleaseSignatureSkip.ValidateDllPath,
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: _ => new McpTargetAuthorization(true, null, null));
        var pingTool = new PingTool(sessionManager);

        var connectArgs = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = _testApp.Id }));

        var connectResult = await connectTool.ExecuteAsync(connectArgs, smokeTimeoutCts.Token);
        var connectJson = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(connectResult));
        var connectError = connectJson.TryGetProperty("error", out var errorProp)
            ? errorProp.GetString()
            : null;
        var connectStage = connectJson.TryGetProperty("stage", out var stageProp)
            ? stageProp.GetString()
            : null;
        var connectExitCode = connectJson.TryGetProperty("exitCode", out var exitCodeProp)
            ? exitCodeProp.ToString()
            : null;
        connectJson.GetProperty("success").GetBoolean().Should().BeTrue(
            $"connect should succeed with real injection. Error={connectError ?? "<none>"}, " +
            $"Stage={connectStage ?? "<none>"}, ExitCode={connectExitCode ?? "<none>"}");

        var pingArgs = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = _testApp.Id }));

        var pingResult = await pingTool.ExecuteAsync(pingArgs, smokeTimeoutCts.Token);
        var pingJson = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(pingResult));
        pingJson.GetProperty("success").GetBoolean().Should().BeTrue(
            "ping should succeed after connect");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectTool_WithSecureIpcEnabled_ThenPingTool_ShouldSucceed()
    {
        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the secure live bootstrap smoke test must fail fast when native bootstrapper artifacts are missing; " +
            "build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        using var smokeTimeoutCts = new CancellationTokenSource(LiveSmokeTimeout);
        _testApp = StartTestApp();

        using var liveSession = SecureLiveSession.Create("WpfDevTools_SecureConnect");
        var sessionManager = liveSession.SessionManager;
        var injector = new ProcessInjector();
        var detector = new WpfProcessDetector();
        var connectTool = new ConnectTool(sessionManager, injector, detector,
            dllPathValidator: TrustedLocalReleaseSignatureSkip.ValidateDllPath,
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: _ => new McpTargetAuthorization(true, null, null));
        var pingTool = new PingTool(sessionManager);

        var connectArgs = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = _testApp.Id }));

        var connectResult = await connectTool.ExecuteAsync(connectArgs, smokeTimeoutCts.Token);
        var connectJson = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(connectResult));
        var connectError = connectJson.TryGetProperty("error", out var errorProp)
            ? errorProp.GetString()
            : null;
        connectJson.GetProperty("success").GetBoolean().Should().BeTrue(
            $"secure connect should succeed with auth+TLS enabled. Error={connectError ?? "<none>"}");

        var pingArgs = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = _testApp.Id }));
        var pingResult = await pingTool.ExecuteAsync(pingArgs, smokeTimeoutCts.Token);
        var pingJson = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(pingResult));
        pingJson.GetProperty("success").GetBoolean().Should().BeTrue(
            "ping should succeed after secure connect");
    }


#if DEBUG
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectTool_WhenBootstrapHostStartConsumesSharedBudget_ShouldReturnStructuredPipeReadyTimeout()
    {
        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the live bootstrap timeout coverage must fail fast when native bootstrapper artifacts are missing; " +
            "build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        _testApp = StartTestApp(
            environmentVariables: new Dictionary<string, string>
            {
                [IntegrationTestDelayHooks.DelayBeforeHostStartEnvVar] = "2500"
            });

        using var liveSession = SecureLiveSession.Create("WpfDevTools_BootstrapTimeout");
        var sessionManager = liveSession.SessionManager;
        var connectTool = CreateLiveConnectTool(sessionManager, TimeSpan.FromMilliseconds(1800));

        var connectResult = await connectTool.ExecuteAsync(
            JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new { processId = _testApp.Id })),
            CancellationToken.None);
        var connectJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(connectResult));

        connectJson.GetProperty("success").GetBoolean().Should().BeFalse();
        connectJson.GetProperty("errorCode").GetString().Should().Be("Timeout");
        connectJson.GetProperty("stage").GetString().Should().Be("PipeReady");
        connectJson.GetProperty("error").GetString().Should().Contain("Named Pipe");
    }
#endif

#if DEBUG
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectTool_WhenFinalAttachConsumesSharedBudget_ShouldReturnStructuredNamedPipeTimeout()
    {
        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the live final-attach timeout coverage must fail fast when native bootstrapper artifacts are missing; " +
            "build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        _testApp = StartTestApp(
            environmentVariables: new Dictionary<string, string>
            {
                [IntegrationTestDelayHooks.DelayAfterPipeConnectEnvVar] = "3000"
            });

        using var liveSession = SecureLiveSession.Create("WpfDevTools_FinalAttachTimeout");
        var sessionManager = liveSession.SessionManager;
        var connectTool = CreateLiveConnectTool(sessionManager, TimeSpan.FromMilliseconds(2200));

        var connectResult = await connectTool.ExecuteAsync(
            JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new { processId = _testApp.Id })),
            CancellationToken.None);
        var connectJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(connectResult));

        connectJson.GetProperty("success").GetBoolean().Should().BeFalse();
        connectJson.GetProperty("errorCode").GetString().Should().Be("Timeout");
        connectJson.TryGetProperty("stage", out _).Should().BeFalse();
        connectJson.GetProperty("error").GetString().Should().Be("Timeout connecting to Inspector Named Pipe");
    }
#endif

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectTool_WhenBootstrapFails_ShouldReturnStageError()
    {
        EnsureDummyBootstrapperExists();

        var sessionManager = new SessionManager();
        var faultInjector = new FaultBootstrapInjector(
            InjectionResult.CreateFailure(
                12345,
                InjectionError.BootstrapFailed,
                "Fault bootstrapper forced ManagedEntrypoint failure",
                bootstrapExitCode: 0x12,
                failedAtStage: BootstrapStage.ManagedEntrypoint));

        // Use FakeProcessDetector to avoid starting a real TestApp
        var connectTool = new ConnectTool(
            sessionManager, faultInjector, new FakeProcessDetector(), isRawInjectionTargetAllowed: _ => true,
            targetPolicy: _ => new McpTargetAuthorization(true, null, null),
            dllPathValidator: TrustedLocalReleaseSignatureSkip.ValidateDllPath);

        var args = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = 12345 }));

        var result = await connectTool.ExecuteAsync(args, CancellationToken.None);
        var resultJson = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(result));

        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("managed bootstrap entrypoint");
        resultJson.GetProperty("stage").GetString().Should().Be("ManagedEntrypoint");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void DummyBootstrapperArtifact_WhenFileAlreadyExists_ShouldPreserveItOnDispose()
    {
        var temporaryRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        var bootstrapperPath = Path.Combine(temporaryRoot, "WpfDevTools.Bootstrapper.x64.dll");
        var originalBytes = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(bootstrapperPath, originalBytes);

        try
        {
            var artifact = DummyBootstrapperArtifact.EnsureExists(temporaryRoot);
            artifact.Dispose();

            File.Exists(bootstrapperPath).Should().BeTrue();
            File.ReadAllBytes(bootstrapperPath).Should().Equal(originalBytes);
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(temporaryRoot);
        }
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperArtifact = DummyBootstrapperArtifact.EnsureExists(AppContext.BaseDirectory);
    }

    private static string FindTestAppExe()
    {
        return TestAppProcessLauncher.FindTestAppExe();
    }

    private static ConnectTool CreateLiveConnectTool(SessionManager sessionManager, TimeSpan connectTimeout)
    {
        return new ConnectTool(
            sessionManager: sessionManager,
            injector: new ProcessInjector(),
            processDetector: new WpfProcessDetector(),
            dllPathValidator: TrustedLocalReleaseSignatureSkip.ValidateDllPath,
            isCurrentProcessElevated: null,
            workingSetResolver: null,
            inspectorCandidateResolver: null,
            bootstrapperCandidateResolver: null,
            pipeReadyProbe: null,
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: _ => new McpTargetAuthorization(true, null, null),
            connectInjectedSessionAsync: null,
            connectTimeout: connectTimeout);
    }

    private sealed class FakeProcessDetector : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId)
        {
            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "TestApp",
                Architecture = ProcessArchitecture.X64,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true
            };
        }
    }

    private sealed class FaultBootstrapInjector : IProcessInjector
    {
        private readonly InjectionResult _result;

        public FaultBootstrapInjector(InjectionResult result) => _result = result;

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => throw new NotSupportedException(
                "Legacy Inject not used in bootstrap integration tests.");

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult InjectWithBootstrap(
            InjectionRequest request,
            CancellationToken cancellationToken = default) => _result;
    }

    private sealed class DummyBootstrapperArtifact : IDisposable
    {
        private const string FileName = "WpfDevTools.Bootstrapper.x64.dll";

        private readonly bool _createdByTest;

        private DummyBootstrapperArtifact(string filePath, bool createdByTest)
        {
            FilePath = filePath;
            _createdByTest = createdByTest;
        }

        public string FilePath { get; }

        public static DummyBootstrapperArtifact EnsureExists(string rootDirectory)
        {
            var filePath = Path.Combine(rootDirectory, FileName);
            if (File.Exists(filePath))
            {
                return new DummyBootstrapperArtifact(filePath, createdByTest: false);
            }

            File.WriteAllBytes(filePath, Array.Empty<byte>());
            return new DummyBootstrapperArtifact(filePath, createdByTest: true);
        }

        public void Dispose()
        {
            if (!_createdByTest || !File.Exists(FilePath))
            {
                return;
            }

            try { File.Delete(FilePath); }
            catch { /* best effort cleanup */ }
        }
    }

    public void Dispose()
    {
        LiveTestProcessCleanup.StopAndDispose(_testApp);
        _testApp = null;

        _dummyBootstrapperArtifact?.Dispose();
    }
}
