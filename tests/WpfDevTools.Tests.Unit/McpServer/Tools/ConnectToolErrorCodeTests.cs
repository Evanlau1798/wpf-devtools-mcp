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
            _ => { });

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("Timeout");
    }

    public void Dispose()
    {
        if (_dummyBootstrapperPath != null && File.Exists(_dummyBootstrapperPath))
        {
            try { File.Delete(_dummyBootstrapperPath); } catch { }
        }
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Bootstrapper.x64.dll");
        if (!File.Exists(_dummyBootstrapperPath))
        {
            File.WriteAllBytes(_dummyBootstrapperPath, Array.Empty<byte>());
        }
    }

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

    private sealed class FailingProcessInjector(InjectionError injectionError) : IProcessInjector
    {
        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => InjectionResult.CreateSuccess(processId, dllPath);

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult InjectWithBootstrap(InjectionRequest request, CancellationToken cancellationToken = default)
            => InjectionResult.CreateFailure(request.ProcessId, injectionError, "Bootstrap timed out");
    }
}
