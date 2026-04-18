using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Inspector.Sdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

public sealed class InspectorSdkTransportSecurityConfigurationTests
{
    [Fact]
    public void Create_WithoutEnvironmentOverrides_ShouldLeaveSdkTransportUnhardened()
    {
        var configuration = InspectorSdkTransportSecurityConfiguration.Create(null, null);

        configuration.AuthenticationManager.Should().BeNull();
        configuration.CertificateManager.Should().BeNull();
        configuration.IsAuthenticationEnabled.Should().BeFalse();
        configuration.IsEncryptionEnabled.Should().BeFalse();
    }

    [Fact]
    public void Create_WithEnvironmentOverrides_ShouldEnableSdkTransportHardening()
    {
        var expectedSecret = new byte[32];
        RandomNumberGenerator.Fill(expectedSecret);
        var authSecret = Convert.ToBase64String(expectedSecret);
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-certs-{Guid.NewGuid():N}");

        var configuration = InspectorSdkTransportSecurityConfiguration.Create(authSecret, certDirectory);

        try
        {
            configuration.IsAuthenticationEnabled.Should().BeTrue();
            configuration.IsEncryptionEnabled.Should().BeTrue();
            configuration.AuthenticationManager.Should().NotBeNull();
            configuration.CertificateManager.Should().NotBeNull();
            configuration.AuthenticationManager!.GetSharedSecret().Should().Equal(expectedSecret);
            configuration.CertificateManager!.CertificateDirectory.Should().Be(certDirectory);
        }
        finally
        {
            configuration.AuthenticationManager?.Dispose();
        }
    }

    [Fact]
    public void Create_WithAuthenticationSecretButNoCertificateDirectory_ShouldThrowClearError()
    {
        var expectedSecret = new byte[32];
        RandomNumberGenerator.Fill(expectedSecret);
        var authSecret = Convert.ToBase64String(expectedSecret);

        var act = () => InspectorSdkTransportSecurityConfiguration.Create(authSecret, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_AUTH_SECRET*WPFDEVTOOLS_CERT_DIR*set together*");
    }

    [Fact]
    public void Create_WithCertificateDirectoryButNoAuthenticationSecret_ShouldThrowClearError()
    {
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-certs-{Guid.NewGuid():N}");

        var act = () => InspectorSdkTransportSecurityConfiguration.Create(null, certDirectory);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_AUTH_SECRET*WPFDEVTOOLS_CERT_DIR*set together*");
    }

    [Fact]
    public void Create_WithInvalidAuthenticationSecret_ShouldThrowFormatException()
    {
        var act = () => InspectorSdkTransportSecurityConfiguration.Create("not-base64", Path.GetTempPath());

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Create_WithRelativeCertificateDirectory_ShouldThrowClearError()
    {
        var authSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var act = () => InspectorSdkTransportSecurityConfiguration.Create(authSecret, Path.Combine("relative", "certs"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_CERT_DIR*absolute path*");
    }

    [Fact]
    public void Create_WithDriveRelativeCertificateDirectory_ShouldThrowClearError()
    {
        var authSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var act = () => InspectorSdkTransportSecurityConfiguration.Create(authSecret, GetDriveRelativePath("certs"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_CERT_DIR*absolute path*");
    }

    private static string GetDriveRelativePath(string pathSuffix)
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        return root[..2] + pathSuffix;
    }
}