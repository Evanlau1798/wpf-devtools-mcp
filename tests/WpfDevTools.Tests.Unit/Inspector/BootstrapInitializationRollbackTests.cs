using System.Security.Cryptography;
using System.IO.Pipes;
using System.Reflection;
using System.Windows.Threading;
using FluentAssertions;
using WpfDevTools.Inspector;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector;

[Collection("BootstrapState")]
public sealed class BootstrapInitializationRollbackTests : IDisposable
{
    private InspectorHost? _startedHost;
    private AuthenticationManager? _authenticationManager;

    public BootstrapInitializationRollbackTests()
    {
        Bootstrap.ResetForTesting();
        Bootstrap.HostStartedCallback = host => _startedHost = host;
        Bootstrap.AuthenticationManagerCreatedCallback = manager => _authenticationManager = manager;
    }

    public void Dispose()
    {
        Bootstrap.ResetForTesting();

        try
        {
            _authenticationManager?.Dispose();
        }
        catch
        {
        }
    }

    [Fact]
    public void InitializeOnUiThread_WhenBindingTraceInstallFails_ShouldRollbackHostState()
    {
        Bootstrap.BindingErrorTraceListenerInstallAction = static () => throw new InvalidOperationException("binding install failed");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Action act = () => Bootstrap.InitializeOnUiThreadForTesting(
            $"pipeName=WpfDevTools_BootstrapRollback_{Guid.NewGuid():N};auth=enabled;authSecretBase64={secret}");

        act.Should().Throw<InvalidOperationException>().WithMessage("binding install failed");
        Bootstrap.IsInitialized.Should().BeFalse();
        Bootstrap.CurrentHostForTesting.Should().BeNull();
        _startedHost.Should().BeNull();
    }

