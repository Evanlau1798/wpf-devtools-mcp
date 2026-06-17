using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Runtime.InteropServices;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Injector.Injection;

internal sealed class BootstrapParameterPayload : IDisposable
{
    private BootstrapParameterPayload(string parameters, string? authenticationSecretFilePath)
    {
        Parameters = parameters;
        AuthenticationSecretFilePath = authenticationSecretFilePath;
    }

    public string Parameters { get; }

    public string? AuthenticationSecretFilePath { get; }

    public static BootstrapParameterPayload Create(InjectionRequest request)
    {
        return Create(request, onSecretFileCreated: null);
    }

    internal static BootstrapParameterPayload Create(
        InjectionRequest request,
        Action<string>? onSecretFileCreated,
        Func<string>? tempPathProvider = null,
        Func<string, bool>? reparsePointDetector = null)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var secretFilePath = string.IsNullOrWhiteSpace(request.AuthenticationSecretBase64)
            ? null
            : CreateAuthenticationSecretFile(
                request.ProcessId,
                request.AuthenticationSecretBase64!,
                onSecretFileCreated,
                tempPathProvider,
                reparsePointDetector);

        try
        {
            return new BootstrapParameterPayload(
                request.BuildBootstrapParameters(secretFilePath),
                secretFilePath);
        }
        catch
        {
            SecureDeleteSecretFile(secretFilePath);
            throw;
        }
    }

    public void Dispose()
    {
        SecureDeleteSecretFile(AuthenticationSecretFilePath);
    }

    private static string CreateAuthenticationSecretFile(
        int processId,
        string secretBase64,
        Action<string>? onSecretFileCreated,
        Func<string>? tempPathProvider,
        Func<string, bool>? reparsePointDetector)
    {
#if !NET48
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Bootstrap authentication secret handoff requires Windows DPAPI.");
        }
#endif

        var tempRoot = ResolveAuthenticationSecretTempRoot(tempPathProvider, reparsePointDetector);
        var path = Path.Combine(
            tempRoot,
            $"WpfDevTools_AuthSecret_{processId}_{Guid.NewGuid():N}.txt");

        try
        {
            EnsureNoAuthenticationSecretTempRootReparsePoint(tempRoot, reparsePointDetector);
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                RestrictSecretFileAcl(path);
                onSecretFileCreated?.Invoke(path);
                var bytes = Encoding.UTF8.GetBytes(secretBase64);
                byte[] protectedBytes = [];
                try
                {
                    protectedBytes = LocalSecretProtector.Protect(bytes);
                    stream.Write(protectedBytes, 0, protectedBytes.Length);
                }
                finally
                {
                    Array.Clear(bytes, 0, bytes.Length);
                    Array.Clear(protectedBytes, 0, protectedBytes.Length);
                }
            }

            return path;
        }
        catch
        {
            SecureDeleteSecretFile(path);
            throw;
        }
    }

    private static string ResolveAuthenticationSecretTempRoot(
        Func<string>? tempPathProvider,
        Func<string, bool>? reparsePointDetector)
    {
#if !NET48
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Bootstrap authentication secret handoff requires Windows DPAPI.");
        }
#endif

        var tempRoot = (tempPathProvider ?? Path.GetTempPath)();
        try
        {
            CertificateStorageSecurity.EnsureLocalPath(tempRoot, nameof(tempRoot));
            var fullTempRoot = Path.GetFullPath(tempRoot);
            EnsureNoAuthenticationSecretTempRootReparsePoint(fullTempRoot, reparsePointDetector);
            return fullTempRoot;
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                "Bootstrap authentication secret temp directory must be an absolute local path " +
                "and must not traverse reparse points. " +
                ex.Message,
                ex);
        }
        catch (Exception ex) when (ex is NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                "Bootstrap authentication secret temp directory must resolve to a valid local path.",
                ex);
        }
    }

    private static void EnsureNoAuthenticationSecretTempRootReparsePoint(
        string tempRoot,
        Func<string, bool>? reparsePointDetector)
    {
        if (reparsePointDetector?.Invoke(tempRoot) == true)
        {
            throw new InvalidOperationException(
                "Bootstrap authentication secret temp directory must not traverse symbolic links or reparse points.");
        }
    }

    private static void RestrictSecretFileAcl(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser == null)
        {
            return;
        }

        var security = new FileSecurity();
        security.SetOwner(currentUser);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Delete,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        new FileInfo(path).SetAccessControl(security);
    }

    private static void SecureDeleteSecretFile(string? path)
    {
        var secretPath = path ?? string.Empty;
        if (string.IsNullOrWhiteSpace(secretPath))
        {
            return;
        }

        if (!File.Exists(secretPath))
        {
            return;
        }

        try
        {
            WipeSecretFile(secretPath);
        }
        catch (IOException ex)
        {
            Trace.TraceWarning($"BootstrapParameterPayload failed to wipe auth secret file {SensitiveLogRedactor.Redact(secretPath)}: {SensitiveLogRedactor.Redact(ex.Message)}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning($"BootstrapParameterPayload failed to wipe auth secret file {SensitiveLogRedactor.Redact(secretPath)}: {SensitiveLogRedactor.Redact(ex.Message)}");
        }

        try
        {
            File.Delete(secretPath);
        }
        catch (IOException ex)
        {
            Trace.TraceWarning($"BootstrapParameterPayload failed to delete auth secret file {SensitiveLogRedactor.Redact(secretPath)}: {SensitiveLogRedactor.Redact(ex.Message)}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning($"BootstrapParameterPayload failed to delete auth secret file {SensitiveLogRedactor.Redact(secretPath)}: {SensitiveLogRedactor.Redact(ex.Message)}");
        }
    }

    private static void WipeSecretFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.WriteThrough);

        var length = stream.Length;
        if (length > 0)
        {
            var zeros = new byte[256];
            while (length > 0)
            {
                var bytesToWrite = (int)Math.Min(zeros.Length, length);
                stream.Write(zeros, 0, bytesToWrite);
                length -= bytesToWrite;
            }

            stream.Flush(flushToDisk: true);
            stream.Position = 0;
        }

        stream.SetLength(0);
        stream.Flush(flushToDisk: true);
    }
}
