using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WpfDevTools.Injector.Discovery;

namespace WpfDevTools.Mcp.Server.Tools;

internal readonly record struct RawInjectionAuthorization(
    bool IsAllowed,
    string? Error,
    string? Hint);

internal readonly record struct PhysicalPathResolution(
    bool IsResolved,
    bool IsRejected,
    string? Path)
{
    internal static PhysicalPathResolution Resolved(string path)
        => new(IsResolved: true, IsRejected: false, Path: path);

    internal static PhysicalPathResolution Unresolved()
        => new(IsResolved: false, IsRejected: false, Path: null);

    internal static PhysicalPathResolution Rejected()
        => new(IsResolved: false, IsRejected: true, Path: null);
}

internal static class RawInjectionTargetPolicy
{
    internal static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    public static bool IsAllowed(WpfProcessInfo processInfo)
        => Authorize(processInfo).IsAllowed;

    public static RawInjectionAuthorization Authorize(WpfProcessInfo processInfo)
        => Authorize(
            processInfo,
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar),
            ResolvePhysicalPathForPolicy);

    internal static RawInjectionAuthorization Authorize(
        WpfProcessInfo processInfo,
        string _,
        string? configuredAllowedTargets,
        Func<string, string?> tryResolvePhysicalPath)
        => Authorize(
            processInfo,
            _,
            configuredAllowedTargets,
            ToPhysicalPathResolver(tryResolvePhysicalPath));

    private static RawInjectionAuthorization Authorize(
        WpfProcessInfo processInfo,
        string _,
        string? configuredAllowedTargets,
        Func<string, PhysicalPathResolution> resolvePhysicalPath)
    {
        if (!TryNormalizeAbsolutePath(processInfo.ExecutablePath, resolvePhysicalPath, out var normalizedTargetPath))
        {
            return new RawInjectionAuthorization(
                IsAllowed: false,
                Error: "Raw injection is blocked because the target executable path is missing or not a local absolute path. Start the target-side SDK host with InspectorSdk.Initialize() or allowlist the exact local executable path before retrying connect().",
                Hint: $"Set {McpServerConfiguration.RawInjectionAllowedTargetsEnvVar} to a semicolon-separated list of exact local absolute executable paths when raw injection into a specific target executable is explicitly intended.");
        }

        if (!TryGetConfiguredAllowedTargets(configuredAllowedTargets, resolvePhysicalPath, out var configuredTargets))
        {
            return CreateInvalidConfigurationAuthorization();
        }

        if (configuredTargets.Contains(normalizedTargetPath, PathComparer))
        {
            return new RawInjectionAuthorization(
                IsAllowed: true,
                Error: null,
                Hint: null);
        }

        return new RawInjectionAuthorization(
            IsAllowed: false,
            Error: "Raw injection into the target is blocked by the server's target policy. Raw injection requires an exact allowlist entry for the target executable.",
            Hint: $"Start the target-side SDK host with InspectorSdk.Initialize() for the safer reuse path, or add the exact local absolute executable path to {McpServerConfiguration.RawInjectionAllowedTargetsEnvVar} before retrying connect(). The full denied path is written only to server diagnostics.");
    }

    internal static IReadOnlyCollection<string> GetConfiguredAllowedTargets()
        => GetConfiguredAllowedTargets(
            Environment.GetEnvironmentVariable(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar),
            ResolvePhysicalPathForPolicy);

    internal static IReadOnlyCollection<string> GetConfiguredAllowedTargets(
        string? configuredValue,
        Func<string, string?> tryResolvePhysicalPath)
        => GetConfiguredAllowedTargets(
            configuredValue,
            ToPhysicalPathResolver(tryResolvePhysicalPath));

    private static IReadOnlyCollection<string> GetConfiguredAllowedTargets(
        string? configuredValue,
        Func<string, PhysicalPathResolution> resolvePhysicalPath)
        => TryGetConfiguredAllowedTargets(configuredValue, resolvePhysicalPath, out var configuredTargets)
            ? configuredTargets
            : Array.Empty<string>();

    private static bool TryGetConfiguredAllowedTargets(
        string? configuredValue,
        Func<string, string?> tryResolvePhysicalPath,
        out IReadOnlyCollection<string> configuredTargets)
        => TryGetConfiguredAllowedTargets(
            configuredValue,
            ToPhysicalPathResolver(tryResolvePhysicalPath),
            out configuredTargets);

    private static bool TryGetConfiguredAllowedTargets(
        string? configuredValue,
        Func<string, PhysicalPathResolution> resolvePhysicalPath,
        out IReadOnlyCollection<string> configuredTargets)
    {
        configuredTargets = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return true;
        }

        var configuredTargetEntries = configuredValue.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (configuredTargetEntries.Length == 0)
        {
            return false;
        }

        var normalizedPaths = new HashSet<string>(PathComparer);
        foreach (var configuredTargetEntry in configuredTargetEntries)
        {
            if (!TryNormalizeAbsolutePath(
                    configuredTargetEntry,
                    resolvePhysicalPath,
                    out var normalizedConfiguredTarget))
            {
                configuredTargets = Array.Empty<string>();
                return false;
            }

            normalizedPaths.Add(normalizedConfiguredTarget);
        }

        configuredTargets = new ReadOnlyCollection<string>(normalizedPaths.ToArray());
        return true;
    }

    private static RawInjectionAuthorization CreateInvalidConfigurationAuthorization()
        => new(
            IsAllowed: false,
            Error: "Invalid raw injection allowlist configuration. Every configured entry must be an exact local absolute executable path.",
            Hint: $"Fix {McpServerConfiguration.RawInjectionAllowedTargetsEnvVar} to a semicolon-separated list of exact local absolute executable paths, then restart the MCP server.");

    internal static bool TryNormalizeAbsolutePath(
        string? path,
        Func<string, string?> tryResolvePhysicalPath,
        out string normalizedPath)
        => TryNormalizeAbsolutePath(
            path,
            ToPhysicalPathResolver(tryResolvePhysicalPath),
            out normalizedPath);

    internal static bool TryNormalizeAbsolutePath(
        string? path,
        Func<string, PhysicalPathResolution> resolvePhysicalPath,
        out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return false;
            }

            if (IsRejectedTargetPath(path))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(path);
            if (IsRejectedTargetPath(fullPath))
            {
                return false;
            }

            var physicalPathResolution = resolvePhysicalPath(fullPath);
            if (physicalPathResolution.IsRejected)
            {
                return false;
            }

            var resolvedPath = physicalPathResolution.IsResolved
                ? physicalPathResolution.Path
                : fullPath;
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return false;
            }

            if (IsRejectedTargetPath(resolvedPath))
            {
                return false;
            }

            normalizedPath = NormalizePath(resolvedPath);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"RawInjectionTargetPolicy path normalization failed: {ex.Message}");
            return false;
        }
    }

    internal static bool AreSameExecutablePath(string? firstPath, string? secondPath)
        => TryCompareExecutablePath(firstPath, secondPath, out var areSame) && areSame;

    internal static bool TryCompareExecutablePath(
        string? firstPath,
        string? secondPath,
        out bool areSame)
    {
        areSame = false;
        if (!TryNormalizeAbsolutePath(firstPath, ResolvePhysicalPathForPolicy, out var normalizedFirstPath)
            || !TryNormalizeAbsolutePath(secondPath, ResolvePhysicalPathForPolicy, out var normalizedSecondPath))
        {
            return false;
        }

        areSame = PathComparer.Equals(normalizedFirstPath, normalizedSecondPath);
        return true;
    }

    private static string? TryNormalizeAbsolutePath(
        string? path,
        Func<string, string?> tryResolvePhysicalPath)
        => TryNormalizeAbsolutePath(path, tryResolvePhysicalPath, out var normalizedPath)
            ? normalizedPath
            : null;

    private static Func<string, PhysicalPathResolution> ToPhysicalPathResolver(
        Func<string, string?> tryResolvePhysicalPath)
        => path =>
        {
            var resolvedPath = tryResolvePhysicalPath(path);
            return resolvedPath is null
                ? PhysicalPathResolution.Unresolved()
                : PhysicalPathResolution.Resolved(resolvedPath);
        };

    private static bool IsNetworkPath(string path)
        => path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
           || (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)
               && !path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase));

    private static bool IsRejectedTargetPath(string path)
    {
        if (IsNetworkPath(path))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return !TryGetWindowsLocalDriveRoot(path, out var driveRoot)
            || IsRejectedDriveRoot(driveRoot);
    }

    private static bool TryGetWindowsLocalDriveRoot(string path, out string driveRoot)
    {
        driveRoot = string.Empty;

        const string extendedDrivePrefix = @"\\?\";
        var candidate = path.StartsWith(extendedDrivePrefix, StringComparison.OrdinalIgnoreCase)
            ? path[extendedDrivePrefix.Length..]
            : path;

        if (candidate.Length < 3
            || candidate[1] != ':'
            || !IsDirectorySeparator(candidate[2])
            || !IsAsciiDriveLetter(candidate[0]))
        {
            return false;
        }

        driveRoot = char.ToUpperInvariant(candidate[0]) + @":\";
        return true;
    }

    private static bool IsDirectorySeparator(char value)
        => value is '\\' or '/';

    private static bool IsAsciiDriveLetter(char value)
        => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsRejectedDriveRoot(string root)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var driveType = new DriveInfo(root).DriveType;
            return driveType is DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(
                $"RawInjectionTargetPolicy drive type detection failed: {ex.GetType().Name}");
            return true;
        }
    }

    internal static string? TryResolvePhysicalPath(string path)
    {
        var resolution = ResolvePhysicalPathForPolicy(path);
        return resolution.IsResolved ? resolution.Path : null;
    }

    internal static PhysicalPathResolution ResolvePhysicalPathForPolicy(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return PhysicalPathResolution.Unresolved();
        }

        if (!OperatingSystem.IsWindows())
        {
            return PhysicalPathResolution.Resolved(NormalizePath(Path.GetFullPath(path)));
        }

        using var handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return PhysicalPathResolution.Unresolved();
        }

        var builder = new StringBuilder(512);
        var result = GetFinalPathNameByHandle(handle, builder, builder.Capacity, 0);
        if (result == 0)
        {
            return PhysicalPathResolution.Unresolved();
        }

        if (result >= builder.Capacity)
        {
            builder.EnsureCapacity((int)result + 1);
            result = GetFinalPathNameByHandle(handle, builder, builder.Capacity, 0);
            if (result == 0)
            {
                return PhysicalPathResolution.Unresolved();
            }
        }

        return TryNormalizeFinalPathName(builder.ToString(), out var normalizedPath)
            ? PhysicalPathResolution.Resolved(normalizedPath)
            : PhysicalPathResolution.Rejected();
    }

    internal static bool TryNormalizeFinalPathName(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";

        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = NormalizePath(@"\\" + path[uncPrefix.Length..]);
            return true;
        }

        if (path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var candidate = path[devicePrefix.Length..];
            if (!TryGetWindowsLocalDriveRoot(candidate, out _))
            {
                return false;
            }

            normalizedPath = NormalizePath(candidate);
            return true;
        }

        if (path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            || IsRejectedTargetPath(path))
        {
            return false;
        }

        normalizedPath = NormalizePath(path);
        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        int cchFilePath,
        uint dwFlags);

    private static string NormalizePath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
