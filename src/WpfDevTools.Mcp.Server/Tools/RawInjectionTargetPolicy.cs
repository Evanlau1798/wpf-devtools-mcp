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
            TryResolvePhysicalPath);

    internal static RawInjectionAuthorization Authorize(
        WpfProcessInfo processInfo,
        string _,
        string? configuredAllowedTargets,
        Func<string, string?> tryResolvePhysicalPath)
    {
        if (!TryNormalizeAbsolutePath(processInfo.ExecutablePath, tryResolvePhysicalPath, out var normalizedTargetPath))
        {
            return new RawInjectionAuthorization(
                IsAllowed: false,
                Error: "Raw injection is blocked because the target executable path is missing or not an absolute path. Start the target-side SDK host with InspectorSdk.Initialize() or allowlist the exact executable path before retrying connect().",
                Hint: $"Set {McpServerConfiguration.RawInjectionAllowedTargetsEnvVar} to a semicolon-separated list of exact absolute executable paths when raw injection into a specific target executable is explicitly intended.");
        }

        var configuredTargets = GetConfiguredAllowedTargets(configuredAllowedTargets, tryResolvePhysicalPath);
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
            Hint: $"Start the target-side SDK host with InspectorSdk.Initialize() for the safer reuse path, or add the exact absolute executable path to {McpServerConfiguration.RawInjectionAllowedTargetsEnvVar} before retrying connect(). The full denied path is written only to server diagnostics.");
    }

    internal static IReadOnlyCollection<string> GetConfiguredAllowedTargets()
        => GetConfiguredAllowedTargets(
            Environment.GetEnvironmentVariable(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar),
            TryResolvePhysicalPath);

    internal static IReadOnlyCollection<string> GetConfiguredAllowedTargets(
        string? configuredValue,
        Func<string, string?> tryResolvePhysicalPath)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return Array.Empty<string>();
        }

        var normalizedPaths = configuredValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => TryNormalizeAbsolutePath(path, tryResolvePhysicalPath))
            .Where(path => path != null)
            .Distinct(PathComparer)
            .ToArray();

        return new ReadOnlyCollection<string>(normalizedPaths!);
    }

    internal static bool TryNormalizeAbsolutePath(
        string? path,
        Func<string, string?> tryResolvePhysicalPath,
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

            var fullPath = Path.GetFullPath(path);

            normalizedPath = NormalizePath(tryResolvePhysicalPath(fullPath) ?? fullPath);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"RawInjectionTargetPolicy path normalization failed: {ex.Message}");
            return false;
        }
    }

    private static string? TryNormalizeAbsolutePath(
        string? path,
        Func<string, string?> tryResolvePhysicalPath)
        => TryNormalizeAbsolutePath(path, tryResolvePhysicalPath, out var normalizedPath)
            ? normalizedPath
            : null;

    internal static string? TryResolvePhysicalPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return NormalizePath(Path.GetFullPath(path));
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
            return null;
        }

        var builder = new StringBuilder(512);
        var result = GetFinalPathNameByHandle(handle, builder, builder.Capacity, 0);
        if (result == 0)
        {
            return null;
        }

        if (result >= builder.Capacity)
        {
            builder.EnsureCapacity((int)result + 1);
            result = GetFinalPathNameByHandle(handle, builder, builder.Capacity, 0);
            if (result == 0)
            {
                return null;
            }
        }

        return NormalizePath(TrimDevicePrefix(builder.ToString()));
    }

    private static string TrimDevicePrefix(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";

        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        if (path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path[devicePrefix.Length..];
        }

        return path;
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
