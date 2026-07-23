using FluentAssertions;
using SdkInspector = WpfDevTools.Inspector.Sdk.InspectorSdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

[Collection("ProcessEnvironment")]
public sealed class InspectorSdkInitializationConfigurationTests
{
    [Fact]
    public void Initialize_WithoutTransportOverrides_ShouldExposeFailClosedInitializationError()
    {
        using var testContext = new InspectorSdkTestContext();

        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
        SdkInspector.LastInitializationError!.Message.Should().Contain("WPFDEVTOOLS_AUTH_SECRET");
        SdkInspector.LastInitializationError!.Message.Should().Contain("WPFDEVTOOLS_CERT_DIR");
    }

}
