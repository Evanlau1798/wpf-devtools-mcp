using System.Globalization;

namespace WpfDevTools.Shared.Security;

internal static class ScreenshotLeasePaths
{
    internal const string RootDirectoryName = "wpf-devtools-mcp-screenshots";

    internal static string CreateStorageRootPath(string tempRoot, int processId, string leaseId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process ID must be positive.");
        }

        if (string.IsNullOrWhiteSpace(leaseId))
        {
            throw new ArgumentException("Screenshot lease ID cannot be empty.", nameof(leaseId));
        }

        return Path.GetFullPath(Path.Combine(
            tempRoot,
            RootDirectoryName,
            processId.ToString(CultureInfo.InvariantCulture),
            leaseId));
    }

    internal static bool IsPathWithinProcessRoot(string candidatePath, string tempRoot, int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        var processRoot = Path.GetFullPath(Path.Combine(
            tempRoot,
            RootDirectoryName,
            processId.ToString(CultureInfo.InvariantCulture)));
        var normalizedRoot = processRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(candidatePath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsStorageRootPathForProcess(string candidatePath, int processId)
    {
        if (processId <= 0 || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(candidatePath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var segments = normalizedPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
        {
            return false;
        }

        var rootDirectorySegment = segments[segments.Length - 3];
        var processIdSegment = segments[segments.Length - 2];
        var leaseIdSegment = segments[segments.Length - 1];

        return string.Equals(rootDirectorySegment, RootDirectoryName, StringComparison.Ordinal)
            && string.Equals(
                processIdSegment,
                processId.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            && IsValidLeaseId(leaseIdSegment);
    }

    private static bool IsValidLeaseId(string leaseId)
    {
        if (leaseId.Length != 32)
        {
            return false;
        }

        foreach (var character in leaseId)
        {
            if ((character < '0' || character > '9') &&
                (character < 'a' || character > 'f') &&
                (character < 'A' || character > 'F'))
            {
                return false;
            }
        }

        return true;
    }
}
