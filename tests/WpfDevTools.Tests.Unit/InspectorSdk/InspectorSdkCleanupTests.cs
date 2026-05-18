using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Security;
using SdkInspector = WpfDevTools.Inspector.Sdk.InspectorSdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

[Collection("ProcessEnvironment")]
public sealed class InspectorSdkCleanupTests
{
    [Fact]
    public void Shutdown_ShouldDisposeHostResources()
    {
        using var testContext = new InspectorSdkTestContext();
        var host = new InspectorHost(processId: 12345);
        using var authenticationManager = new AuthenticationManager(() => InspectorSdkTestContext.CreateAuthSecret());
        var certificateManager = new CertificateManager(testContext.CreateTemporaryDirectory("wpf-devtools-sdk-shutdown"));

        try
        {
            InspectorSdkTestContext.SetInspectorSdkState(host, authenticationManager, certificateManager, isInitialized: true, isInitializing: 0);

            SdkInspector.Shutdown();

            SdkInspector.IsInitialized.Should().BeFalse();
            SdkInspector.LastShutdownError.Should().BeNull();
            InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();
            InspectorSdkTestContext.GetInspectorSdkAuthenticationManager().Should().BeNull();
            InspectorSdkTestContext.GetInspectorSdkCertificateManager().Should().BeNull();
            host.IsDisposed.Should().BeTrue();

            Action getSecret = () => authenticationManager.GetSharedSecret();
            getSecret.Should().Throw<ObjectDisposedException>();
        }
        finally
        {
            InspectorSdkTestContext.SetInspectorSdkState(host: null, authenticationManager: null, certificateManager: null, isInitialized: false, isInitializing: 0);
            if (!host.IsDisposed)
            {
                host.Dispose();
            }
        }
    }

