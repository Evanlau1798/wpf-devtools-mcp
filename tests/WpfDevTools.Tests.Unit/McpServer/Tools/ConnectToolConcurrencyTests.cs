using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed class ConnectToolConcurrencyTests : IDisposable
{
    private static readonly TimeSpan SignalWaitTimeout = TimeSpan.FromSeconds(10);
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
            () => false,
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        var firstCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
        injector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();

        var secondCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        await Task.Delay(100);
        injector.Release();
        var results = await Task.WhenAll(firstCall, secondCall);

        injector.InjectWithBootstrapCallCount.Should().Be(1);
        foreach (var result in results)
        {
            var json = JsonSerializer.SerializeToElement(result);
            json.GetProperty("success").GetBoolean().Should().BeFalse();
            json.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
            json.GetProperty("error").GetString().Should().Contain("Bootstrap failed");
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
    public async Task Execute_WithParallelConnectCallsAcrossToolInstances_ShouldSingleFlightSharedSessionManager()
    {
        EnsureDummyBootstrapperExists();

        using var sessionManager = new SessionManager();
        using var injector = new BlockingFailureInjector();
        var firstTool = new ConnectTool(
            sessionManager,
            injector,
            new FakeProcessDetector(),
            _ => { },
            () => false,
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);
        var secondTool = new ConnectTool(
            sessionManager,
            injector,
            new FakeProcessDetector(),
            _ => { },
            () => false,
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        var firstCall = firstTool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
        injector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();

        var secondCall = secondTool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        await Task.Delay(100);
        injector.Release();
        var results = await Task.WhenAll(firstCall, secondCall);

        injector.InjectWithBootstrapCallCount.Should().Be(1);
        foreach (var result in results)
        {
            var json = JsonSerializer.SerializeToElement(result);
            json.GetProperty("success").GetBoolean().Should().BeFalse();
            json.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
            json.GetProperty("error").GetString().Should().Contain("Bootstrap failed");
        }
    }

    [Fact]
    public async Task Execute_WithParallelConnectCallsAcrossDifferentSessionManagers_ShouldNotShareSingleFlightOperation()
    {
        EnsureDummyBootstrapperExists();

        using var firstSessionManager = new SessionManager();
        using var secondSessionManager = new SessionManager();
        using var firstInjector = new BlockingFailureInjector();
        using var secondInjector = new BlockingFailureInjector();
        var firstTool = new ConnectTool(
            firstSessionManager,
            firstInjector,
            new FakeProcessDetector(),
            _ => { },
            () => false,
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);
        var secondTool = new ConnectTool(
            secondSessionManager,
            secondInjector,
            new FakeProcessDetector(),
            _ => { },
            () => false,
            pipeReadyProbe: CreateNoExistingHostPipeReadyProbe(),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        var firstCall = firstTool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
        firstInjector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();

        var secondCall = secondTool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
        try
        {
            secondInjector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue(
                "in-flight connect keys include the SessionManager instance, so separate hosts must not share a single-flight operation");
        }
        finally
        {
            firstInjector.Release();
            secondInjector.Release();
        }

        await Task.WhenAll(firstCall, secondCall);

        firstInjector.InjectWithBootstrapCallCount.Should().Be(1);
        secondInjector.InjectWithBootstrapCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Execute_WhenSingleFlightOperationCompletes_ShouldRemoveInflightEntryAndAllowFreshOperation()
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

        var firstCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
        injector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();
        injector.Release();
        await firstCall;

        injector.ResetReleaseGate();
        var secondCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
        try
        {
            injector.AdditionalCallStarted.Wait(SignalWaitTimeout).Should().BeTrue(
                "completed single-flight operations must be removed from the static in-flight cache");
        }
        finally
        {
            injector.Release();
        }

        await secondCall;

        injector.InjectWithBootstrapCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Execute_WhenNewCallerArrivesAfterInflightEntryRemovedBeforeCompletion_ShouldStartFreshOperation()
    {
        EnsureDummyBootstrapperExists();

        var hookEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hookCallCount = 0;
        ConnectTool.BeforeSingleFlightCompletionForTesting = () =>
        {
            if (Interlocked.Increment(ref hookCallCount) == 1)
            {
                hookEntered.SetResult();
                return allowCompletion.Task;
            }

            return Task.CompletedTask;
        };

        try
        {
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

            var firstCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
            injector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();
            injector.Release();
            await hookEntered.Task.WaitAsync(SignalWaitTimeout);

            var secondCall = tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);
            injector.AdditionalCallStarted.Wait(SignalWaitTimeout).Should().BeTrue(
                "callers arriving after an operation is closed to new waiters must start a fresh connect instead of joining a stale completion");

            allowCompletion.SetResult();
            await Task.WhenAll(firstCall, secondCall);

            injector.InjectWithBootstrapCallCount.Should().Be(2);
        }
        finally
        {
            allowCompletion.TrySetResult();
            ConnectTool.BeforeSingleFlightCompletionForTesting = null;
        }
    }

    [Fact]
    public async Task Execute_WithExplicitAndAutoDiscoveryCallsResolvingSameProcess_ShouldShapeResponsesPerCaller()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        using var sessionManager = new SessionManager();
        using var injector = new BlockingSuccessInjector();
        var tool = new ConnectTool(
            sessionManager,
            injector,
            new SingleProcessDetector(processId),
            _ => { },
            () => false,
            pipeReadyProbe: new PipeReadyProbe((_, _) => false, () => DateTime.UtcNow, _ => { }),
            isRawInjectionTargetAllowed: _ => true,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        var explicitCall = tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);
        injector.FirstCallStarted.Wait(SignalWaitTimeout).Should().BeTrue();

        var autoDiscoveryCall = tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        await Task.Delay(100);
        injector.Release();

        var explicitResult = JsonSerializer.SerializeToElement(await explicitCall);
        var autoDiscoveryResult = JsonSerializer.SerializeToElement(await autoDiscoveryCall);

        injector.InjectWithBootstrapCallCount.Should().Be(1);

        explicitResult.GetProperty("success").GetBoolean().Should().BeTrue();
        explicitResult.TryGetProperty("autoDiscovered", out _).Should().BeFalse();
        explicitResult.TryGetProperty("candidateCount", out _).Should().BeFalse();

        autoDiscoveryResult.GetProperty("success").GetBoolean().Should().BeTrue();
        autoDiscoveryResult.GetProperty("autoDiscovered").GetBoolean().Should().BeTrue();
        autoDiscoveryResult.GetProperty("candidateCount").GetInt32().Should().Be(1);
        autoDiscoveryResult.GetProperty("processId").GetInt32().Should().Be(processId);
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

    public void Dispose()
    {
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = EnsureSharedDummyBootstrapperExists();
    }

    private static PipeReadyProbe CreateNoExistingHostPipeReadyProbe()
        => new((_, _) => false, () => DateTime.UtcNow, _ => { });

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

    private sealed class SingleProcessDetector(int processId) : WpfProcessDetector
    {
        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
            => [CreateProcessInfo(processId)];

        public override WpfProcessInfo? GetProcessInfo(int requestedProcessId)
            => requestedProcessId == processId ? CreateProcessInfo(processId) : null;

        private static WpfProcessInfo CreateProcessInfo(int processId)
        {
            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "SingleApp",
                WindowTitle = "SingleApp Window",
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
            }
            else
            {
                AdditionalCallStarted.Set();
            }

            try
            {
                _release.Wait(SignalWaitTimeout, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.Set();
                throw;
            }

            return InjectionResult.CreateFailure(
                request.ProcessId,
                InjectionError.BootstrapFailed,
                "Simulated injection failure");
        }

        public void Release() => _release.Set();

        public void ResetReleaseGate() => _release.Reset();

        public void Dispose()
        {
            _release.Dispose();
            FirstCallStarted.Dispose();
            AdditionalCallStarted.Dispose();
            CancellationObserved.Dispose();
        }
    }

    private sealed class BlockingSuccessInjector : IProcessInjector, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private InspectorHost? _host;

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

            _release.Wait(SignalWaitTimeout, cancellationToken);
            _host = new InspectorHost(request.ProcessId, request.ExpectedPipeName);
            _host.Start();

            return InjectionResult.CreateSuccess(
                request.ProcessId,
                request.InspectorDllPath,
                bootstrapExitCode: 0,
                pipeName: request.ExpectedPipeName);
        }

        public void Release() => _release.Set();

        public void Dispose()
        {
            _host?.Stop();
            _host?.Dispose();
            _release.Dispose();
            FirstCallStarted.Dispose();
        }
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
