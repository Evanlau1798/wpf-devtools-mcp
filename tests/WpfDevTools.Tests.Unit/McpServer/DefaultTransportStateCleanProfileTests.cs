using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("SecurityState")]
public sealed class DefaultTransportStateCleanProfileTests : IDisposable
{
    private readonly string _profileRoot;
    private readonly string _appDataDirectory;

    public DefaultTransportStateCleanProfileTests()
    {
        _profileRoot = Path.Combine(Path.GetTempPath(), "WpfDevTools_CleanProfile_" + Guid.NewGuid().ToString("N"));
        _appDataDirectory = Path.Combine(_profileRoot, "AppData", "Roaming");
        Directory.CreateDirectory(_appDataDirectory);
    }

    public void Dispose() => DeleteDirectoryWithRetry(_profileRoot);

    [Fact]
    public void Create_UsingCleanDefaultProfile_ShouldCreateAndReusePersistedAuthAndCertificate()
    {
        var first = CreateConfiguration();
        var second = default(TransportSecurityConfiguration);
        try
        {
            using var firstCertificate = first.CertificateManager.GetOrCreateCertificate();
            var firstSecret = first.AuthenticationManager.GetSharedSecret();
            second = CreateConfiguration();
            using var secondCertificate = second.CertificateManager.GetOrCreateCertificate();

            second.AuthenticationManager.GetSharedSecret().Should().Equal(firstSecret);
            secondCertificate.Thumbprint.Should().Be(firstCertificate.Thumbprint);
            first.CertificateManager.CertificateDirectory.Should().Be(DefaultCertificateDirectory);
            second.UsesExplicitAuthenticationSecret.Should().BeFalse();
            second.UsesExplicitCertificateDirectory.Should().BeFalse();

            AssertProtectedFileSystemEntry(DefaultAuthDirectory);
            AssertProtectedFileSystemEntry(DefaultAuthSecretPath);
            AssertProtectedFileSystemEntry(DefaultCertificateDirectory);
            AssertProtectedFileSystemEntry(DefaultCertificatePath);
            AssertProtectedFileSystemEntry(DefaultCertificatePasswordPath);
        }
        finally
        {
            second?.AuthenticationManager.Dispose();
            first.AuthenticationManager.Dispose();
        }
    }

    [Theory]
    [InlineData("certificate")]
    [InlineData("password")]
    public void Create_UsingCleanDefaultProfile_WhenPersistedArtifactsAreCorrupt_ShouldRegenerate(
        string corruptCertificateArtifact)
    {
        var first = CreateConfiguration();
        var second = default(TransportSecurityConfiguration);
        try
        {
            using var firstCertificate = first.CertificateManager.GetOrCreateCertificate();
            var firstSecret = first.AuthenticationManager.GetSharedSecret();
            var firstThumbprint = firstCertificate.Thumbprint;

            File.WriteAllBytes(DefaultAuthSecretPath, RandomNumberGenerator.GetBytes(17));
            if (corruptCertificateArtifact == "certificate")
            {
                File.WriteAllBytes(DefaultCertificatePath, RandomNumberGenerator.GetBytes(29));
            }
            else
            {
                File.WriteAllBytes(DefaultCertificatePasswordPath, RandomNumberGenerator.GetBytes(13));
            }

            second = CreateConfiguration();
            using var secondCertificate = second.CertificateManager.GetOrCreateCertificate();

            second.AuthenticationManager.GetSharedSecret().Should().NotEqual(firstSecret);
            secondCertificate.Thumbprint.Should().NotBe(firstThumbprint);
            Directory.GetFiles(DefaultAuthDirectory, "shared-secret.bin.corrupt-*")
                .Should().ContainSingle();
            AssertProtectedFileSystemEntry(DefaultAuthSecretPath);
            AssertProtectedFileSystemEntry(DefaultCertificatePath);
            AssertProtectedFileSystemEntry(DefaultCertificatePasswordPath);
        }
        finally
        {
            second?.AuthenticationManager.Dispose();
            first.AuthenticationManager.Dispose();
        }
    }

    [Fact]
    public void Create_UsingCleanDefaultProfile_WhenCurrentUserDpapiFails_ShouldRequireExplicitFallback()
    {
        using (LocalSecretProtector.BeginTestScope(
            (_, scope) => scope == DataProtectionScope.CurrentUser
                ? throw new CryptographicException("CurrentUser DPAPI unavailable")
                : [1, 2, 3],
            localMachineFallbackAllowed: () => false))
        {
            var act = () => CreateConfiguration();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*default persisted authentication secret*")
                .WithInnerException<CryptographicException>();
        }

        ResetProfile();

        using (LocalSecretProtector.BeginTestScope(
            (_, scope) => scope == DataProtectionScope.CurrentUser
                ? throw new CryptographicException("CurrentUser DPAPI unavailable")
                : [1, 2, 3],
            localMachineFallbackAllowed: () => true))
        {
            var configuration = CreateConfiguration();
            try
            {
                Encoding.ASCII.GetString(File.ReadAllBytes(DefaultAuthSecretPath))
                    .Should().StartWith("WPFDEVTOOLS-DPAPI:LocalMachine\n");
                configuration.AuthenticationManager.GetSharedSecret().Should().HaveCount(32);
            }
            finally
            {
                configuration.AuthenticationManager.Dispose();
            }
        }
    }

    [Fact]
    public void Create_UsingNetworkDefaultProfile_ShouldFailClosedBeforeCreatingArtifacts()
    {
        var act = () => TransportSecurityConfiguration.Create(
            null,
            null,
            defaultAppDataPathProvider: () => @"\\server\share\AppData\Roaming");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*default persisted authentication secret*")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*local path*");
    }

    private TransportSecurityConfiguration CreateConfiguration()
        => TransportSecurityConfiguration.Create(
            null,
            null,
            defaultAppDataPathProvider: () => _appDataDirectory);

    private string DefaultAuthDirectory => Path.Combine(_appDataDirectory, "WpfDevTools", "auth");

    private string DefaultAuthSecretPath => Path.Combine(DefaultAuthDirectory, "shared-secret.bin");

    private string DefaultCertificateDirectory => Path.Combine(_appDataDirectory, "WpfDevTools", "certs");

    private string DefaultCertificatePath => Path.Combine(DefaultCertificateDirectory, "server.pfx");

    private string DefaultCertificatePasswordPath => Path.Combine(DefaultCertificateDirectory, "server.pwd");

    private void ResetProfile()
    {
        DeleteDirectoryWithRetry(_profileRoot);
        Directory.CreateDirectory(_appDataDirectory);
    }

    private static void AssertProtectedFileSystemEntry(string path)
    {
        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();

        security.AreAccessRulesProtected.Should().BeTrue($"{path} should not inherit broad profile ACLs");
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }
}
