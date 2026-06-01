using System.Security.Cryptography;

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

    private static string ValidateScreenshotFileAndSha256(string fullPath, string? sha256)
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

        var actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
        if (!string.Equals(actualSha256, normalizedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Screenshot file failed SHA-256 verification during registration.");
        }

        return normalizedSha256.ToLowerInvariant();
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
