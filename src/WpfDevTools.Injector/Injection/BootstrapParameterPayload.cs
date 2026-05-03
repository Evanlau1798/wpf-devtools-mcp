using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Runtime.InteropServices;

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
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var secretFilePath = string.IsNullOrWhiteSpace(request.AuthenticationSecretBase64)
            ? null
            : CreateAuthenticationSecretFile(
                request.ProcessId,
                request.AuthenticationSecretBase64!);

        try
        {
            return new BootstrapParameterPayload(
                request.BuildBootstrapParameters(secretFilePath),
                secretFilePath);
        }
        catch
        {
            DeleteSecretFile(secretFilePath);
            throw;
        }
    }

    public void Dispose()
    {
        DeleteSecretFile(AuthenticationSecretFilePath);
    }

    private static string CreateAuthenticationSecretFile(int processId, string secretBase64)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"WpfDevTools_AuthSecret_{processId}_{Guid.NewGuid():N}.txt");

        using (var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough))
        {
            RestrictSecretFileAcl(path);
            var bytes = Encoding.UTF8.GetBytes(secretBase64);
            stream.Write(bytes, 0, bytes.Length);
        }

        return path;
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
            FileSystemRights.Read | FileSystemRights.Delete | FileSystemRights.ReadPermissions,
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

    private static void DeleteSecretFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
