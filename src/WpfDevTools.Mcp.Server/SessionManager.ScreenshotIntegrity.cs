namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    private static bool IsValidScreenshotId(string screenshotId)
    {
        if (screenshotId.Length != 37 ||
            !screenshotId.StartsWith("shot_", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 5; index < screenshotId.Length; index++)
        {
            var character = screenshotId[index];
            if ((character < '0' || character > '9') &&
                (character < 'a' || character > 'f') &&
                (character < 'A' || character > 'F'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidScreenshotFileName(string fileName) =>
        fileName.EndsWith(ScreenshotFileExtension, StringComparison.OrdinalIgnoreCase)
        && IsValidScreenshotId(Path.GetFileNameWithoutExtension(fileName));

    private static ScreenshotResourceReader OpenVerifiedScreenshotFile(string fullPath, string? sha256)
    {
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Screenshot file does not exist.", fullPath);
        }

        if (string.IsNullOrWhiteSpace(sha256))
        {
            throw new ArgumentException("Screenshot SHA-256 digest is required.", nameof(sha256));
        }

        var normalizedSha256 = sha256.Trim();
        if (!IsLowerOrUpperHexDigest(normalizedSha256))
        {
            throw new ArgumentException(
                "Screenshot SHA-256 digest must be a 64-character hex string.",
                nameof(sha256));
        }

        return ScreenshotResourceReader.OpenVerified(fullPath, normalizedSha256);
    }

    private static bool IsLowerOrUpperHexDigest(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
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
