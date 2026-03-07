using System.IO;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using Xunit;

namespace WpfDevTools.Tests.Integration;

/// <summary>
/// End-to-end bootstrap injection tests.
/// ConnectTool_ThenPingTool requires native bootstrapper DLLs built.
/// ConnectTool_WhenBootstrapFails uses fault injection (no native DLLs needed).
/// </summary>
public class BootstrapInjectionTests : IDisposable
{
    private System.Diagnostics.Process? _testApp;

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
        // Skip if native bootstrapper DLLs are not built
        var bootstrapperExists = HasNativeBootstrapper();
        if (!bootstrapperExists)
        {
            // Cannot run without native bootstrapper - skip gracefully
            return;
        }

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
        connectJson.GetProperty("success").GetBoolean().Should().BeTrue(
            "connect should succeed with real injection");

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
    public async Task ConnectTool_WhenBootstrapFails_ShouldReturnStageError()
    {
        _testApp = StartTestApp();

        // Ensure dummy bootstrapper exists so DLL discovery succeeds
        EnsureDummyBootstrapperExists();

        var sessionManager = new SessionManager();
        var faultInjector = new FaultBootstrapInjector(
            InjectionResult.CreateFailure(
                _testApp.Id,
                InjectionError.BootstrapFailed,
                "Fault bootstrapper forced ManagedEntrypoint failure",
                bootstrapExitCode: 0x12,
                failedAtStage: BootstrapStage.ManagedEntrypoint));

        var connectTool = new ConnectTool(
            sessionManager, faultInjector, new WpfProcessDetector());

        var args = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = _testApp.Id }));

        var result = await connectTool.ExecuteAsync(args, CancellationToken.None);
        var resultJson = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(result));

        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("ManagedEntrypoint");
        resultJson.GetProperty("stage").GetString().Should().Be("ManagedEntrypoint");
    }

    private static bool HasNativeBootstrapper()
    {
        var solutionDir = FindSolutionRoot();
        var artifactsDir = Path.Combine(solutionDir, "artifacts", "bootstrapper");
        if (Directory.Exists(artifactsDir))
        {
            var dlls = Directory.GetFiles(artifactsDir,
                "WpfDevTools.Bootstrapper.*.dll", SearchOption.AllDirectories);
            // Only count real DLLs (non-zero size, not dummy test files)
            if (dlls.Any(d => new FileInfo(d).Length > 0)) return true;
        }

        // Also check AppContext.BaseDirectory (must be real, not dummy)
        var localPath = Path.Combine(
            AppContext.BaseDirectory, "WpfDevTools.Bootstrapper.x64.dll");
        return File.Exists(localPath) && new FileInfo(localPath).Length > 0;
    }

    private static void EnsureDummyBootstrapperExists()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "WpfDevTools.Bootstrapper.x64.dll");
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, Array.Empty<byte>());
        }
    }

    private static string FindTestAppExe()
    {
        var solutionDir = FindSolutionRoot();
        var candidates = new[]
        {
            Path.Combine(solutionDir, "tests", "WpfDevTools.Tests.TestApp",
                "bin", "Debug", "net8.0-windows", "WpfDevTools.Tests.TestApp.exe"),
            Path.Combine(solutionDir, "tests", "WpfDevTools.Tests.TestApp",
                "bin", "Release", "net8.0-windows", "WpfDevTools.Tests.TestApp.exe")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException(
                "TestApp executable not found. Build tests/WpfDevTools.Tests.TestApp first.");
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WpfDevTools.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Solution root not found");
    }

    private sealed class FaultBootstrapInjector : IProcessInjector
    {
        private readonly InjectionResult _result;

        public FaultBootstrapInjector(InjectionResult result) => _result = result;

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => throw new NotSupportedException(
                "Legacy Inject not used in bootstrap integration tests.");

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult InjectWithBootstrap(InjectionRequest request) => _result;
    }

    public void Dispose()
    {
        if (_testApp != null && !_testApp.HasExited)
        {
            _testApp.Kill();
            _testApp.WaitForExit(5000);
            _testApp.Dispose();
        }
    }
}
