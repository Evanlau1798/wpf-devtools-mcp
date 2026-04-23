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

    [Fact]
    public void Initialize_WithInvalidAuthenticationSecret_ShouldExposeInitializationError()
    {
        using var testContext = new InspectorSdkTestContext();
        var certDirectory = testContext.CreateTemporaryDirectory("wpf-devtools-sdk-init");

        testContext.SetTransport("not-base64", certDirectory);

        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<FormatException>();
    }

    [Fact]
    public void Initialize_WithInvalidCertificateDirectory_ShouldExposeInitializationError()
    {
        using var testContext = new InspectorSdkTestContext();
        var invalidCertDirectory = testContext.CreateTemporaryFile();

        testContext.SetTransport(InspectorSdkTestContext.CreateAuthSecret(), invalidCertDirectory);

        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<IOException>();
        SdkInspector.LastInitializationError!.Message.Should().Contain(invalidCertDirectory);
    }

    [Fact]
    public void Initialize_WithOnlyAuthenticationSecret_ShouldExposeClearInitializationError()
    {
        using var testContext = new InspectorSdkTestContext();

        testContext.SetTransport(InspectorSdkTestContext.CreateAuthSecret(), certDirectory: null);

        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
        SdkInspector.LastInitializationError!.Message.Should().Contain("set together");
    }

    [Fact]
    public void Initialize_WithOnlyCertificateDirectory_ShouldExposeClearInitializationError()
    {
        using var testContext = new InspectorSdkTestContext();
        var certDirectory = testContext.CreateTemporaryDirectory("wpf-devtools-sdk-init");

        testContext.SetTransport(authSecret: null, certDirectory);

        SdkInspector.Initialize(processId: 12345);

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
        SdkInspector.LastInitializationError!.Message.Should().Contain("set together");
    }
}