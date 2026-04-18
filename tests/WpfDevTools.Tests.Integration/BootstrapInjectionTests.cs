using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using System.Text.Json;
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

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// End-to-end bootstrap injection tests.
/// ConnectTool_ThenPingTool requires native bootstrapper DLLs built.
/// ConnectTool_WhenBootstrapFails uses fault injection with FakeProcessDetector (no TestApp needed).
/// </summary>
[Collection("LiveBootstrapIntegration")]
public class BootstrapInjectionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private System.Diagnostics.Process? _testApp;
    private string? _dummyBootstrapperPath;

    public BootstrapInjectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private System.Diagnostics.Process StartTestApp()
    {
        var testAppPath = FindTestAppExe();
        var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = testAppPath,
                UseShellExecute = true
            });
        Assert.NotNull(process);
        Thread.Sleep(3000);
        return process!;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectTool_ThenPingTool_ShouldSucceed()
    {
        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the live bootstrap smoke test must fail fast when native bootstrapper artifacts are missing; " +
            "build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        _testApp = StartTestApp();

        var sessionManager = new SessionManager();
        var injector = new ProcessInjector();
        var detector = new WpfProcessDetector();
        var connectTool = new ConnectTool(sessionManager, injector, detector);
        var pingTool = new PingTool(sessionManager);

        var connectArgs = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = _testApp.Id }));

        var connectResult = await connectTool.ExecuteAsync(connectArgs, CancellationToken.None);
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

        var pingResult = await pingTool.ExecuteAsync(pingArgs, CancellationToken.None);
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

        _testApp = StartTestApp();

        var secretBytes = new byte[32];
        RandomNumberGenerator.Fill(secretBytes);
        var authSecret = Convert.ToBase64String(secretBytes);
        var certDirectory = Path.Combine(Path.GetTempPath(), $"WpfDevTools_SecureConnect_{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);

        try
        {
            var sessionManager = new SessionManager(
                authManager: new AuthenticationManager(() => authSecret),
                certManager: new CertificateManager(certDirectory));
            var injector = new ProcessInjector();
            var detector = new WpfProcessDetector();
            var connectTool = new ConnectTool(sessionManager, injector, detector);
            var pingTool = new PingTool(sessionManager);

            var connectArgs = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(new { processId = _testApp.Id }));

            var connectResult = await connectTool.ExecuteAsync(connectArgs, CancellationToken.None);
            var connectJson = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(connectResult));
            var connectError = connectJson.TryGetProperty("error", out var errorProp)
                ? errorProp.GetString()
                : null;
            connectJson.GetProperty("success").GetBoolean().Should().BeTrue(
                $"secure connect should succeed with auth+TLS enabled. Error={connectError ?? "<none>"}");

            var pingArgs = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(new { processId = _testApp.Id }));
            var pingResult = await pingTool.ExecuteAsync(pingArgs, CancellationToken.None);
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
            sessionManager, faultInjector, new FakeProcessDetector());

        var args = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = 12345 }));

        var result = await connectTool.ExecuteAsync(args, CancellationToken.None);
        var resultJson = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(result));

        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("ManagedEntrypoint");
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
        return IntegrationExecutableLocator.FindExecutable(
                AppContext.BaseDirectory,
                "tests",
                "WpfDevTools.Tests.TestApp",
                "net8.0-windows",
                "WpfDevTools.Tests.TestApp.exe")
            ?? throw new InvalidOperationException(
                "TestApp executable not found for the current test configuration. Build tests/WpfDevTools.Tests.TestApp first.");
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