    [Fact]
    public void Initialize_WhenPostStartInitializationFails_ShouldDisposeHostAndPreserveOriginalError()
    {
        using var testContext = new InspectorSdkTestContext();
        var certDirectory = testContext.CreateTemporaryDirectory("wpf-devtools-sdk-post-start");
        InspectorHost? startedHost = null;

        SdkInspector.HostStartedCallback = host =>
        {
            startedHost = host;
            throw new InvalidOperationException("Simulated post-start initialization failure");
        };
        testContext.SetTransport(InspectorSdkTestContext.CreateAuthSecret(), certDirectory);

        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
        SdkInspector.LastInitializationError!.Message.Should().Be("Simulated post-start initialization failure");
        InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();
        InspectorSdkTestContext.GetInspectorSdkAuthenticationManager().Should().BeNull();
        InspectorSdkTestContext.GetInspectorSdkCertificateManager().Should().BeNull();
        startedHost.Should().NotBeNull();
        startedHost!.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Shutdown_DuringInFlightInitialization_ShouldDisposeStartedHostBeforePublish()
    {
        using var testContext = new InspectorSdkTestContext();
        var certDirectory = testContext.CreateTemporaryDirectory("wpf-devtools-sdk-shutdown-during-init");
        using var hostStarted = new ManualResetEventSlim(false);
        using var releaseInitialization = new ManualResetEventSlim(false);
        InspectorHost? startedHost = null;

        SdkInspector.HostStartedCallback = host =>
        {
            startedHost = host;
            hostStarted.Set();
            releaseInitialization.Wait();
        };
        testContext.SetTransport(InspectorSdkTestContext.CreateAuthSecret(), certDirectory);

        var initializeTask = Task.Run(() => SdkInspector.Initialize(processId: 12346));
        hostStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        try
        {
            SdkInspector.Shutdown();
        }
        finally
        {
            releaseInitialization.Set();
            await initializeTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        SdkInspector.IsInitialized.Should().BeFalse(
            "shutdown requested during initialization must prevent the started host from being published afterward");
        InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();
        startedHost.Should().NotBeNull();
        startedHost!.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Shutdown_DuringFailedInitialization_ShouldPreserveInitializationError()
    {
        using var testContext = new InspectorSdkTestContext();
        var initializationError = new InvalidOperationException("simulated initialization failure");
        InspectorSdkTestContext.SetInspectorSdkErrorState(initializationError, shutdownError: null, isInitializing: 1);

        try
        {
            SdkInspector.Shutdown();

            SdkInspector.LastInitializationError.Should().BeSameAs(initializationError,
                "deferred shutdown requests must not erase the initialization failure reported by the initializing thread");
        }
        finally
        {
            InspectorSdkTestContext.SetInspectorSdkState(
                host: null,
                authenticationManager: null,
                certificateManager: null,
                isInitialized: false,
                isInitializing: 0);
        }
    }

    [Fact]
    public void CompleteInitializationIfShutdownRequested_ShouldNotPublishAfterFinalGate()
    {
        using var testContext = new InspectorSdkTestContext();
        var host = new InspectorHost(processId: 12347);
        AuthenticationManager? authenticationManager = new(() => InspectorSdkTestContext.CreateAuthSecret());
        var certificateManager = new CertificateManager(testContext.CreateTemporaryDirectory("wpf-devtools-sdk-final-gate"));

        try
        {
            InspectorSdkTestContext.SetInspectorSdkState(
                host: null,
                authenticationManager: null,
                certificateManager: null,
                isInitialized: false,
                isInitializing: 1);
            SdkInspector.Shutdown();

            var completed = InspectorSdkTestContext.CompleteInitializationIfShutdownRequestedForTesting(
                ref host,
                ref authenticationManager,
                ref certificateManager);

            completed.Should().BeTrue();
            host.Should().BeNull();
            authenticationManager.Should().BeNull();
            certificateManager.Should().BeNull();
            SdkInspector.IsInitialized.Should().BeFalse();
        }
        finally
        {
            InspectorSdkTestContext.SetInspectorSdkState(
                host: null,
                authenticationManager: null,
                certificateManager: null,
                isInitialized: false,
                isInitializing: 0);
            host?.Dispose();
            authenticationManager?.Dispose();
        }
    }

    [Fact]
    public void TryPublishInitializedHost_WhenShutdownAlreadyRequested_ShouldRejectPublication()
    {
        using var testContext = new InspectorSdkTestContext();
        var host = new InspectorHost(processId: 12348);
        var originalHost = host;
        AuthenticationManager? authenticationManager = new(() => InspectorSdkTestContext.CreateAuthSecret());
        var certificateManager = new CertificateManager(testContext.CreateTemporaryDirectory("wpf-devtools-sdk-publish-gate"));

        try
        {
            InspectorSdkTestContext.SetInspectorSdkState(
                host: null,
                authenticationManager: null,
                certificateManager: null,
                isInitialized: false,
                isInitializing: 1);
            SdkInspector.Shutdown();

            var published = InspectorSdkTestContext.TryPublishInitializedHostForTesting(
                processId: 12348,
                ref host,
                ref authenticationManager,
                ref certificateManager);

            published.Should().BeFalse();
            SdkInspector.IsInitialized.Should().BeFalse();
            InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();

            var completed = InspectorSdkTestContext.CompleteInitializationIfShutdownRequestedForTesting(
                ref host,
                ref authenticationManager,
                ref certificateManager);

            completed.Should().BeTrue();
            host.Should().BeNull();
            authenticationManager.Should().BeNull();
            certificateManager.Should().BeNull();
            originalHost.IsDisposed.Should().BeTrue();
        }
        finally
        {
            InspectorSdkTestContext.SetInspectorSdkState(
                host: null,
                authenticationManager: null,
                certificateManager: null,
                isInitialized: false,
                isInitializing: 0);
            host?.Dispose();
            authenticationManager?.Dispose();
        }
    }

    [Fact]
    public void CleanupHostResources_WhenHostDisposeFails_ShouldStillDisposeAuthenticationManager()
    {
        using var testContext = new InspectorSdkTestContext();
        var invalidHost = (InspectorHost)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(InspectorHost));
        using var authenticationManager = new AuthenticationManager(() => InspectorSdkTestContext.CreateAuthSecret());

        Action cleanup = () => SdkInspector.CleanupHostResources(invalidHost, authenticationManager);

        cleanup.Should().Throw<NullReferenceException>();

        Action getSecret = () => authenticationManager.GetSharedSecret();
        getSecret.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Shutdown_WhenCleanupFails_ShouldExposeShutdownError()
    {
        using var testContext = new InspectorSdkTestContext();
        var invalidHost = (InspectorHost)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(InspectorHost));

        InspectorSdkTestContext.SetInspectorSdkState(
            invalidHost,
            authenticationManager: null,
            certificateManager: null,
            isInitialized: true,
            isInitializing: 0);

        try
        {
            SdkInspector.Shutdown();

            SdkInspector.IsInitialized.Should().BeFalse();
            SdkInspector.LastShutdownError.Should().NotBeNull();
            InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();
        }
        finally
        {
            InspectorSdkTestContext.SetInspectorSdkState(
                host: null,
                authenticationManager: null,
                certificateManager: null,
                isInitialized: false,
                isInitializing: 0);
        }
    }
}
