using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.AccessControl;
using System.Security.Principal;
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
    public async Task GetOrCreateCertificate_CalledConcurrently_ShouldPersistReusableArtifacts()
    {
        var firstManager = new CertificateManager(_tempDir);
        var secondManager = new CertificateManager(_tempDir);

        var certificates = await Task.WhenAll(
            Task.Run(() => firstManager.GetOrCreateCertificate()),
            Task.Run(() => secondManager.GetOrCreateCertificate()),
            Task.Run(() => firstManager.GetOrCreateCertificate()),
            Task.Run(() => secondManager.GetOrCreateCertificate()));

        try
        {
            certificates.Select(certificate => certificate.Thumbprint)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Should().ContainSingle();
            File.Exists(Path.Combine(_tempDir, "server.pfx")).Should().BeTrue();
            File.Exists(Path.Combine(_tempDir, "server.pwd")).Should().BeTrue();
        }
        finally
        {
            foreach (var certificate in certificates)
            {
                certificate.Dispose();
            }
        }
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

    [Fact]
    public void Constructor_WithUncPath_ShouldThrow()
    {
        var act = () => new CertificateManager(@"\\server\share\wpfdevtools-certs");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*local path*");
    }

    [Fact]
    public void Constructor_WithExtendedLocalPath_ShouldNormalizeAndAccept()
    {
        var localPath = Path.Combine(Path.GetTempPath(), "WpfDevTools_Test_" + Guid.NewGuid());
        var extendedLocalPath = @"\\?\" + localPath;

        var manager = new CertificateManager(extendedLocalPath);

        manager.CertificateDirectory.Should().Be(Path.GetFullPath(localPath));
    }

    [Fact]
    public void GetOrCreateCertificate_ShouldRemoveBroadWriteAccessFromDirectoryAndFiles()
    {
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        GrantEveryoneModifyAccess(_tempDir, everyoneSid);
        var manager = new CertificateManager(_tempDir);

        using var cert = manager.GetOrCreateCertificate();

        HasBroadWriteAccess(_tempDir, everyoneSid).Should().BeFalse();
        HasBroadWriteAccess(Path.Combine(_tempDir, "server.pfx"), everyoneSid).Should().BeFalse();
        HasBroadWriteAccess(Path.Combine(_tempDir, "server.pwd"), everyoneSid).Should().BeFalse();
    }

    private static void GrantEveryoneModifyAccess(string path, SecurityIdentifier everyoneSid)
    {
        var directoryInfo = new DirectoryInfo(path);
        var directorySecurity = directoryInfo.GetAccessControl();
        directorySecurity.AddAccessRule(new FileSystemAccessRule(
            everyoneSid,
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        directoryInfo.SetAccessControl(directorySecurity);
    }

    private static bool HasBroadWriteAccess(string path, SecurityIdentifier everyoneSid)
    {
        var accessRules = File.GetAttributes(path).HasFlag(FileAttributes.Directory)
            ? new DirectoryInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
            : new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));

        return accessRules
            .OfType<FileSystemAccessRule>()
            .Any(rule =>
                rule.AccessControlType == AccessControlType.Allow
                && Equals(rule.IdentityReference, everyoneSid)
                && (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl)) != 0);
    }
}
