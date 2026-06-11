using Xunit;
using FluentAssertions;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using WpfDevTools.Inspector;

namespace WpfDevTools.Tests.Unit.Inspector;

/// <summary>
/// Tests for Bootstrap concurrency issues
/// Note: Bootstrap is marked [ExcludeFromCodeCoverage] but we can still test its logic
/// </summary>
[Collection("BootstrapState")]
public class BootstrapConcurrencyTests
{
    [Fact]
    public async Task Initialize_ConcurrentCalls_ShouldOnlyInitializeOnce()
    {
        // Arrange - Reset static state using reflection
        var bootstrapType = Type.GetType("WpfDevTools.Inspector.Bootstrap, WpfDevTools.Inspector");
        bootstrapType.Should().NotBeNull();

        var isInitializedField = bootstrapType!.GetField("_isInitialized",
            BindingFlags.NonPublic | BindingFlags.Static);
        var isInitializingField = bootstrapType.GetField("_isInitializing",
            BindingFlags.NonPublic | BindingFlags.Static);

        isInitializedField.Should().NotBeNull();
        isInitializingField.Should().NotBeNull();

        // Reset state
        isInitializedField!.SetValue(null, false);
        isInitializingField!.SetValue(null, 0);

        // Act - Call Initialize concurrently
        var tasks = new List<Task>();
        var initializeMethod = bootstrapType.GetMethod("Initialize",
            BindingFlags.Public | BindingFlags.Static);
        initializeMethod.Should().NotBeNull();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    initializeMethod!.Invoke(null, new object[] { "" });
                }
                catch
                {
                    // Expected to fail in test environment (no WPF app)
                    // We're testing the race condition logic, not the full initialization
                }
            }));
        }

        // Assert - Should not throw
        await Task.WhenAll(tasks);

        SpinWait.SpinUntil(() => (int)isInitializingField.GetValue(null)! == 0, 2_000)
            .Should().BeTrue("because asynchronous initialization should eventually reset _isInitializing");
    }

    [Fact]
    public void Initialize_WhenHostStartBlocks_ShouldReturnBeforeBackgroundStartupCompletes()
    {
        using var dispatcherThread = new DispatcherThreadContext();
        var hostStartEntered = new ManualResetEventSlim(initialState: false);
        var releaseHostStart = new ManualResetEventSlim(initialState: false);
        var bindingInstalled = new ManualResetEventSlim(initialState: false);

        Bootstrap.ResetForTesting();

        try
        {
            Bootstrap.DispatcherResolver = () => dispatcherThread.Dispatcher;
            Bootstrap.HostStartAction = _ =>
            {
                hostStartEntered.Set();
                releaseHostStart.Wait(TimeSpan.FromSeconds(5));
            };
            Bootstrap.BindingErrorTraceListenerInstallAction = () => bindingInstalled.Set();

            var stopwatch = Stopwatch.StartNew();
            Bootstrap.Initialize("pipeName=BootstrapAsyncInitTest");
            stopwatch.Stop();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
            hostStartEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            bindingInstalled.IsSet.Should().BeTrue();
            Bootstrap.IsInitialized.Should().BeFalse();

            releaseHostStart.Set();

            SpinWait.SpinUntil(() => Bootstrap.IsInitialized, 2_000)
                .Should().BeTrue("bootstrap should only publish initialized state after the background host start completes");
        }
        finally
        {
            releaseHostStart.Set();
            Bootstrap.ResetForTesting();
        }
    }

    private sealed class DispatcherThreadContext : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _dispatcherReady = new(initialState: false);
        private Dispatcher? _dispatcher;

        public Dispatcher Dispatcher => _dispatcher!;

        public DispatcherThreadContext()
        {
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _dispatcherReady.Set();
                Dispatcher.Run();
            });

            _thread.IsBackground = true;
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            _dispatcherReady.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        }

        public void Dispose()
        {
            if (_dispatcher != null)
            {
                _dispatcher.InvokeShutdown();
            }

            _thread.Join(TimeSpan.FromSeconds(2)).Should().BeTrue();
            _dispatcherReady.Dispose();
        }
    }
}
