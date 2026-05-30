using System.IO;
using System.Security.Cryptography;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Inspector.Utilities;

internal static class ScreenshotStorage
{
    internal const string DirectoryEnvironmentVariable = "WPFDEVTOOLS_SCREENSHOT_DIR";
    internal const int MaxInlineEncodedPngBytes = 256 * 1024;
    internal const int MaxEncodedPngBytes = 6 * 1024 * 1024;
    internal const int MaxStoredScreenshots = 100;
    internal static readonly TimeSpan RetentionMaxAge = TimeSpan.FromHours(24);

    private const string ScreenshotExtension = ".png";
    private const string ProductDirectoryName = "WpfDevTools";
    private const string TempDirectoryName = "tmp";
    private const string ScreenshotDirectoryName = "screenshots";

    public static ScreenshotFile WritePng(byte[] imageBytes, string? directoryOverride = null)
    {
        if (imageBytes == null)
        {
            throw new ArgumentNullException(nameof(imageBytes));
        }

        if (imageBytes.Length > MaxEncodedPngBytes)
        {
            throw new InvalidOperationException(
                $"Screenshot PNG payload length {imageBytes.Length} exceeds the {MaxEncodedPngBytes} byte retention limit.");
        }

        var screenshotId = $"shot_{Guid.NewGuid():N}";
        var directory = directoryOverride is null
            ? GetScreenshotDirectory()
            : ValidateDirectoryOverride(directoryOverride);
        Directory.CreateDirectory(directory);
        CleanupExpiredScreenshots(directory, DateTimeOffset.UtcNow);

        var path = Path.Combine(directory, screenshotId + ScreenshotExtension);
        File.WriteAllBytes(path, imageBytes);
        CleanupExpiredScreenshots(directory, DateTimeOffset.UtcNow, path);

        return new ScreenshotFile(
            screenshotId,
            path,
            ComputeSha256Hex(imageBytes));
    }

    private static void CleanupExpiredScreenshots(string directory, DateTimeOffset now, string? protectedPath = null)
    {
        var fullProtectedPath = protectedPath is null ? null : Path.GetFullPath(protectedPath);
        var candidates = Directory.EnumerateFiles(directory, "shot_*" + ScreenshotExtension)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        var retainedUnprotectedLimit = MaxStoredScreenshots -
            (candidates.Any(file => IsProtected(file, fullProtectedPath)) ? 1 : 0);
        var retainedUnprotectedCount = 0;

        for (var index = 0; index < candidates.Length; index++)
        {
            var file = candidates[index];
            var age = now - new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            if (IsProtected(file, fullProtectedPath))
            {
                continue;
            }

            var exceedsCount = retainedUnprotectedCount >= retainedUnprotectedLimit;
            if (age > RetentionMaxAge || exceedsCount)
            {
                TryDelete(file);
                continue;
            }

            retainedUnprotectedCount++;
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsProtected(FileInfo file, string? fullProtectedPath)
        => fullProtectedPath is not null &&
           string.Equals(file.FullName, fullProtectedPath, StringComparison.OrdinalIgnoreCase);

    private static string GetScreenshotDirectory()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return ValidateConfiguredDirectory(configuredDirectory);
        }

        return GetDefaultScreenshotDirectory();
    }

    internal static string GetDefaultScreenshotDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("LocalApplicationData path is unavailable.");
        }

        return Path.Combine(
            localApplicationData,
            ProductDirectoryName,
            TempDirectoryName,
            ScreenshotDirectoryName);
    }

    private static string ValidateConfiguredDirectory(string configuredDirectory)
    {
        var root = Path.GetPathRoot(configuredDirectory);
        if (string.IsNullOrWhiteSpace(root) ||
            root.Length < 3 ||
            root.StartsWith(@"\\", StringComparison.Ordinal) ||
            !root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{DirectoryEnvironmentVariable} must be an absolute local directory path.");
        }

        return Path.GetFullPath(configuredDirectory);
    }

    private static string ValidateDirectoryOverride(string directoryOverride)
    {
        var fullPath = CertificateStorageSecurity.ResolveAndValidateLocalPath(
            directoryOverride,
            nameof(directoryOverride),
            "Screenshot directory override");
        var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        if (!ScreenshotLeasePaths.IsPathWithinProcessRoot(fullPath, Path.GetTempPath(), processId))
        {
            throw new ScreenshotDirectoryOverrideException(
                "Screenshot directory override must be under the MCP server-issued screenshot lease root.");
        }

        return fullPath;
    }

    private static string ComputeSha256Hex(byte[] imageBytes)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(imageBytes);
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    internal sealed record ScreenshotFile(string ScreenshotId, string Path, string Sha256);

    internal sealed class ScreenshotDirectoryOverrideException : InvalidOperationException
    {
        public ScreenshotDirectoryOverrideException(string message) : base(message)
        {
        }
    }
}
