using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Tests.Integration.TestSupport;

namespace WpfDevTools.Tests.Integration;

[Collection("WpfAndBootstrapIntegration")]
public sealed class ConnectToolActiveProcessIntegrationTests : IDisposable
{
    private Process? _testApp;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectTool_WhenAnotherSessionIsActive_ShouldPromoteConnectedProcess()
    {
        BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory).Should().BeTrue(
            "the live bootstrap smoke test must fail fast when native bootstrapper artifacts are missing; " +
            "build src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj first");

        _testApp = StartTestApp();

        using var liveSession = SecureLiveSession.Create("WpfDevTools_ActiveProcess");
        var sessionManager = liveSession.SessionManager;
        sessionManager.AddSession(Environment.ProcessId);

        var connectTool = new ConnectTool(sessionManager, new ProcessInjector(), new WpfProcessDetector(),
            dllPathValidator: TrustedLocalReleaseSignatureSkip.ValidateDllPath,
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: _ => new McpTargetAuthorization(true, null, null));
        var connectArgs = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { processId = _testApp.Id }));

        var connectResult = await connectTool.ExecuteAsync(connectArgs, CancellationToken.None);
        var connectJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(connectResult));

        connectJson.GetProperty("success").GetBoolean().Should().BeTrue();
        sessionManager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(_testApp.Id);
    }

    public void Dispose()
    {
        LiveTestProcessCleanup.StopAndDispose(_testApp);
        _testApp = null;
    }

    private static Process StartTestApp()
    {
        return TestAppProcessLauncher.StartAndWaitForMainWindow(TestAppProcessLauncher.FindTestAppExe());
    }

    private static string FindTestAppExe()
    {
        return TestAppProcessLauncher.FindTestAppExe();
    }
}
