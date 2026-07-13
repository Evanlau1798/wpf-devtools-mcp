using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Inspector.Sdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

public sealed class InspectorSdkTransportSecurityConfigurationTests
{
    [Fact]
    public void Create_WithoutEnvironmentOverrides_ShouldFailClosed()
    {
        var act = () => InspectorSdkTransportSecurityConfiguration.Create(null, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_AUTH_SECRET*WPFDEVTOOLS_CERT_DIR*InspectorSdk.Initialize()*");
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
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
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
            .WithMessage("*WPFDEVTOOLS_AUTH_SECRET*WPFDEVTOOLS_CERT_DIR*Partial SDK transport configuration is not supported*");
    }

    [Fact]
    public void Create_WithCertificateDirectoryButNoAuthenticationSecret_ShouldThrowClearError()
    {
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-certs-{Guid.NewGuid():N}");

        var act = () => InspectorSdkTransportSecurityConfiguration.Create(null, certDirectory);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_AUTH_SECRET*WPFDEVTOOLS_CERT_DIR*Partial SDK transport configuration is not supported*");
    }

    [Fact]
    public void Create_WithInvalidAuthenticationSecret_ShouldThrowFormatException()
    {
        var certDirectory = Path.Combine(
            Path.GetTempPath(),
            $"wpf-devtools-sdk-invalid-auth-{Guid.NewGuid():N}");

        try
        {
            var act = () => InspectorSdkTransportSecurityConfiguration.Create("not-base64", certDirectory);

            act.Should().Throw<FormatException>();
            Directory.Exists(certDirectory).Should().BeFalse(
                "authentication must be validated before certificate storage is touched");
        }
        finally
        {
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
        }
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

    [Theory]
    [InlineData(@"\\server\share\wpfdevtools-certs")]
    [InlineData(@"\\?\UNC\server\share\wpfdevtools-certs")]
    public void Create_WithNetworkCertificateDirectory_ShouldThrowLocalPathError(string certificateDirectory)
    {
        var authSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var act = () => InspectorSdkTransportSecurityConfiguration.Create(authSecret, certificateDirectory);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_CERT_DIR*local path*Network paths are not allowed*");
    }

    private static string GetDriveRelativePath(string pathSuffix)
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        return root[..2] + pathSuffix;
    }
}
