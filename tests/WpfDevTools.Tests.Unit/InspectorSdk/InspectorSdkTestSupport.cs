using System.Security.Cryptography;
using System.Windows;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Inspector.Sdk;
using WpfDevTools.Shared.Security;
using SdkInspector = WpfDevTools.Inspector.Sdk.InspectorSdk;

namespace WpfDevTools.Tests.Unit.InspectorSdk;

internal sealed class InspectorSdkTestContext : IDisposable
{
    private readonly string? _originalAuthSecret;
    private readonly string? _originalCertDirectory;
    private readonly List<string> _trackedDirectories = new();
    private readonly List<string> _trackedFiles = new();
    private bool _disposed;

    public InspectorSdkTestContext()
    {
        _originalAuthSecret = Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET");
        _originalCertDirectory = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR");

        ResetTestHooks();
        SdkInspector.Shutdown();
    }

    public static string CreateAuthSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public void SetTransport(string? authSecret, string? certDirectory)
    {
        Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", authSecret);
        Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", certDirectory);
    }

    public string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _trackedDirectories.Add(path);
        return path;
    }

    public string CreateTemporaryFile()
    {
        var path = Path.GetTempFileName();
        _trackedFiles.Add(path);
        return path;
    }

    public void TrackDirectory(string path)
    {
        _trackedDirectories.Add(path);
    }

    public static void ResetTestHooks()
    {
        SdkInspector.DispatcherResolver = static () => Application.Current?.Dispatcher;
        SdkInspector.InitializationQueuedCallback = null;
        SdkInspector.BeforeAbortPendingInitializationCallback = null;
        SdkInspector.HostStartedCallback = null;
    }

    public static void SetInspectorSdkState(
        InspectorHost? host,
        AuthenticationManager? authenticationManager,
        CertificateManager? certificateManager,
        bool isInitialized,
        int isInitializing)
    {
        typeof(SdkInspector)
            .GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, host);
        typeof(SdkInspector)
            .GetField("_authenticationManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, authenticationManager);
        typeof(SdkInspector)
            .GetField("_certificateManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, certificateManager);
        typeof(SdkInspector)
            .GetField("_isInitialized", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, isInitialized);
        typeof(SdkInspector)
            .GetField("_isInitializing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, isInitializing);
    }

    public static InspectorHost? GetInspectorSdkHost()
    {
        return (InspectorHost?)typeof(SdkInspector)
            .GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null);
    }

    public static AuthenticationManager? GetInspectorSdkAuthenticationManager()
    {
        return (AuthenticationManager?)typeof(SdkInspector)
            .GetField("_authenticationManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null);
    }

    public static CertificateManager? GetInspectorSdkCertificateManager()
    {
        return (CertificateManager?)typeof(SdkInspector)
            .GetField("_certificateManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ResetTestHooks();
        SdkInspector.Shutdown();
        Environment.SetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET", _originalAuthSecret);
        Environment.SetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR", _originalCertDirectory);

        foreach (var path in _trackedFiles)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        foreach (var path in _trackedDirectories.OrderByDescending(static path => path.Length))
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}