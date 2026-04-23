using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Inspector.Sdk;
using SdkInspector = WpfDevTools.Inspector.Sdk.InspectorSdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

[Collection("ProcessEnvironment")]
public sealed class InspectorSdkInitializationTests
{
    [Fact]
    public void Initialize_WithInvalidAuthenticationSecret_ShouldExposeInitializationError()
    {
        var originalAuthSecret = Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET");
        var originalCertDirectory = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-init-{Guid.NewGuid():N}");

        try
        {
            SdkInspector.Shutdown();
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", "not-base64");
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", certDirectory);

            SdkInspector.Initialize(processId: 12345);

            SdkInspector.IsInitialized.Should().BeFalse();
            SdkInspector.LastInitializationError.Should().NotBeNull();
            SdkInspector.LastInitializationError.Should().BeOfType<FormatException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", originalAuthSecret);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", originalCertDirectory);
            SdkInspector.Shutdown();
        }
    }

    [Fact]
    public void Initialize_WithInvalidCertificateDirectory_ShouldExposeInitializationError()
    {
        var originalAuthSecret = Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET");
        var originalCertDirectory = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR");
        var invalidCertDirectory = Path.GetTempFileName();
        var authSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        try
        {
            SdkInspector.Shutdown();
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", authSecret);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", invalidCertDirectory);

            SdkInspector.Initialize(processId: 12345);

            SdkInspector.IsInitialized.Should().BeFalse();
            SdkInspector.LastInitializationError.Should().NotBeNull();
            SdkInspector.LastInitializationError.Should().BeOfType<IOException>();
            SdkInspector.LastInitializationError!.Message.Should().Contain(invalidCertDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", originalAuthSecret);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", originalCertDirectory);
            SdkInspector.Shutdown();
            if (File.Exists(invalidCertDirectory))
            {
                File.Delete(invalidCertDirectory);
            }
        }
    }

    [Fact]
    public void Initialize_WithOnlyAuthenticationSecret_ShouldExposeClearInitializationError()
    {
        var originalAuthSecret = Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET");
        var originalCertDirectory = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR");
        var authSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        try
        {
            SdkInspector.Shutdown();
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", authSecret);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", null);

            SdkInspector.Initialize(processId: 12345);

            SdkInspector.IsInitialized.Should().BeFalse();
            SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
            SdkInspector.LastInitializationError!.Message.Should().Contain("set together");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", originalAuthSecret);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", originalCertDirectory);
            SdkInspector.Shutdown();
        }
    }

    [Fact]
    public void Initialize_WithOnlyCertificateDirectory_ShouldExposeClearInitializationError()
    {
        var originalAuthSecret = Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET");
        var originalCertDirectory = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-init-{Guid.NewGuid():N}");

        try
        {
            SdkInspector.Shutdown();
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", null);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", certDirectory);

            SdkInspector.Initialize(processId: 12345);

            SdkInspector.IsInitialized.Should().BeFalse();
            SdkInspector.LastInitializationError.Should().BeOfType<InvalidOperationException>();
            SdkInspector.LastInitializationError!.Message.Should().Contain("set together");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", originalAuthSecret);
            Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", originalCertDirectory);
            SdkInspector.Shutdown();
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
        }
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