    [Fact]
    public void InitializeOnUiThread_WhenBindingTraceInstallFails_ShouldDisposeAuthenticationManager()
    {
        Bootstrap.BindingErrorTraceListenerInstallAction = static () => throw new InvalidOperationException("binding install failed");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Action act = () => Bootstrap.InitializeOnUiThreadForTesting(
            $"pipeName=WpfDevTools_BootstrapRollback_{Guid.NewGuid():N};auth=enabled;authSecretBase64={secret}");

        act.Should().Throw<InvalidOperationException>().WithMessage("binding install failed");
        _authenticationManager.Should().NotBeNull();

        Action getSecret = () => _authenticationManager!.GetSharedSecret();
        getSecret.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void InitializeOnUiThread_WhenCertificateManagerCreationFails_ShouldDisposeAuthenticationManager()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Action act = () => Bootstrap.InitializeOnUiThreadForTesting(
            $"pipeName=WpfDevTools_BootstrapRollback_{Guid.NewGuid():N};auth=enabled;authSecretBase64={secret};encryption=enabled;certDirectory=\\\\server\\share\\certs");

        act.Should().Throw<ArgumentException>();
        Bootstrap.CurrentHostForTesting.Should().BeNull();
        _authenticationManager.Should().NotBeNull();

        Action getSecret = () => _authenticationManager!.GetSharedSecret();
        getSecret.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void InitializeOnUiThread_WhenBindingTraceInstallFailsAfterPartialInstall_ShouldInvokeUninstall()
    {
        var uninstallCalls = 0;
        Bootstrap.BindingErrorTraceListenerInstallAction = static () => throw new InvalidOperationException("binding install failed");
        Bootstrap.BindingErrorTraceListenerUninstallAction = () => uninstallCalls++;
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Action act = () => Bootstrap.InitializeOnUiThreadForTesting(
            $"pipeName=WpfDevTools_BootstrapRollback_{Guid.NewGuid():N};auth=enabled;authSecretBase64={secret}");

        act.Should().Throw<InvalidOperationException>().WithMessage("binding install failed");
        uninstallCalls.Should().Be(1);
    }

    [Fact]
    public void InitializeOnUiThread_WhenHostTransportStartupFails_ShouldDisposeAuthenticationManager()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Bootstrap.HostFactory = static (processId, pipeName, authManager, certManager) => new InspectorHost(
            processId,
            pipeName,
            authManager,
            certManager,
            FileLogLevel.Warning,
            pipeServerFactory: () => throw new IOException("pipe create failed"),
            startupTimeout: default);

        Action act = () => Bootstrap.InitializeOnUiThreadForTesting(
            $"pipeName=WpfDevTools_BootstrapRollback_{Guid.NewGuid():N};auth=enabled;authSecretBase64={secret}");

        act.Should().Throw<IOException>().WithMessage("pipe create failed");
        Bootstrap.CurrentHostForTesting.Should().BeNull();
        _startedHost.Should().BeNull();
        _authenticationManager.Should().NotBeNull();

        Action getSecret = () => _authenticationManager!.GetSharedSecret();
        getSecret.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Initialize_WhenHostStartupTimesOutThenPipeCreationSucceeds_ShouldNotPublishInitializedState()
    {
        using var dispatcherThread = new DispatcherThreadContext();
        var pipeFactoryEntered = new ManualResetEventSlim(initialState: false);
        var releasePipeFactory = new ManualResetEventSlim(initialState: false);
        var bindingInstalled = false;
        var uninstallCalls = 0;

        Bootstrap.DispatcherResolver = () => dispatcherThread.Dispatcher;
        Bootstrap.BindingErrorTraceListenerInstallAction = () => bindingInstalled = true;
        Bootstrap.BindingErrorTraceListenerUninstallAction = () => uninstallCalls++;
        Bootstrap.HostFactory = (processId, pipeName, authManager, certManager) => new InspectorHost(
            processId,
            pipeName,
            authManager,
            certManager,
            FileLogLevel.Warning,
            pipeServerFactory: () =>
            {
                pipeFactoryEntered.Set();
                releasePipeFactory.Wait(TimeSpan.FromSeconds(5));
                return new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            },
            startupTimeout: TimeSpan.FromMilliseconds(200));

        try
        {
            Bootstrap.Initialize($"pipeName=WpfDevTools_BootstrapTimeout_{Guid.NewGuid():N}");

            pipeFactoryEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            await Task.Delay(300);
            releasePipeFactory.Set();

            SpinWait.SpinUntil(static () => GetBootstrapIsInitializing() == 0, 5_000)
                .Should().BeTrue();

            bindingInstalled.Should().BeTrue();
            uninstallCalls.Should().Be(1);
            Bootstrap.IsInitialized.Should().BeFalse();
            Bootstrap.CurrentHostForTesting.Should().BeNull();
        }
        finally
        {
            releasePipeFactory.Set();
        }
    }

    [Fact]
    public void Initialize_WhenDispatcherFinalizeTimesOut_ShouldRollbackHostState()
    {
        using var dispatcherThread = new DispatcherThreadContext();
        var pipeName = $"WpfDevTools_DispatcherTimeout_{Guid.NewGuid():N}";
        var releaseDispatcher = new ManualResetEventSlim(initialState: false);
        var bindingInstalled = false;

        Bootstrap.DispatcherResolver = () => dispatcherThread.Dispatcher;
        Bootstrap.DispatcherFinalizeTimeout = TimeSpan.FromMilliseconds(200);
        Bootstrap.BindingErrorTraceListenerInstallAction = () => bindingInstalled = true;

        dispatcherThread.Dispatcher.BeginInvoke(
            new Action(() => releaseDispatcher.Wait(TimeSpan.FromSeconds(5))),
            DispatcherPriority.Normal);

        Bootstrap.Initialize($"pipeName={pipeName}");

        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            Action connect = () => client.Connect(250);
            connect.Should().Throw<TimeoutException>();

            SpinWait.SpinUntil(static () => GetBootstrapIsInitializing() == 0, 5_000).Should().BeTrue();

            bindingInstalled.Should().BeFalse();
            Bootstrap.IsInitialized.Should().BeFalse();
            Bootstrap.CurrentHostForTesting.Should().BeNull();
            _startedHost.Should().BeNull();
        }
        finally
        {
            releaseDispatcher.Set();
        }
    }

    [Fact]
    public void Initialize_WhenInstallActionOutlivesDispatcherFinalizeTimeout_ShouldNotLatePublishState()
    {
        using var dispatcherThread = new DispatcherThreadContext();
        var installEntered = new ManualResetEventSlim(initialState: false);
        var releaseInstall = new ManualResetEventSlim(initialState: false);

        Bootstrap.DispatcherResolver = () => dispatcherThread.Dispatcher;
        Bootstrap.DispatcherFinalizeTimeout = TimeSpan.FromMilliseconds(200);
        Bootstrap.HostStartAction = _ => { };
        Bootstrap.BindingErrorTraceListenerInstallAction = () =>
        {
            installEntered.Set();
            releaseInstall.Wait(TimeSpan.FromSeconds(5));
        };

        Bootstrap.Initialize($"pipeName=WpfDevTools_InstallTimeout_{Guid.NewGuid():N}");

        try
        {
            installEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            SpinWait.SpinUntil(static () => GetBootstrapIsInitializing() == 0, 5_000).Should().BeTrue();

            Bootstrap.IsInitialized.Should().BeFalse();
            Bootstrap.CurrentHostForTesting.Should().BeNull();

            releaseInstall.Set();
            SpinWait.SpinUntil(static () => GetBootstrapIsInitializing() == 0, 2_000).Should().BeTrue();

            Bootstrap.IsInitialized.Should().BeFalse();
            Bootstrap.CurrentHostForTesting.Should().BeNull();
        }
        finally
        {
            releaseInstall.Set();
        }
    }

    private static int GetBootstrapIsInitializing()
    {
        var field = typeof(Bootstrap).GetField("_isInitializing", BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        return (int)field!.GetValue(null)!;
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