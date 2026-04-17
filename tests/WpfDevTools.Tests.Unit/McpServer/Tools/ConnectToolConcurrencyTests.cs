using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class ConnectToolConcurrencyTests : IDisposable
{
    private string? _dummyBootstrapperPath;

    [Fact]
    public async Task Execute_WithParallelConnectCalls_ShouldSingleFlightInjection()
    {
        EnsureDummyBootstrapperExists();

        using var sessionManager = new SessionManager();
        using var injector = new BlockingFailureInjector();
        var tool = new ConnectTool(
            sessionManager,
            injector,
            new FakeProcessDetector(),
            _ => { },
            () => false);

        var firstCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
        injector.FirstCallStarted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        var secondCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        await Task.Delay(100);
        injector.Release();
        var results = await Task.WhenAll(firstCall, secondCall);

        injector.InjectWithBootstrapCallCount.Should().Be(1);
        foreach (var result in results)
        {
            var json = JsonSerializer.SerializeToElement(result);
            json.GetProperty("success").GetBoolean().Should().BeFalse();
            json.GetProperty("error").GetString().Should().Contain("Simulated injection failure");
        }
    }

    [Fact]
    public async Task Execute_WhenOriginalCallerCancels_ShouldNotCancelOtherSingleFlightWaiters()
    {
        EnsureDummyBootstrapperExists();

        using var sessionManager = new SessionManager();
        using var injector = new BlockingFailureInjector();
        var tool = new ConnectTool(
            sessionManager,
            injector,
            new FakeProcessDetector(),
            _ => { },
            () => false);

        using var firstCallCts = new CancellationTokenSource();
        var firstCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), firstCallCts.Token);
        injector.FirstCallStarted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        var secondCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        firstCallCts.Cancel();

        Func<Task> cancelledWait = async () => await firstCall;
        await cancelledWait.Should().ThrowAsync<OperationCanceledException>();

        injector.Release();
        var secondResult = await secondCall;

        injector.InjectWithBootstrapCallCount.Should().Be(1);
        var json = JsonSerializer.SerializeToElement(secondResult);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("error").GetString().Should().Contain("Simulated injection failure");
    }

    public void Dispose()
    {
        if (_dummyBootstrapperPath != null && File.Exists(_dummyBootstrapperPath))
        {
            try { File.Delete(_dummyBootstrapperPath); }
            catch { }
        }
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = Path.Combine(
            AppContext.BaseDirectory,
            "WpfDevTools.Bootstrapper.x64.dll");
        if (!File.Exists(_dummyBootstrapperPath))
        {
            File.WriteAllBytes(_dummyBootstrapperPath, Array.Empty<byte>());
        }
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
                IsWpfApplication = true,
                IsElevated = false
            };
        }
    }

    private sealed class BlockingFailureInjector : IProcessInjector, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);

        public ManualResetEventSlim FirstCallStarted { get; } = new(false);

        public int InjectWithBootstrapCallCount { get; private set; }

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => throw new NotSupportedException();

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult InjectWithBootstrap(
            InjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            InjectWithBootstrapCallCount++;
            FirstCallStarted.Set();
            _release.Wait(TimeSpan.FromSeconds(2), cancellationToken);
            return InjectionResult.CreateFailure(
                request.ProcessId,
                InjectionError.BootstrapFailed,
                "Simulated injection failure");
        }

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Dispose();
            FirstCallStarted.Dispose();
        }
    }
}
