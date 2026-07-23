using System.Security.Cryptography;

namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class PackArchiveInput
{
    public static string ComputeSha256(Stream archiveStream, PackImportLimits limits)
    {
        Validate(archiveStream, limits);
        archiveStream.Position = 0;
        var digest = SHA256.HashData(archiveStream);
        archiveStream.Position = 0;
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static async Task<string> ComputeSha256Async(
        Stream archiveStream,
        PackImportLimits limits,
        CancellationToken cancellationToken)
    {
        Validate(archiveStream, limits);
        archiveStream.Position = 0;
        var digest = await SHA256.HashDataAsync(archiveStream, cancellationToken)
            .ConfigureAwait(false);
        archiveStream.Position = 0;
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static void Validate(Stream archiveStream, PackImportLimits limits)
    {
        if (!archiveStream.CanRead || !archiveStream.CanSeek)
        {
            throw new ArgumentException(
                "Archive stream must be readable and seekable.",
                nameof(archiveStream));
        }

        if (archiveStream.Length > limits.MaxArchiveBytes)
        {
            throw new InvalidDataException("Archive compressed size is too large.");
        }
    }
}
