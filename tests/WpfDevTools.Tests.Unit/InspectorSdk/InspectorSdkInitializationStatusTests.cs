using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Security;
using SdkInspector = WpfDevTools.Inspector.Sdk.InspectorSdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

[Collection("ProcessEnvironment")]
public sealed class InspectorSdkInitializationStatusTests
{
    [Fact]
    public void Initialize_WithoutTransportOverrides_ShouldExposeStructuredFailureStatus()
    {
        using var testContext = new InspectorSdkTestContext();

        SdkInspector.Initialize(processId: 12345);

        var status = SdkInspector.LastInitializationStatus;
        status.State.Should().Be("Failed");
        status.IsInitialized.Should().BeFalse();
        status.ProcessId.Should().Be(12345);
        status.ErrorCode.Should().Be("SdkTransportConfigurationInvalid");
        status.ErrorType.Should().Be(nameof(InvalidOperationException));
        status.ErrorMessage.Should().Contain("WPFDEVTOOLS_AUTH_SECRET");
        status.ErrorMessage.Should().Contain("WPFDEVTOOLS_CERT_DIR");
        status.Hint.Should().Contain("set both");
    }

    [Fact]
    public void Initialize_WhenAlreadyInitialized_ShouldKeepExistingHostStatus()
    {
        using var testContext = new InspectorSdkTestContext();
        AuthenticationManager? authenticationManager = new(() => InspectorSdkTestContext.CreateAuthSecret());
        CertificateManager? certificateManager = new(testContext.CreateTemporaryDirectory("wpf-devtools-sdk-status"));
        InspectorHost? host = new(12345, authenticationManager, certificateManager);

        try
        {
            InspectorSdkTestContext.SetInspectorSdkState(
                host,
                authenticationManager,
                certificateManager,
                isInitialized: false,
                isInitializing: 0);
            var published = InspectorSdkTestContext.TryPublishInitializedHostForTesting(
                12345,
                ref host,
                ref authenticationManager,
                ref certificateManager);
            published.Should().BeTrue();

            var publishedStatus = SdkInspector.LastInitializationStatus;

            SdkInspector.Initialize(processId: 54321);

            SdkInspector.LastInitializationStatus.Should().BeSameAs(publishedStatus);
            SdkInspector.LastInitializationStatus.ProcessId.Should().Be(12345);
            SdkInspector.LastInitializationStatus.IsInitialized.Should().BeTrue();
        }
        finally
        {
            InspectorSdkTestContext.SetInspectorSdkState(
                host: null,
                authenticationManager: null,
                certificateManager: null,
                isInitialized: false,
                isInitializing: 0);
            if (host is { IsDisposed: false })
            {
                host.Dispose();
            }

            authenticationManager?.Dispose();
        }
    }
}
