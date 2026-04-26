using System.Security.Cryptography;
using System.Text;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Persists the default MCP server authentication secret so restarted servers can
/// reconnect to already injected inspector hosts without forcing a target restart.
/// </summary>
internal sealed class PersistedAuthenticationSecretStore
{
    private const int SecretLengthBytes = 32;
    private readonly string _secretFilePath;
    private readonly TimeSpan _mutexTimeout;

    internal string MutexName => BuildMutexName();

    public PersistedAuthenticationSecretStore(string? secretFilePath = null, TimeSpan? mutexTimeout = null)
    {
        var resolvedSecretFilePath = string.IsNullOrWhiteSpace(secretFilePath)
            ? ResolveDefaultSecretFilePath()
            : secretFilePath;
        _secretFilePath = ResolveAndValidateSecretFilePath(resolvedSecretFilePath);
        _mutexTimeout = mutexTimeout ?? TimeSpan.FromSeconds(30);
    }

    public string GetOrCreateSecretBase64()
    {
        using var mutex = new Mutex(false, BuildMutexName());
        var lockTaken = false;
        try
        {
            try
            {
                lockTaken = mutex.WaitOne(_mutexTimeout);
                if (!lockTaken)
                {
                    throw new TimeoutException($"Timed out waiting to access persisted authentication secret at '{_secretFilePath}'.");
                }
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            return GetOrCreateSecretBase64UnderLock();
        }
        finally
        {
            if (lockTaken)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private string GetOrCreateSecretBase64UnderLock()
    {
        PrepareSecretDirectory();

        if (File.Exists(_secretFilePath))
        {
            try
            {
                return LoadSecretBase64();
            }
            catch (CryptographicException)
            {
                return QuarantineAndRegenerateSecretBase64();
            }
            catch (InvalidDataException)
            {
                return QuarantineAndRegenerateSecretBase64();
            }
        }

        return CreateAndPersistSecretBase64();
    }

    private string LoadSecretBase64()
    {
        CertificateStorageSecurity.PrepareExistingFile(_secretFilePath, "persisted authentication secret");
        var protectedBytes = File.ReadAllBytes(_secretFilePath);
        var secretBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        try
        {
            if (secretBytes.Length != SecretLengthBytes)
            {
                throw new InvalidDataException(
                    $"Persisted authentication secret at '{_secretFilePath}' must be exactly {SecretLengthBytes} bytes.");
            }

            return Convert.ToBase64String(secretBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    private string CreateAndPersistSecretBase64()
    {
        var secretBytes = new byte[SecretLengthBytes];
        RandomNumberGenerator.Fill(secretBytes);
        var tempFilePath = _secretFilePath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            var protectedBytes = ProtectedData.Protect(secretBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(tempFilePath, protectedBytes);
            CertificateStorageSecurity.ApplyFileSecurity(tempFilePath);

            try
            {
                File.Move(tempFilePath, _secretFilePath);
                CertificateStorageSecurity.ApplyFileSecurity(_secretFilePath);
            }
            catch (IOException) when (File.Exists(_secretFilePath))
            {
                File.Delete(tempFilePath);
                return LoadSecretBase64();
            }

            return Convert.ToBase64String(secretBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private string QuarantineAndRegenerateSecretBase64()
    {
        if (File.Exists(_secretFilePath))
        {
            var quarantinePath = _secretFilePath + $".corrupt-{Guid.NewGuid():N}";
            File.Move(_secretFilePath, quarantinePath);
        }

        return CreateAndPersistSecretBase64();
    }

    private void PrepareSecretDirectory()
    {
        var directory = Path.GetDirectoryName(_secretFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                $"Persisted authentication secret path must include a parent directory. Resolved path was '{_secretFilePath}'.");
        }

        CertificateStorageSecurity.PrepareDirectory(
            directory,
            "persisted authentication secret directory");
    }

    private string BuildMutexName()
    {
        var normalizedPath = Path.GetFullPath(_secretFilePath).ToUpperInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        var hash = Convert.ToHexString(hashBytes);
        return $"Local\\WpfDevTools.AuthSecret.{hash}";
    }

    private static string ResolveAndValidateSecretFilePath(string path)
    {
        try
        {
            return CertificateStorageSecurity.ResolveAndValidateLocalPath(
                path,
                nameof(path),
                "Persisted authentication secret path");
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Persisted authentication secret path must be absolute, must be a local path, and must not traverse reparse points. Resolved path was '{path}'.",
                ex);
        }
        catch (Exception ex) when (ex is NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                $"Persisted authentication secret path must resolve to a valid local path. Resolved path was '{path}'.",
                ex);
        }
    }

    private static string ResolveDefaultSecretFilePath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appDataPath) || !Path.IsPathRooted(appDataPath))
        {
            throw new InvalidOperationException(
                "Could not resolve a valid ApplicationData directory for the default persisted authentication secret. " +
                "Set WPFDEVTOOLS_AUTH_SECRET explicitly if the current environment does not provide a writable user profile.");
        }

        return Path.Combine(appDataPath, "WpfDevTools", "auth", "shared-secret.bin");
    }
}
