using System.IO;
using System.Security.Cryptography;

namespace WpfDevTools.Inspector.Utilities;

internal static class ScreenshotStorage
{
    private const string ScreenshotExtension = ".png";

    public static ScreenshotFile WritePng(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var screenshotId = $"shot_{Guid.NewGuid():N}";
        var directory = Path.Combine(Path.GetTempPath(), "WpfDevTools", "Screenshots");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, screenshotId + ScreenshotExtension);
        File.WriteAllBytes(path, imageBytes);

        return new ScreenshotFile(
            screenshotId,
            path,
            Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant());
    }

    internal sealed record ScreenshotFile(string ScreenshotId, string Path, string Sha256);
}
