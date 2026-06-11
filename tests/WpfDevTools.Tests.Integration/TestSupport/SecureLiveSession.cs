using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal sealed class SecureLiveSession : IDisposable
{
    private readonly AuthenticationManager _authenticationManager;
    private readonly string _certificateDirectory;
    private int _disposeState;

    private SecureLiveSession(
        AuthenticationManager authenticationManager,
        CertificateManager certificateManager,
        string certificateDirectory)
    {
        _authenticationManager = authenticationManager;
        _certificateDirectory = certificateDirectory;
        SessionManager = new SessionManager(
            authManager: authenticationManager,
            certManager: certificateManager);
    }

    public SessionManager SessionManager { get; }

    internal string CertificateDirectoryForTesting => _certificateDirectory;

    public static SecureLiveSession Create(string directoryPrefix = "WpfDevTools_SecureLiveSession")
    {
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var authSecret = Convert.ToBase64String(secretBytes);
        Array.Clear(secretBytes);

        var certificateDirectory = Path.Combine(
            ReleasePackagingTestHarness.GetRepoFilePath("tmp"),
            $"{directoryPrefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(certificateDirectory);

        return new SecureLiveSession(
            new AuthenticationManager(() => authSecret),
            new CertificateManager(certificateDirectory),
            certificateDirectory);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        try
        {
            SessionManager.Dispose();
        }
        finally
        {
            _authenticationManager.Dispose();
            TryDeleteCertificateDirectory();
        }
    }

    private void TryDeleteCertificateDirectory()
    {
        try
        {
            ReleasePackagingTestHarness.DeleteDirectory(_certificateDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"SecureLiveSession: failed to clean certificate directory '{_certificateDirectory}': {ex.Message}");
        }
    }
}
