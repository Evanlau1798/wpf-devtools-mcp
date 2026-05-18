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

        SdkInspector.Shutdown();
        releaseInitialization.Set();
        await initializeTask.WaitAsync(TimeSpan.FromSeconds(10));

        SdkInspector.IsInitialized.Should().BeFalse(
            "shutdown requested during initialization must prevent the started host from being published afterward");
        InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();
        startedHost.Should().NotBeNull();
        startedHost!.IsDisposed.Should().BeTrue();
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
