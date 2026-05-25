using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace WpfDevTools.Shared.Security;

/// <summary>
/// Manages self-signed X.509 certificates for SslStream encryption.
/// Certificates are persisted to a specified directory and reused across sessions.
/// PFX passwords are protected using DPAPI (CurrentUser scope).
/// </summary>
#if !NET48
[SupportedOSPlatform("windows")]
#endif
public sealed class CertificateManager
{
    private const string CertFileName = "server.pfx";
    private const string PasswordFileName = "server.pwd";
    private const string SubjectName = "CN=WpfDevTools-Inspector";
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const int RsaKeySize = 2048;
    private const int PasswordLengthBytes = 32;
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(30);
    private static readonly object SyncRoot = new();

    private readonly string _certDirectory;

    /// <summary>
    /// Creates a new CertificateManager
    /// </summary>
    /// <param name="certDirectory">Directory to store certificate files</param>
    /// <exception cref="ArgumentNullException">Thrown when certDirectory is null</exception>
    /// <exception cref="ArgumentException">Thrown when certDirectory is empty</exception>
    public CertificateManager(string certDirectory)
    {
        if (certDirectory == null)
            throw new ArgumentNullException(nameof(certDirectory));
        _certDirectory = CertificateStorageSecurity.ResolveAndValidateLocalPath(certDirectory, nameof(certDirectory));
    }

    /// <summary>
    /// Creates a CertificateManager using the default directory (%APPDATA%\WpfDevTools\certs)
    /// </summary>
    public CertificateManager()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WpfDevTools", "certs"))
    {
    }

    /// <summary>
    /// Gets the directory used to persist certificate files.
    /// </summary>
    public string CertificateDirectory => _certDirectory;

