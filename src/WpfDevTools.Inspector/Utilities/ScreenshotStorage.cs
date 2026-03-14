using System.IO;
using System.Security.Cryptography;

namespace WpfDevTools.Inspector.Utilities;

internal static class ScreenshotStorage
{
    private const string ScreenshotExtension = ".png";
    private const string ProductDirectoryName = "WpfDevTools";
    private const string TempDirectoryName = "tmp";
    private const string ScreenshotDirectoryName = "screenshots";

    public static ScreenshotFile WritePng(byte[] imageBytes)
    {
        if (imageBytes == null)
        {
            throw new ArgumentNullException(nameof(imageBytes));
        }

        var screenshotId = $"shot_{Guid.NewGuid():N}";
        var directory = GetScreenshotDirectory();
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, screenshotId + ScreenshotExtension);
        File.WriteAllBytes(path, imageBytes);

        return new ScreenshotFile(
            screenshotId,
            path,
            ComputeSha256Hex(imageBytes));
    }

    private static string GetScreenshotDirectory()
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

    private static string ComputeSha256Hex(byte[] imageBytes)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(imageBytes);
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    internal sealed record ScreenshotFile(string ScreenshotId, string Path, string Sha256);
}
