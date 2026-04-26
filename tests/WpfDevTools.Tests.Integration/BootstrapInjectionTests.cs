using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Security;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// End-to-end bootstrap injection tests.
/// ConnectTool_ThenPingTool requires native bootstrapper DLLs built.
/// ConnectTool_WhenBootstrapFails uses fault injection with FakeProcessDetector (no TestApp needed).
/// </summary>
[Collection("LiveBootstrapIntegration")]
public class BootstrapInjectionTests : IDisposable
{
    private static readonly TimeSpan LiveTestAppStartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LiveSmokeTimeout = TimeSpan.FromSeconds(60);
    private readonly ITestOutputHelper _output;
    private System.Diagnostics.Process? _testApp;
    private string? _dummyBootstrapperPath;

    public BootstrapInjectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private System.Diagnostics.Process StartTestApp()
    {
        return TestAppProcessLauncher.StartAndWaitForMainWindow(
            TestAppProcessLauncher.FindTestAppExe(),
            LiveTestAppStartupTimeout);
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

        using var sessionManager = new SessionManager();
        var injector = new ProcessInjector();
        var detector = new WpfProcessDetector();
        var connectTool = new ConnectTool(sessionManager, injector, detector, isRawInjectionTargetAllowed: _ => true);
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

        var secretBytes = new byte[32];
        RandomNumberGenerator.Fill(secretBytes);
        var authSecret = Convert.ToBase64String(secretBytes);
        var certDirectory = Path.Combine(Path.GetTempPath(), $"WpfDevTools_SecureConnect_{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);

        try
        {
            using var sessionManager = new SessionManager(
                authManager: new AuthenticationManager(() => authSecret),
                certManager: new CertificateManager(certDirectory));
            var injector = new ProcessInjector();
            var detector = new WpfProcessDetector();
            var connectTool = new ConnectTool(sessionManager, injector, detector, isRawInjectionTargetAllowed: _ => true);
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
        finally
        {
            try
            {
                Directory.Delete(certDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for integration temp assets.
            }
        }
    }


    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectTool_WhenBootstrapHostStartConsumesSharedBudget_ShouldReturnStructuredPipeReadyTimeout()
    {
        EnsureDebugDelayHooksEnabled();

        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the live bootstrap timeout coverage must fail fast when native bootstrapper artifacts are missing; " +
            "build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        using var hostStartDelayScope = new EnvironmentVariableScope(
            IntegrationTestDelayHooks.DelayBeforeHostStartEnvVar,
            "2500");

        _testApp = StartTestApp();

        using var sessionManager = new SessionManager();
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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectTool_WhenFinalAttachConsumesSharedBudget_ShouldReturnStructuredNamedPipeTimeout()
    {
        EnsureDebugDelayHooksEnabled();

        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the live final-attach timeout coverage must fail fast when native bootstrapper artifacts are missing; " +
            "build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        using var attachDelayScope = new EnvironmentVariableScope(
            IntegrationTestDelayHooks.DelayAfterPipeConnectEnvVar,
            "3000");

        _testApp = StartTestApp();

        using var sessionManager = new SessionManager();
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
            sessionManager, faultInjector, new FakeProcessDetector(), isRawInjectionTargetAllowed: _ => true);

        var args = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = 12345 }));

        var result = await connectTool.ExecuteAsync(args, CancellationToken.None);
        var resultJson = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(result));

        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("managed bootstrap entrypoint");
        resultJson.GetProperty("stage").GetString().Should().Be("ManagedEntrypoint");
    }
    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = Path.Combine(
            AppContext.BaseDirectory, "WpfDevTools.Bootstrapper.x64.dll");
        if (!File.Exists(_dummyBootstrapperPath))
        {
            File.WriteAllBytes(_dummyBootstrapperPath, Array.Empty<byte>());
        }
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
            dllPathValidator: null,
            isCurrentProcessElevated: null,
            workingSetResolver: null,
            inspectorCandidateResolver: null,
            bootstrapperCandidateResolver: null,
            pipeReadyProbe: null,
            isRawInjectionTargetAllowed: _ => true,
            connectInjectedSessionAsync: null,
            connectTimeout: connectTimeout);
    }

    private static void EnsureDebugDelayHooksEnabled()
    {
#if !DEBUG
        throw SkipException.ForSkip(
            "Live timeout-budget bootstrap coverage requires the Debug-only delay hooks and is skipped in Release test lanes.");
#endif
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

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }

    public void Dispose()
    {
        if (_testApp != null && !_testApp.HasExited)
        {
            _testApp.Kill();
            _testApp.WaitForExit(5000);
            _testApp.Dispose();
        }

        if (_dummyBootstrapperPath != null && File.Exists(_dummyBootstrapperPath))
        {
            try { File.Delete(_dummyBootstrapperPath); }
            catch { /* best effort cleanup */ }
        }
    }
}
