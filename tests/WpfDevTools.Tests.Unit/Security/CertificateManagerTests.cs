using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using WpfDevTools.Shared.Security;
using Xunit;

namespace WpfDevTools.Tests.Unit.Security;

public class CertificateManagerTests : IDisposable
{
    private readonly string _tempDir;

    public CertificateManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WpfDevTools_Test_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void GetOrCreateCertificate_ShouldReturnValidCertificate()
    {
        // Arrange
        var manager = new CertificateManager(_tempDir);

        // Act
        using var cert = manager.GetOrCreateCertificate();

        // Assert
        cert.Should().NotBeNull();
        cert.HasPrivateKey.Should().BeTrue();
        cert.Subject.Should().Contain("CN=WpfDevTools-Inspector");
    }

    [Fact]
    public void GetOrCreateCertificate_CalledTwice_ShouldReturnSameCertificate()
    {
        // Arrange
        var manager = new CertificateManager(_tempDir);

        // Act
        using var cert1 = manager.GetOrCreateCertificate();
        using var cert2 = manager.GetOrCreateCertificate();

        // Assert
        cert1.Thumbprint.Should().Be(cert2.Thumbprint);
    }

    [Fact]
    public void GetOrCreateCertificate_ShouldPersistToFile()
    {
        // Arrange
        var manager = new CertificateManager(_tempDir);
        manager.GetOrCreateCertificate().Dispose();

        // Act - create a new manager pointing to same dir
        var manager2 = new CertificateManager(_tempDir);
        using var cert = manager2.GetOrCreateCertificate();

        // Assert
        cert.Should().NotBeNull();
        cert.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void GetOrCreateCertificate_ShouldHaveCorrectKeyUsage()
    {
        // Arrange
        var manager = new CertificateManager(_tempDir);

        // Act
        using var cert = manager.GetOrCreateCertificate();

        // Assert - should have key usage extension
        var keyUsageExt = cert.Extensions
            .OfType<X509KeyUsageExtension>()
            .FirstOrDefault();
        keyUsageExt.Should().NotBeNull();
        keyUsageExt!.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.DigitalSignature);
        keyUsageExt.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.KeyEncipherment);
    }

    [Fact]
    public void GetOrCreateCertificate_ShouldHaveServerAuthEku()
    {
        // Arrange
        var manager = new CertificateManager(_tempDir);

        // Act
        using var cert = manager.GetOrCreateCertificate();

        // Assert - should have server authentication EKU
        var ekuExt = cert.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();
        ekuExt.Should().NotBeNull();
        ekuExt!.EnhancedKeyUsages
            .Cast<Oid>()
            .Should().Contain(oid => oid.Value == "1.3.6.1.5.5.7.3.1",
                "certificate should have Server Authentication EKU");
    }

    [Fact]
    public void GetOrCreateCertificate_ShouldBeValidForAtLeast11Months()
    {
        // Arrange
        var manager = new CertificateManager(_tempDir);

        // Act
        using var cert = manager.GetOrCreateCertificate();

        // Assert - should be valid for approximately 1 year
        cert.NotBefore.Should().BeOnOrBefore(DateTime.UtcNow);
        cert.NotAfter.Should().BeOnOrAfter(DateTime.UtcNow.AddMonths(11));
    }

    [Fact]
    public void GetOrCreateCertificate_ShouldUseRsa2048()
    {
        // Arrange
        var manager = new CertificateManager(_tempDir);

        // Act
        using var cert = manager.GetOrCreateCertificate();

        // Assert
        using var rsa = cert.GetRSAPublicKey();
        rsa.Should().NotBeNull();
        rsa!.KeySize.Should().Be(2048);
    }

    [Fact]
    public void Constructor_WithNullPath_ShouldThrow()
    {
        // Act
        var act = () => new CertificateManager(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithEmptyPath_ShouldThrow()
    {
        // Act
        var act = () => new CertificateManager("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
