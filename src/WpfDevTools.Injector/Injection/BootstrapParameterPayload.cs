using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Runtime.InteropServices;
using WpfDevTools.Shared.Security;

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
        Action<string>? onSecretFileCreated)
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
                onSecretFileCreated);

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
        Action<string>? onSecretFileCreated)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"WpfDevTools_AuthSecret_{processId}_{Guid.NewGuid():N}.txt");

        try
        {
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
#if !NET48
                    if (!OperatingSystem.IsWindows())
                    {
                        throw new PlatformNotSupportedException("Bootstrap authentication secret handoff requires Windows DPAPI.");
                    }
#endif
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
            Trace.TraceWarning($"BootstrapParameterPayload failed to wipe auth secret file '{secretPath}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning($"BootstrapParameterPayload failed to wipe auth secret file '{secretPath}': {ex.Message}");
        }

        try
        {
            File.Delete(secretPath);
        }
        catch (IOException ex)
        {
            Trace.TraceWarning($"BootstrapParameterPayload failed to delete auth secret file '{secretPath}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning($"BootstrapParameterPayload failed to delete auth secret file '{secretPath}': {ex.Message}");
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
