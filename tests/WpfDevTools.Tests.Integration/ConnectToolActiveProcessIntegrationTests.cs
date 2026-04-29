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

[Collection("LiveBootstrapIntegration")]
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

        using var sessionManager = new SessionManager();
        sessionManager.AddSession(54321);

        var connectTool = new ConnectTool(sessionManager, new ProcessInjector(), new WpfProcessDetector(), isRawInjectionTargetAllowed: _ => true,
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
        if (_testApp != null && !_testApp.HasExited)
        {
            _testApp.Kill();
            _testApp.WaitForExit(5000);
            _testApp.Dispose();
        }
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
