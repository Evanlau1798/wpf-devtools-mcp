using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WpfDevTools.Shared.Security;

#if !NET48
[SupportedOSPlatform("windows")]
#endif
internal static class CertificateStorageSecurity
{
    private static readonly SecurityIdentifier LocalSystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier EveryoneSid = new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier AuthenticatedUsersSid = new(WellKnownSidType.AuthenticatedUserSid, null);
    private static readonly SecurityIdentifier BuiltinUsersSid = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier BuiltinGuestsSid = new(WellKnownSidType.BuiltinGuestsSid, null);
    private static readonly SecurityIdentifier AnonymousSid = new(WellKnownSidType.AnonymousSid, null);
    private static readonly SecurityIdentifier NetworkSid = new(WellKnownSidType.NetworkSid, null);

    private const FileSystemRights DangerousWriteRights =
        FileSystemRights.Write |
        FileSystemRights.Modify |
        FileSystemRights.FullControl |
        FileSystemRights.CreateDirectories |
        FileSystemRights.CreateFiles |
        FileSystemRights.AppendData |
        FileSystemRights.WriteData |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership |
        FileSystemRights.WriteAttributes |
        FileSystemRights.WriteExtendedAttributes;

    public static bool IsNetworkPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal);
    }

    public static void EnsureLocalPath(string path, string parameterName)
    {
        _ = ResolveAndValidateLocalPath(path, parameterName);
    }

    internal static string ResolveAndValidateLocalPath(
        string path,
        string parameterName,
        Func<string, DriveType>? driveTypeResolver = null,
        Func<string, bool>? pathExistsResolver = null,
        Func<string, FileAttributes>? attributesResolver = null)
        => ResolveAndValidateLocalPath(
            path,
            parameterName,
            description: "Certificate directory",
            driveTypeResolver,
            pathExistsResolver,
            attributesResolver);

    internal static string ResolveAndValidateLocalPath(
        string path,
        string parameterName,
        string description,
        Func<string, DriveType>? driveTypeResolver = null,
        Func<string, bool>? pathExistsResolver = null,
        Func<string, FileAttributes>? attributesResolver = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{description} cannot be empty", parameterName);
        }

        if (!IsAbsolutePath(path))
        {
            throw new ArgumentException($"{description} must be an absolute path", parameterName);
        }

        var normalizedPath = NormalizeExtendedPathPrefix(path);
        if (IsNetworkPath(normalizedPath))
        {
            throw new ArgumentException(
                $"{description} must be a local path. Network paths are not allowed.",
                parameterName);
        }

        var fullPath = Path.GetFullPath(normalizedPath);
        if (IsNetworkPath(fullPath) || IsMappedNetworkDrive(fullPath, driveTypeResolver))
        {
            throw new ArgumentException(
                $"{description} must be a local path. Network paths are not allowed.",
                parameterName);
        }

        if (ContainsReparsePointInPathChain(fullPath, pathExistsResolver, attributesResolver))
        {
            throw new ArgumentException(
                $"{description} must not traverse symbolic links or reparse points.",
                parameterName);
        }

        return fullPath;
    }

    public static void PrepareDirectory(string directoryPath)
        => PrepareDirectory(directoryPath, "certificate directory");

    internal static void PrepareDirectory(string directoryPath, string description)
    {
        directoryPath = ResolveAndValidateLocalPath(directoryPath, nameof(directoryPath), description);

        var currentUserSid = GetCurrentUserSid();
        var directoryInfo = new DirectoryInfo(directoryPath);

        if (directoryInfo.Exists)
        {
            EnsureTrustedOwner(directoryInfo.GetAccessControl(), currentUserSid, description);
        }
        else
        {
            directoryInfo.Create();
        }

        var hardenedSecurity = CreateDirectorySecurity(currentUserSid);
        directoryInfo.SetAccessControl(hardenedSecurity);

        ValidateNoBroadWriteAccess(directoryInfo.GetAccessControl(), description);
    }

    public static void PrepareExistingFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var currentUserSid = GetCurrentUserSid();
        var fileInfo = new FileInfo(path);
        EnsureTrustedOwner(fileInfo.GetAccessControl(), currentUserSid, description);
        fileInfo.SetAccessControl(CreateFileSecurity(currentUserSid));
        ValidateNoBroadWriteAccess(fileInfo.GetAccessControl(), description);
    }

    public static void ApplyFileSecurity(string path)
    {
        var fileInfo = new FileInfo(path);
        fileInfo.SetAccessControl(CreateFileSecurity(GetCurrentUserSid()));
    }

    private static SecurityIdentifier GetCurrentUserSid()
    {
        return WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Failed to resolve the current Windows user for certificate directory security.");
    }

    private static void EnsureTrustedOwner(ObjectSecurity security, SecurityIdentifier currentUserSid, string description)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner == null)
        {
            throw new InvalidOperationException($"Failed to resolve the owner for the {description}.");
        }

        if (IsTrustedOwner(owner, currentUserSid))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The {description} must be owned by the current user, SYSTEM, or Administrators.");
    }

    private static DirectorySecurity CreateDirectorySecurity(SecurityIdentifier currentUserSid)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUserSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            LocalSystemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            AdministratorsSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static FileSecurity CreateFileSecurity(SecurityIdentifier currentUserSid)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUserSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            LocalSystemSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            AdministratorsSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    private static void ValidateNoBroadWriteAccess(CommonObjectSecurity security, string description)
    {
        var broadWriteRule = security
            .GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .FirstOrDefault(rule =>
                rule.AccessControlType == AccessControlType.Allow
                && IsBroadPrincipal(rule.IdentityReference as SecurityIdentifier)
                && (rule.FileSystemRights & DangerousWriteRights) != 0);

        if (broadWriteRule == null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The {description} grants write access to an overly broad Windows principal: {broadWriteRule.IdentityReference.Value}.");
    }

    private static bool IsBroadPrincipal(SecurityIdentifier? sid)
    {
        if (sid == null)
        {
            return false;
        }

        return IsBroadWritePrincipal(sid);
    }

    internal static bool IsTrustedOwner(SecurityIdentifier? ownerSid, SecurityIdentifier currentUserSid)
    {
        if (ownerSid == null)
        {
            return false;
        }

        return ownerSid.Equals(currentUserSid)
            || ownerSid.Equals(LocalSystemSid)
            || ownerSid.Equals(AdministratorsSid);
    }

    internal static bool IsBroadWritePrincipal(SecurityIdentifier? sid)
    {
        if (sid == null)
        {
            return false;
        }

        return sid.Equals(EveryoneSid)
            || sid.Equals(AuthenticatedUsersSid)
            || sid.Equals(BuiltinUsersSid)
            || sid.Equals(BuiltinGuestsSid)
            || sid.Equals(AnonymousSid)
            || sid.Equals(NetworkSid);
    }

    internal static bool IsMappedNetworkDrive(string fullPath, Func<string, DriveType>? driveTypeResolver = null)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) || IsNetworkPath(root))
        {
            return false;
        }

        try
        {
            driveTypeResolver ??= static rootPath => new DriveInfo(rootPath).DriveType;
            return driveTypeResolver(root) == DriveType.Network;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool ContainsReparsePointInPathChain(
        string fullPath,
        Func<string, bool>? pathExistsResolver = null,
        Func<string, FileAttributes>? attributesResolver = null)
    {
        pathExistsResolver ??= static candidate => Directory.Exists(candidate) || File.Exists(candidate);
        attributesResolver ??= static candidate => File.GetAttributes(candidate);

        foreach (var candidate in EnumerateExistingPathChain(fullPath, pathExistsResolver))
        {
            if ((attributesResolver(candidate) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return path.Length >= 7
                && char.IsLetter(path[4])
                && path[5] == ':'
                && (path[6] == Path.DirectorySeparatorChar || path[6] == Path.AltDirectorySeparatorChar);
        }

        if (IsNetworkPath(path))
        {
            return true;
        }

        return path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == Path.DirectorySeparatorChar || path[2] == Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeExtendedPathPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path.Substring(8);
        }

        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring(4);
        }

        return path;
    }

    private static IEnumerable<string> EnumerateExistingPathChain(string fullPath, Func<string, bool> pathExistsResolver)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            yield break;
        }

        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!pathExistsResolver(normalizedRoot))
        {
            yield break;
        }

        yield return normalizedRoot;

        var remainder = fullPath.Substring(root.Length)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var currentPath = normalizedRoot;

        foreach (var segment in remainder)
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!pathExistsResolver(currentPath))
            {
                yield break;
            }

            yield return currentPath;
        }
    }
}
