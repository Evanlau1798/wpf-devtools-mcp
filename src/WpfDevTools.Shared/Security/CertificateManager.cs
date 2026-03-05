using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace WpfDevTools.Shared.Security;

/// <summary>
/// Manages self-signed X.509 certificates for SslStream encryption.
/// Certificates are persisted to a specified directory and reused across sessions.
/// </summary>
public class CertificateManager
{
    private const string CertFileName = "server.pfx";
    private const string SubjectName = "CN=WpfDevTools-Inspector";
    private const int RsaKeySize = 2048;

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
        if (string.IsNullOrWhiteSpace(certDirectory))
            throw new ArgumentException("Certificate directory cannot be empty", nameof(certDirectory));

        _certDirectory = certDirectory;
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
    /// Gets or creates a self-signed server certificate.
    /// If a valid certificate exists on disk, it is loaded and returned.
    /// Otherwise, a new self-signed certificate is generated, saved, and returned.
    /// </summary>
    /// <returns>X509Certificate2 with private key</returns>
    public X509Certificate2 GetOrCreateCertificate()
    {
        Directory.CreateDirectory(_certDirectory);
        var certPath = Path.Combine(_certDirectory, CertFileName);

        if (File.Exists(certPath))
        {
            try
            {
                var loaded = new X509Certificate2(
                    certPath, GetCertPassword(),
                    X509KeyStorageFlags.Exportable);

                if (loaded.NotAfter > DateTime.UtcNow)
                    return loaded;

                // Certificate expired, regenerate
                loaded.Dispose();
            }
            catch (CryptographicException)
            {
                // Corrupt file, regenerate
            }
        }

        return CreateAndSaveCertificate(certPath);
    }

    private X509Certificate2 CreateAndSaveCertificate(string certPath)
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
                    new Oid("1.3.6.1.5.5.7.3.1") // Server Authentication
                },
                critical: true));

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // Export and re-import to make the private key persistable
        var pfxBytes = cert.Export(X509ContentType.Pfx, GetCertPassword());
        File.WriteAllBytes(certPath, pfxBytes);
        cert.Dispose();

        return new X509Certificate2(
            certPath, GetCertPassword(),
            X509KeyStorageFlags.Exportable);
    }

    private string GetCertPassword()
    {
        var machineId = Environment.MachineName + Environment.UserName;
#if NET48
        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineId));
            return Convert.ToBase64String(hash);
        }
#else
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineId));
        return Convert.ToBase64String(hash);
#endif
    }
}
