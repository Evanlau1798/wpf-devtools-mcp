using System.Diagnostics;
using System.Windows.Threading;
using FluentAssertions;
using SdkInspector = WpfDevTools.Inspector.Sdk.InspectorSdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

[Collection("ProcessEnvironment")]
public sealed class InspectorSdkDispatcherLifecycleTests
{
    [Fact]
    public void InvokeInitializeOnDispatcher_WithBlockedDispatcher_ShouldRespectTimeout()
    {
        Dispatcher? dispatcher = null;
        using var dispatcherReady = new ManualResetEventSlim(false);
        using var blockingStarted = new ManualResetEventSlim(false);
        using var releaseBlock = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(() =>
            {
                blockingStarted.Set();
                releaseBlock.Wait();
            }), DispatcherPriority.Send);

            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        blockingStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        try
        {
            var act = () => SdkInspector.InvokeInitializeOnDispatcher(
                dispatcher!,
                processId: 12345,
                timeout: TimeSpan.FromMilliseconds(100));

            act.Should().Throw<TimeoutException>();
        }
        finally
        {
            releaseBlock.Set();
            dispatcher!.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task InvokeInitializeOnDispatcher_WhenInitializationStartsWithinTimeout_ShouldRespectWholeDeadline()
    {
        using var testContext = new InspectorSdkTestContext();
        var certDirectory = testContext.CreateTemporaryDirectory("wpf-devtools-sdk-dispatcher-entry");
        Dispatcher? dispatcher = null;
        using var dispatcherReady = new ManualResetEventSlim(false);
        using var hostStarted = new ManualResetEventSlim(false);
        using var releaseInitialization = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        try
        {
            SdkInspector.HostStartedCallback = _ =>
            {
                hostStarted.Set();
                releaseInitialization.Wait();
            };
            testContext.SetTransport(InspectorSdkTestContext.CreateAuthSecret(), certDirectory);

            var initializeTask = Task.Run(() =>
                SdkInspector.InvokeInitializeOnDispatcher(
                    dispatcher!,
                    processId: 12345,
                    timeout: TimeSpan.FromMilliseconds(100)));

            hostStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            initializeTask.IsCompleted.Should().BeTrue(
                "the dispatcher initialization timeout should bound the whole wait path, including work after the dispatcher starts executing");

            var exception = await Assert.ThrowsAsync<TimeoutException>(() => initializeTask);
            exception.Message.Should().Contain("while completing WpfDevTools Inspector SDK initialization");

            releaseInitialization.Set();
            dispatcher!.Invoke(() => { }, DispatcherPriority.Background);

            SdkInspector.IsInitialized.Should().BeFalse(
                "a deadline-expired dispatcher initialization must not publish initialized state later");
            InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();
        }
        finally
        {
            releaseInitialization.Set();
            dispatcher!.BeginInvokeShutdown(DispatcherPriority.Send);
            thread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task InvokeInitializeOnDispatcher_WhenInitializationStartsDuringTimeoutAbortBoundary_ShouldRespectWholeDeadline()
    {
        using var testContext = new InspectorSdkTestContext();
        var certDirectory = testContext.CreateTemporaryDirectory("wpf-devtools-sdk-dispatcher-boundary");
        Dispatcher? dispatcher = null;
        using var dispatcherReady = new ManualResetEventSlim(false);
        using var blockingStarted = new ManualResetEventSlim(false);
        using var initializationQueued = new ManualResetEventSlim(false);
        using var hostStarted = new ManualResetEventSlim(false);
        using var releaseBlock = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(() =>
            {
                blockingStarted.Set();
                releaseBlock.Wait();
            }), DispatcherPriority.Send);

            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        blockingStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        try
        {
            SdkInspector.InitializationQueuedCallback = () => initializationQueued.Set();
            SdkInspector.BeforeAbortPendingInitializationCallback = () =>
            {
                releaseBlock.Set();
                hostStarted.Wait(TimeSpan.FromSeconds(5));
            };
            SdkInspector.HostStartedCallback = _ => hostStarted.Set();
            testContext.SetTransport(InspectorSdkTestContext.CreateAuthSecret(), certDirectory);

            var initializeTask = Task.Run(() =>
                SdkInspector.InvokeInitializeOnDispatcher(
                    dispatcher!,
                    processId: 12345,
                    timeout: TimeSpan.FromMilliseconds(100)));

            initializationQueued.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            var exception = await Assert.ThrowsAsync<TimeoutException>(() => initializeTask);
            exception.Message.Should().Contain("while completing WpfDevTools Inspector SDK initialization");

            dispatcher!.Invoke(() => { }, DispatcherPriority.Background);

            SdkInspector.IsInitialized.Should().BeFalse(
                "initialization that only starts at the timeout boundary should not publish initialized state later");
            InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();
        }
        finally
        {
            releaseBlock.Set();
            dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
            thread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
        }
    }

    [Fact]
    public void InvokeInitializeOnDispatcher_WhenDispatcherHasShutdown_ShouldFailImmediately()
    {
        Dispatcher? dispatcher = null;
        using var dispatcherReady = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcherReady.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        try
        {
            dispatcher!.BeginInvokeShutdown(DispatcherPriority.Normal);
            thread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();

            var timeout = TimeSpan.FromMilliseconds(500);
            var stopwatch = Stopwatch.StartNew();
            var act = () => SdkInspector.InvokeInitializeOnDispatcher(
                dispatcher,
                processId: 12345,
                timeout: timeout);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*dispatcher is shutting down*");
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
                "a dispatcher that is already shut down should fail before consuming the caller-provided timeout budget of {0}",
                timeout);
        }
        finally
        {
            if (thread.IsAlive)
            {
                dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
                thread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
            }
        }
    }

    [Fact]
    public async Task Initialize_WithBlockedApplicationDispatcher_ShouldExposeTimeoutAndAllowRetry()
    {
        using var testContext = new InspectorSdkTestContext();
        var certDirectory = testContext.CreateTemporaryDirectory("wpf-devtools-sdk-dispatcher-timeout");
        Dispatcher? dispatcher = null;
        using var dispatcherReady = new ManualResetEventSlim(false);
        using var blockingStarted = new ManualResetEventSlim(false);
        using var releaseBlock = new ManualResetEventSlim(false);

        var uiThread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(() =>
            {
                blockingStarted.Set();
                releaseBlock.Wait();
            }), DispatcherPriority.Send);

            dispatcherReady.Set();
            Dispatcher.Run();
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.IsBackground = true;
        uiThread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        blockingStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        try
        {
            SdkInspector.DispatcherResolver = () => dispatcher;
            testContext.SetTransport(InspectorSdkTestContext.CreateAuthSecret(), certDirectory);

            await Task.Run(() => SdkInspector.Initialize(processId: 12345));

            SdkInspector.IsInitialized.Should().BeFalse();
            SdkInspector.LastInitializationError.Should().BeOfType<TimeoutException>();
            InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();
            InspectorSdkTestContext.GetInspectorSdkAuthenticationManager().Should().BeNull();
            InspectorSdkTestContext.GetInspectorSdkCertificateManager().Should().BeNull();

            releaseBlock.Set();
            dispatcher!.Invoke(() => { }, DispatcherPriority.Background);

            SdkInspector.IsInitialized.Should().BeFalse("timed out dispatcher operations should be aborted instead of completing later");

            await Task.Run(() => SdkInspector.Initialize(processId: 12345));

            SdkInspector.LastInitializationError.Should().BeNull();
            SdkInspector.IsInitialized.Should().BeTrue();
            InspectorSdkTestContext.GetInspectorSdkHost().Should().NotBeNull();
        }
        finally
        {
            releaseBlock.Set();
            if (dispatcher != null)
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }

            uiThread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task InvokeInitializeOnDispatcher_WhenQueuedInitializationIsAbortedByShutdown_ShouldExposeLifecycleError()
    {
        using var testContext = new InspectorSdkTestContext();
        Dispatcher? dispatcher = null;
        using var dispatcherReady = new ManualResetEventSlim(false);
        using var blockingStarted = new ManualResetEventSlim(false);
        using var initializationQueued = new ManualResetEventSlim(false);
        using var releaseBlock = new ManualResetEventSlim(false);

        var uiThread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(() =>
            {
                blockingStarted.Set();
                releaseBlock.Wait();
            }), DispatcherPriority.Send);

            dispatcherReady.Set();
            Dispatcher.Run();
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.IsBackground = true;
        uiThread.Start();

        dispatcherReady.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        blockingStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        try
        {
            SdkInspector.InitializationQueuedCallback = () => initializationQueued.Set();

            var initializeTask = Task.Run(() =>
                SdkInspector.InvokeInitializeOnDispatcher(
                    dispatcher!,
                    processId: 12345,
                    timeout: TimeSpan.FromSeconds(1)));

            initializationQueued.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            dispatcher!.BeginInvokeShutdown(DispatcherPriority.Send);
            releaseBlock.Set();

            Func<Task> act = async () => await initializeTask;
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*dispatcher shut down before initialization could start*");

            SdkInspector.IsInitialized.Should().BeFalse("aborted queued initialization must not complete later in the background");
        }
        finally
        {
            releaseBlock.Set();
            if (uiThread.IsAlive)
            {
                dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
                uiThread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
            }
        }
    }
}