    /// <summary>
    /// Gets or creates a self-signed server certificate.
    /// If a valid certificate exists on disk, it is loaded and returned.
    /// Otherwise, a new self-signed certificate is generated, saved, and returned.
    /// </summary>
    /// <returns>X509Certificate2 with private key</returns>
    public X509Certificate2 GetOrCreateCertificate()
    {
        using var mutex = new Mutex(false, BuildMutexName(_certDirectory));
        var lockTaken = false;
        try
        {
            try
            {
                lockTaken = mutex.WaitOne(MutexTimeout);
                if (!lockTaken)
                {
                    throw new TimeoutException($"Timed out waiting to access certificate directory '{_certDirectory}'.");
                }
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            lock (SyncRoot)
            {
                CertificateStorageSecurity.PrepareDirectory(_certDirectory);
                var certPath = Path.Combine(_certDirectory, CertFileName);
                var passwordPath = Path.Combine(_certDirectory, PasswordFileName);
                CertificateStorageSecurity.PrepareExistingFile(certPath, "certificate file");
                CertificateStorageSecurity.PrepareExistingFile(passwordPath, "certificate password file");

                if (File.Exists(certPath) && File.Exists(passwordPath))
                {
                    try
                    {
                        var password = LoadPassword(passwordPath);
                        var loaded = LoadCertificateFromFile(certPath, password);

                        if (IsReusableCertificate(loaded))
                            return loaded;

                        loaded.Dispose();
                    }
                    catch (CryptographicException)
                    {
                        // Corrupt file or password mismatch, regenerate
                    }
                }

                return CreateAndSaveCertificate(certPath, passwordPath);
            }
        }
        finally
        {
            if (lockTaken)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private X509Certificate2 CreateAndSaveCertificate(string certPath, string passwordPath)
    {
        using var rsa = RSA.Create(RsaKeySize);
        var request = new CertificateRequest(
            SubjectName, rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new Oid(ServerAuthenticationOid)
                },
                critical: true));

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // Generate random password and protect with DPAPI
        var password = GenerateRandomPassword();
        SavePassword(passwordPath, password);
        CertificateStorageSecurity.ApplyFileSecurity(passwordPath);

        // Persist the encrypted PFX on disk, then re-import with a non-exportable
        // private key and non-persistent key storage unless compatibility fallback is required.
        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(certPath, pfxBytes);
        CertificateStorageSecurity.ApplyFileSecurity(certPath);
        cert.Dispose();

        return LoadCertificateFromFile(certPath, password);
    }

    private static bool IsReusableCertificate(X509Certificate2 certificate)
        => IsWithinValidityPeriod(certificate)
           && HasExpectedSubject(certificate)
           && HasExpectedRsaKeySize(certificate)
           && HasRequiredKeyUsage(certificate)
           && HasServerAuthenticationEku(certificate);

    private static bool IsWithinValidityPeriod(X509Certificate2 certificate)
        => certificate.NotBefore <= DateTime.UtcNow && certificate.NotAfter > DateTime.UtcNow;

    private static bool HasExpectedSubject(X509Certificate2 certificate)
        => string.Equals(certificate.Subject, SubjectName, StringComparison.Ordinal);

    private static bool HasExpectedRsaKeySize(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPublicKey();
        return rsa?.KeySize >= RsaKeySize;
    }

    private static bool HasRequiredKeyUsage(X509Certificate2 certificate)
    {
        const X509KeyUsageFlags requiredUsages = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment;
        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        return keyUsage is not null && (keyUsage.KeyUsages & requiredUsages) == requiredUsages;
    }

    private static bool HasServerAuthenticationEku(X509Certificate2 certificate)
    {
        var enhancedKeyUsage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        return enhancedKeyUsage is not null
               && enhancedKeyUsage.EnhancedKeyUsages.Cast<Oid>()
                   .Any(oid => string.Equals(oid.Value, ServerAuthenticationOid, StringComparison.Ordinal));
    }

    private static X509Certificate2 LoadCertificateFromFile(string certPath, string password)
        => LoadCertificateFromFile(
            certPath,
            password,
            static (path, certificatePassword, keyStorageFlags) =>
                new X509Certificate2(path, certificatePassword, keyStorageFlags));

    internal static X509Certificate2 LoadCertificateFromFile(
        string certPath,
        string password,
        Func<string, string, X509KeyStorageFlags, X509Certificate2> certificateLoader)
    {
        CryptographicException? lastException = null;
        foreach (var keyStorageFlags in GetNonExportableKeyStoragePreferences())
        {
            try
            {
                var certificate = certificateLoader(certPath, password, keyStorageFlags);
                if (RequiresServerTlsFallback(keyStorageFlags))
                {
                    certificate.Dispose();
                    continue;
                }

                return certificate;
            }
            catch (CryptographicException ex)
            {
                lastException = ex;
            }
        }

        throw lastException ?? new CryptographicException("Certificate could not be loaded.");
    }

    private static X509KeyStorageFlags[] GetNonExportableKeyStoragePreferences()
    {
#if NET48
        return
        [
            X509KeyStorageFlags.UserKeySet,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet
        ];
#else
        return
        [
            X509KeyStorageFlags.EphemeralKeySet,
            X509KeyStorageFlags.UserKeySet,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet
        ];
#endif
    }

    private static bool RequiresServerTlsFallback(X509KeyStorageFlags keyStorageFlags)
    {
#if NET48
        return false;
#else
        return OperatingSystem.IsWindows()
               && (keyStorageFlags & X509KeyStorageFlags.EphemeralKeySet) != 0;
#endif
    }

    private static string GenerateRandomPassword()
    {
        var randomBytes = new byte[PasswordLengthBytes];
#if NET48
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
#else
        RandomNumberGenerator.Fill(randomBytes);
#endif
        return Convert.ToBase64String(randomBytes);
    }

    internal static void SavePassword(string passwordPath, string password)
    {
        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
        try
        {
            var protectedBytes = LocalSecretProtector.Protect(passwordBytes);
            File.WriteAllBytes(passwordPath, protectedBytes);
        }
        finally
        {
            Array.Clear(passwordBytes, 0, passwordBytes.Length);
        }
    }

    internal static string LoadPassword(string passwordPath)
    {
        var protectedBytes = File.ReadAllBytes(passwordPath);
        var passwordBytes = LocalSecretProtector.Unprotect(protectedBytes);
        try
        {
            return System.Text.Encoding.UTF8.GetString(passwordBytes);
        }
        finally
        {
            Array.Clear(passwordBytes, 0, passwordBytes.Length);
        }
    }
    private static string BuildMutexName(string certificateDirectory)
    {
        var normalizedPath = Path.GetFullPath(certificateDirectory).ToUpperInvariant();
        var hash = ComputeSha256Hex(Encoding.UTF8.GetBytes(normalizedPath));
        return $"Local\\WpfDevTools.CertDir.{hash}";
    }

    private static string ComputeSha256Hex(byte[] input)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(input);
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
    }
}
