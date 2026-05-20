using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Inspector.Sdk;
using SdkInspector = WpfDevTools.Inspector.Sdk.InspectorSdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

[Collection("ProcessEnvironment")]
public sealed class InspectorSdkOptionsInitializationTests
{
    [Fact]
    public void Initialize_WithExplicitOptions_ShouldUseOptionsWithoutMutatingEnvironment()
    {
        using var testContext = new InspectorSdkTestContext();
        var certDirectory = testContext.CreateTemporaryDirectory("wpf-devtools-sdk-options");
        InspectorHost? startedHost = null;
        testContext.SetTransport(authSecret: null, certDirectory: null);

        SdkInspector.HostStartedCallback = host =>
        {
            startedHost = host;
            throw new InvalidOperationException("options reached host start");
        };

        SdkInspector.Initialize(new InspectorSdkOptions
        {
            ProcessId = 12345,
            AuthenticationSecretBase64 = InspectorSdkTestContext.CreateAuthSecret(),
            CertificateDirectory = certDirectory
        });

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
        SdkInspector.LastInitializationError!.Message.Should().Be("options reached host start");
        SdkInspector.LastInitializationStatus.ProcessId.Should().Be(12345);
        startedHost.Should().NotBeNull();
        startedHost!.IsDisposed.Should().BeTrue();
        Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET").Should().BeNull();
        Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR").Should().BeNull();
    }

    [Fact]
    public void Initialize_WithPartialExplicitOptions_ShouldRejectWithoutMixingEnvironmentFallback()
    {
        using var testContext = new InspectorSdkTestContext();
        var envCertDirectory = testContext.CreateTemporaryDirectory("wpf-devtools-sdk-env");
        testContext.SetTransport(InspectorSdkTestContext.CreateAuthSecret(), envCertDirectory);

        SdkInspector.Initialize(new InspectorSdkOptions
        {
            ProcessId = 12346,
            AuthenticationSecretBase64 = InspectorSdkTestContext.CreateAuthSecret()
        });

        SdkInspector.IsInitialized.Should().BeFalse();
        SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
        SdkInspector.LastInitializationError!.Message.Should().Contain("InspectorSdkOptions.AuthenticationSecretBase64");
        SdkInspector.LastInitializationError.Message.Should().Contain("InspectorSdkOptions.CertificateDirectory");
        SdkInspector.LastInitializationStatus.ProcessId.Should().Be(12346);
        InspectorSdkTestContext.GetInspectorSdkHost().Should().BeNull();
    }
}
