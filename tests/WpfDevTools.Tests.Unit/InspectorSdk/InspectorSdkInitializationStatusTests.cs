using FluentAssertions;
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
}
