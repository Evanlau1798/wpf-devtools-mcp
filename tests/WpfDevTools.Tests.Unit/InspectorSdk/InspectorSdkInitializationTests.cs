using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Inspector.Sdk;
using SdkInspector = WpfDevTools.Inspector.Sdk.InspectorSdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

[Collection("ProcessEnvironment")]
public sealed class InspectorSdkInitializationTests : IDisposable
{
    private readonly InspectorSdkTestContext _testContext = new();

    public void Dispose()
    {
        _testContext.Dispose();
    }

    [Fact]
    public void Initialize_WithInvalidAuthenticationSecret_ShouldExposeInitializationError()
    {
        var certDirectory = _testContext.CreateTemporaryDirectory("wpf-devtools-sdk-init");

        _testContext.SetTransport("not-base64", certDirectory);
        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().NotBeNull();
        SdkInspector.LastInitializationError.Should().BeOfType<FormatException>();
    }

    [Fact]
    public void Initialize_WithInvalidCertificateDirectory_ShouldExposeInitializationError()
    {
        var invalidCertDirectory = _testContext.CreateTemporaryFile();

        _testContext.SetTransport(InspectorSdkTestContext.CreateAuthSecret(), invalidCertDirectory);
        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().NotBeNull();
        SdkInspector.LastInitializationError.Should().BeOfType<IOException>();
        SdkInspector.LastInitializationError!.Message.Should().Contain(invalidCertDirectory);
    }

    [Fact]
    public void Initialize_WithOnlyAuthenticationSecret_ShouldExposeClearInitializationError()
    {
        var authSecret = InspectorSdkTestContext.CreateAuthSecret();

        _testContext.SetTransport(authSecret, certDirectory: null);
        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
        SdkInspector.LastInitializationError!.Message.Should().Contain("set together");
    }

    [Fact]
    public void Initialize_WithOnlyCertificateDirectory_ShouldExposeClearInitializationError()
    {
        var certDirectory = _testContext.CreateTemporaryDirectory("wpf-devtools-sdk-init");

        _testContext.SetTransport(authSecret: null, certDirectory);
        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
        SdkInspector.LastInitializationError!.Message.Should().Contain("set together");
    }

    [Fact]
    public void Shutdown_ShouldDisposeHostResources()
    {
        SdkInspector.Shutdown();

        var host = new InspectorHost(processId: 12345);

        try
        {
            SetInspectorSdkState(host, isInitialized: true, isInitializing: 0);

            SdkInspector.Shutdown();

            SdkInspector.IsInitialized.Should().BeFalse();
            SdkInspector.LastShutdownError.Should().BeNull();
            GetInspectorSdkHost().Should().BeNull();
            host.IsDisposed.Should().BeTrue();
        }
        finally
        {
            SetInspectorSdkState(host: null, isInitialized: false, isInitializing: 0);
            if (!host.IsDisposed)
            {
                host.Dispose();
            }
        }
    }

    [Fact]
    public void Shutdown_WhenCleanupFails_ShouldExposeShutdownError()
    {
        var invalidHost = (InspectorHost)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(InspectorHost));

        try
        {
            SetInspectorSdkState(invalidHost, isInitialized: true, isInitializing: 0);

            SdkInspector.Shutdown();

            SdkInspector.IsInitialized.Should().BeFalse();
            SdkInspector.LastShutdownError.Should().NotBeNull();
            GetInspectorSdkHost().Should().BeNull();
        }
        finally
        {
            SetInspectorSdkState(host: null, isInitialized: false, isInitializing: 0);
        }
    }

    private static void SetInspectorSdkState(InspectorHost? host, bool isInitialized, int isInitializing)
    {
        typeof(SdkInspector)
            .GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, host);
        typeof(SdkInspector)
            .GetField("_authenticationManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
        typeof(SdkInspector)
            .GetField("_certificateManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
        typeof(SdkInspector)
            .GetField("_isInitialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, isInitialized);
        typeof(SdkInspector)
            .GetField("_isInitializing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, isInitializing);
    }

    private static InspectorHost? GetInspectorSdkHost()
    {
        return (InspectorHost?)typeof(SdkInspector)
            .GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null);
    }
}
