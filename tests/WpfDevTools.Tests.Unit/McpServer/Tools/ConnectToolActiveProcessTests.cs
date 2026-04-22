using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed class ConnectToolActiveProcessTests : IDisposable
{
    private string? _dummyBootstrapperPath;

    [Fact]
    public async Task Execute_WhenFreshConnectionSucceeds_ShouldSelectConnectedProcessAsActive()
    {
        var existingProcessId = NextSyntheticProcessId();
        var connectedProcessId = Environment.ProcessId;
        EnsureDummyBootstrapperExists();

        using var sessionManager = new SessionManager();
        sessionManager.AddSession(existingProcessId);
        using var injector = new BootstrapStartsPipeInjector();

        var tool = new ConnectTool(
            sessionManager,
            injector,
            new FakeProcessDetector(),
            _ => { },
            () => false,
            isRawInjectionTargetAllowed: _ => true);

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId = connectedProcessId }),
            CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        sessionManager.TryGetActiveProcessId(out var activeProcessId).Should().BeTrue();
        activeProcessId.Should().Be(connectedProcessId);
    }

    public void Dispose()
    {
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = EnsureSharedDummyBootstrapperExists();
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

    private class FakeProcessInjector : IProcessInjector
    {
        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => throw new NotSupportedException();

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public virtual InjectionResult InjectWithBootstrap(
            InjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            return InjectionResult.CreateSuccess(
                request.ProcessId,
                request.InspectorDllPath,
                bootstrapExitCode: 0,
                pipeName: request.ExpectedPipeName);
        }
    }

    private sealed class BootstrapStartsPipeInjector : FakeProcessInjector, IDisposable
    {
        private readonly List<InspectorHost> _hosts = [];

        public override InjectionResult InjectWithBootstrap(
            InjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            var host = new InspectorHost(request.ProcessId);
            host.Start();
            _hosts.Add(host);

            return base.InjectWithBootstrap(request, cancellationToken);
        }

        public void Dispose()
        {
            foreach (var host in _hosts)
            {
                host.Stop();
                host.Dispose();
            }
        }
    }
}
