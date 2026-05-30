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
}
