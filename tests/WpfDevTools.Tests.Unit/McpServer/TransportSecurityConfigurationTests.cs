using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class TransportSecurityConfigurationTests
{
    [Fact]
    public void Create_WithoutEnvironmentOverrides_ShouldReusePersistedAuthenticationAndDefaultTls()
    {
        var secretFilePath = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-{Guid.NewGuid():N}.bin");
        var secretStore = new PersistedAuthenticationSecretStore(secretFilePath);
        var configuration = TransportSecurityConfiguration.Create(null, null, secretStore);
        var secondConfiguration = TransportSecurityConfiguration.Create(null, null, secretStore);

        try
        {
            configuration.AuthenticationManager.IsAuthenticationEnabled.Should().BeTrue();
            configuration.AuthenticationManager.GetSharedSecret().Should().HaveCount(32);
            secondConfiguration.AuthenticationManager.GetSharedSecret().Should().Equal(configuration.AuthenticationManager.GetSharedSecret());
            configuration.UsesExplicitAuthenticationSecret.Should().BeFalse();
            configuration.UsesExplicitCertificateDirectory.Should().BeFalse();
            configuration.CertificateManager.CertificateDirectory.Should().Contain(Path.Combine("WpfDevTools", "certs"));
            configuration.GetAuthenticationLogMessage().Should().Contain("persisted default shared secret");
            configuration.GetEncryptionLogMessage().Should().Contain("default certificate directory");
        }
        finally
        {
            secondConfiguration.AuthenticationManager.Dispose();
            configuration.AuthenticationManager.Dispose();
            if (File.Exists(secretFilePath))
            {
                File.Delete(secretFilePath);
            }
        }
    }

    [Fact]
    public void Create_WithEnvironmentOverrides_ShouldUseExplicitSecurityArtifacts()
    {
        var expectedSecret = new byte[32];
        RandomNumberGenerator.Fill(expectedSecret);
        var authSecret = Convert.ToBase64String(expectedSecret);
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-certs-{Guid.NewGuid():N}");

        var configuration = TransportSecurityConfiguration.Create(authSecret, certDirectory);

        try
        {
            configuration.UsesExplicitAuthenticationSecret.Should().BeTrue();
            configuration.UsesExplicitCertificateDirectory.Should().BeTrue();
            configuration.AuthenticationManager.GetSharedSecret().Should().Equal(expectedSecret);
            configuration.CertificateManager.CertificateDirectory.Should().Be(certDirectory);
            configuration.GetAuthenticationLogMessage().Should().Contain("WPFDEVTOOLS_AUTH_SECRET");
            configuration.GetEncryptionLogMessage().Should().Contain(certDirectory);
        }
        finally
        {
            configuration.AuthenticationManager.Dispose();
        }
    }

    [Fact]
    public void Create_WithRelativeCertificateDirectory_ShouldThrowClearError()
    {
        var act = () => TransportSecurityConfiguration.Create(null, Path.Combine("relative", "certs"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_CERT_DIR*absolute path*");
    }

    [Fact]
    public void Create_WithDriveRelativeCertificateDirectory_ShouldThrowClearError()
    {
        var act = () => TransportSecurityConfiguration.Create(null, GetDriveRelativePath("certs"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_CERT_DIR*absolute path*");
    }

    [Fact]
    public void Create_WithUncCertificateDirectory_ShouldThrowClearError()
    {
        var act = () => TransportSecurityConfiguration.Create(null, @"\\server\share\wpfdevtools-certs");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_CERT_DIR*local path*");
    }

    [Fact]
    public void Create_WithMalformedAbsoluteCertificateDirectory_ShouldThrowClearError()
    {
        var malformedPath = string.Concat(Path.GetPathRoot(Path.GetTempPath()), "bad", '\0', "name");
        var act = () => TransportSecurityConfiguration.Create(null, malformedPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WPFDEVTOOLS_CERT_DIR*valid absolute path*");
    }

    [Fact]
    public void Create_WhenPersistedSecretFileIsLocked_ShouldWrapClearStartupError()
    {
        var secretFilePath = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-{Guid.NewGuid():N}.bin");
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var protectedBytes = ProtectedData.Protect(secretBytes, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(secretFilePath)!);
        File.WriteAllBytes(secretFilePath, protectedBytes);
        var secretStore = new PersistedAuthenticationSecretStore(secretFilePath);

        try
        {
            using var lockStream = new FileStream(secretFilePath, FileMode.Open, FileAccess.Read, FileShare.None);

            var act = () => TransportSecurityConfiguration.Create(null, null, secretStore);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*default persisted authentication secret*")
                .WithInnerException<IOException>();
        }
        finally
        {
            if (File.Exists(secretFilePath))
            {
                File.Delete(secretFilePath);
            }
        }
    }

    [Fact]
    public void Create_WhenDefaultCertificateDirectoryCannotBeResolved_ShouldWrapClearStartupError()
    {
        var act = () => TransportSecurityConfiguration.Create(
            null,
            null,
            defaultCertificateManagerFactory: () => throw new InvalidOperationException("ApplicationData unavailable"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*default TLS certificate directory*")
            .WithInnerException<InvalidOperationException>();
    }

    [Fact]
    public void Create_WhenPersistedSecretStoreTimesOut_ShouldWrapClearStartupError()
    {
        var secretFilePath = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-{Guid.NewGuid():N}.bin");
        var secretStore = new PersistedAuthenticationSecretStore(secretFilePath, TimeSpan.FromMilliseconds(10));
        using var mutexAcquired = new ManualResetEventSlim(false);
        using var releaseMutex = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(false, secretStore.MutexName);
            mutex.WaitOne();
            mutexAcquired.Set();
            releaseMutex.Wait();
            mutex.ReleaseMutex();
        });

        thread.Start();
        mutexAcquired.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();

        try
        {
            var act = () => TransportSecurityConfiguration.Create(null, null, secretStore);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*default persisted authentication secret*")
                .WithInnerException<TimeoutException>();
        }
        finally
        {
            releaseMutex.Set();
            thread.Join();
            if (File.Exists(secretFilePath))
            {
                File.Delete(secretFilePath);
            }
        }
    }

    private static string GetDriveRelativePath(string pathSuffix)
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        return root[..2] + pathSuffix;
    }
}