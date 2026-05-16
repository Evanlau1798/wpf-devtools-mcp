using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class ConnectToolConcurrencyTests
{
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
            () => false,
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        using var firstCallCts = new CancellationTokenSource();
        var firstCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), firstCallCts.Token);
        injector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();

        var secondCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        firstCallCts.Cancel();

        Func<Task> cancelledWait = async () => await firstCall;
        await cancelledWait.Should().ThrowAsync<OperationCanceledException>();

        injector.Release();
        var secondResult = await secondCall;

        injector.InjectWithBootstrapCallCount.Should().Be(1);
        var json = JsonSerializer.SerializeToElement(secondResult);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
        json.GetProperty("error").GetString().Should().Contain("Bootstrap failed");
    }

    [Fact]
    public async Task ProcessMcpToolsConnect_WhenOriginalCallerCancels_ShouldNotCancelOtherProtocolWaiters()
    {
        EnsureDummyBootstrapperExists();

        using var toolCallHelperScope = ToolCallHelper.BeginTestScope();
        using var sessionManager = new SessionManager();
        using var injector = new BlockingFailureInjector();
        var connectTool = new ConnectTool(
            sessionManager,
            injector,
            new FakeProcessDetector(),
            _ => { },
            () => false,
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);
        ToolCallHelper.CachedTool<ConnectTool>("ConnectTool", () => connectTool);

        using var firstCallCts = new CancellationTokenSource();
        var firstCall = ProcessMcpTools.Connect(sessionManager, processId: 12345, cancellationToken: firstCallCts.Token);
        injector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();

        var secondCall = ProcessMcpTools.Connect(sessionManager, processId: 12345, cancellationToken: CancellationToken.None);

        firstCallCts.Cancel();

        Func<Task> cancelledWait = async () => await firstCall;
        await cancelledWait.Should().ThrowAsync<OperationCanceledException>();

        injector.Release();
        var secondResult = await secondCall;

        injector.InjectWithBootstrapCallCount.Should().Be(1);
        secondResult.IsError.Should().BeTrue();
        secondResult.StructuredContent.Should().NotBeNull();
        secondResult.StructuredContent!.Value.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
    }

    [Fact]
    public async Task Execute_WhenAllSingleFlightWaitersCancel_ShouldCancelInjectorAndAllowFreshRetry()
    {
        EnsureDummyBootstrapperExists();

        using var sessionManager = new SessionManager();
        using var injector = new BlockingFailureInjector();
        var tool = new ConnectTool(
            sessionManager,
            injector,
            new FakeProcessDetector(),
            _ => { },
            () => false,
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        using var firstCallCts = new CancellationTokenSource();
        using var secondCallCts = new CancellationTokenSource();

        var firstCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), firstCallCts.Token);
        injector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();

        var secondCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), secondCallCts.Token);
        await Task.Delay(50);

        firstCallCts.Cancel();
        secondCallCts.Cancel();

        Func<Task> firstCancelledWait = async () => await firstCall;
        Func<Task> secondCancelledWait = async () => await secondCall;
        await firstCancelledWait.Should().ThrowAsync<OperationCanceledException>();
        await secondCancelledWait.Should().ThrowAsync<OperationCanceledException>();

        injector.CancellationObserved.Wait(SignalWaitTimeout).Should().BeTrue();

        var thirdCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
        injector.AdditionalCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();
        injector.Release();

        var thirdResult = await thirdCall;

        injector.InjectWithBootstrapCallCount.Should().Be(2);
        var json = JsonSerializer.SerializeToElement(thirdResult);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
        json.GetProperty("error").GetString().Should().Contain("Bootstrap failed");
    }

    [Fact]
    public async Task Execute_WhenNewCallerArrivesAfterLastWaiterCancelsButBeforeCleanup_ShouldWaitForSettlementBeforeStartingFreshOperation()
    {
        EnsureDummyBootstrapperExists();

        using var sessionManager = new SessionManager();
        using var injector = new CancellationWindowInjector();
        var tool = new ConnectTool(
            sessionManager,
            injector,
            new FakeProcessDetector(),
            _ => { },
            () => false,
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        using var cancelledCallCts = new CancellationTokenSource();
        var cancelledCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), cancelledCallCts.Token);
        injector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();

        cancelledCallCts.Cancel();
        injector.CancellationObserved.Wait(SignalWaitTimeout).Should().BeTrue();

        var cancelledCompletion = await Task.WhenAny(cancelledCall, Task.Delay(TimeSpan.FromSeconds(1)));
        cancelledCompletion.Should().Be(
            cancelledCall,
            "the final cancelled caller should observe cancellation promptly even if the shared operation is still unwinding");

        Func<Task> cancelledWait = async () => await cancelledCall;
        await cancelledWait.Should().ThrowAsync<OperationCanceledException>();

        var freshCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        await Task.Delay(100);
        injector.AdditionalCallStarted.IsSet.Should().BeFalse(
            "a closing cancelled operation must settle before a fresh operation starts for the same shared SessionManager + processId key");

        injector.AllowCancelledOperationToFinish();

        injector.AdditionalCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();

        var freshResult = JsonSerializer.SerializeToElement(await freshCall);
        injector.InjectWithBootstrapCallCount.Should().Be(2);
        freshResult.GetProperty("success").GetBoolean().Should().BeFalse();
        freshResult.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
        freshResult.GetProperty("error").GetString().Should().Contain("Bootstrap failed");
    }

    private sealed class CancellationWindowInjector : IProcessInjector, IDisposable
    {
        private readonly ManualResetEventSlim _allowCancelledOperationToFinish = new(false);

        public ManualResetEventSlim FirstCallStarted { get; } = new(false);
        public ManualResetEventSlim AdditionalCallStarted { get; } = new(false);
        public ManualResetEventSlim CancellationObserved { get; } = new(false);
        public int InjectWithBootstrapCallCount { get; private set; }

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => throw new NotSupportedException();

        public InjectionError ValidateTarget(int processId) => InjectionError.None;

        public InjectionResult InjectWithBootstrap(
            InjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            InjectWithBootstrapCallCount++;
            if (InjectWithBootstrapCallCount == 1)
            {
                FirstCallStarted.Set();
                try
                {
                    cancellationToken.WaitHandle.WaitOne();
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    CancellationObserved.Set();
                    _allowCancelledOperationToFinish.Wait(SignalWaitTimeout);
                    throw;
                }
            }

            AdditionalCallStarted.Set();
            return InjectionResult.CreateFailure(
                request.ProcessId,
                InjectionError.BootstrapFailed,
                "Simulated injection failure");
        }

        public void AllowCancelledOperationToFinish() => _allowCancelledOperationToFinish.Set();

        public void Dispose()
        {
            _allowCancelledOperationToFinish.Dispose();
            FirstCallStarted.Dispose();
            AdditionalCallStarted.Dispose();
            CancellationObserved.Dispose();
        }
    }
}
