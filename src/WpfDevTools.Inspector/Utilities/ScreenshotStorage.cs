using System.IO;
using System.Security.Cryptography;

namespace WpfDevTools.Inspector.Utilities;

internal static class ScreenshotStorage
{
    private const string ScreenshotExtension = ".png";

    public static ScreenshotFile WritePng(byte[] imageBytes)
    {
        if (imageBytes == null)
        {
            throw new ArgumentNullException(nameof(imageBytes));
        }

        var screenshotId = $"shot_{Guid.NewGuid():N}";
        var directory = Path.Combine(Path.GetTempPath(), "WpfDevTools", "Screenshots");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, screenshotId + ScreenshotExtension);
        File.WriteAllBytes(path, imageBytes);

        return new ScreenshotFile(
            screenshotId,
            path,
            ComputeSha256Hex(imageBytes));
    }

    private static string ComputeSha256Hex(byte[] imageBytes)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(imageBytes);
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    internal sealed record ScreenshotFile(string ScreenshotId, string Path, string Sha256);
}
