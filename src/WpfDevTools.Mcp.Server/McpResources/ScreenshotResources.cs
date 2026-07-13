using System.ComponentModel;
using System.Security.Cryptography;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WpfDevTools.Mcp.Server.McpResources;

[McpServerResourceType]
public static class ScreenshotResources
{
    internal const int MaxChunkBytes = 16 * 1024;

    [McpServerResource(
        Name = "wpf_screenshot_png",
        Title = "WPF Screenshot PNG",
        UriTemplate = "wpf://screenshots/{screenshotId}",
        MimeType = "image/png")]
    [Description("Reads a retained PNG captured by element_screenshot outputMode=file using the returned screenshotId/resourceUri.")]
    public static ResourceContents GetScreenshotPng(SessionManager sessionManager, string screenshotId)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        if (!sessionManager.TryGetScreenshotResource(screenshotId, out var screenshot))
        {
            throw ScreenshotResourceNotFound();
        }

        var imageBytes = ReadVerifiedScreenshotBytes(sessionManager, screenshot);

        return BlobResourceContents.FromBytes(imageBytes, screenshot.ResourceUri, "image/png");
    }

    [McpServerResource(
        Name = "wpf_screenshot_png_chunk",
        Title = "WPF Screenshot PNG Chunk",
        UriTemplate = "wpf://screenshots/{screenshotId}/chunks/{offset}/{length}",
        MimeType = "application/octet-stream")]
    [Description("Reads a verified byte range from a retained PNG when a client cannot receive the complete resource in one response.")]
    public static ResourceContents GetScreenshotPngChunk(
        SessionManager sessionManager,
        string screenshotId,
        int offset,
        int length)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        if (!sessionManager.TryGetScreenshotResource(screenshotId, out var screenshot))
        {
            throw ScreenshotResourceNotFound();
        }

        var imageBytes = ReadVerifiedScreenshotBytes(sessionManager, screenshot);
        if (offset < 0 || offset >= imageBytes.Length || length <= 0 || length > MaxChunkBytes)
        {
            throw new McpProtocolException(
                $"Screenshot chunk requires offset within the {imageBytes.Length}-byte resource and length from 1 to {MaxChunkBytes} bytes.",
                McpErrorCode.InvalidParams);
        }

        var chunkLength = Math.Min(length, imageBytes.Length - offset);
        var chunk = imageBytes.AsSpan(offset, chunkLength).ToArray();
        var resourceUri = $"wpf://screenshots/{screenshotId}/chunks/{offset}/{length}";
        return BlobResourceContents.FromBytes(chunk, resourceUri, "application/octet-stream");
    }

    private static byte[] ReadVerifiedScreenshotBytes(
        SessionManager sessionManager,
        StoredScreenshotResource screenshot)
    {
        var imageBytes = ReadScreenshotBytes(sessionManager, screenshot);
        if (!string.IsNullOrWhiteSpace(screenshot.Sha256))
        {
            var actualSha256 = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
            if (!string.Equals(actualSha256, screenshot.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new McpProtocolException(
                    "Screenshot resource failed integrity verification.",
                    McpErrorCode.InternalError);
            }
        }

        return imageBytes;
    }

    private static byte[] ReadScreenshotBytes(
        SessionManager sessionManager,
        StoredScreenshotResource screenshot)
    {
        try
        {
            var filePath = sessionManager.ResolveScreenshotResourcePathForRead(screenshot);
            return File.ReadAllBytes(filePath);
        }
        catch (FileNotFoundException ex)
        {
            throw ScreenshotResourceNoLongerAvailable(ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw ScreenshotResourceNoLongerAvailable(ex);
        }
        catch (IOException ex)
        {
            throw new McpProtocolException(
                "Screenshot resource could not be read.",
                ex,
                McpErrorCode.InternalError);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new McpProtocolException(
                "Screenshot resource could not be read.",
                ex,
                McpErrorCode.InternalError);
        }
        catch (ArgumentException ex)
        {
            throw ScreenshotResourceFailedInternalValidation(ex);
        }
        catch (InvalidOperationException ex) when (ex is not ObjectDisposedException)
        {
            throw ScreenshotResourceFailedInternalValidation(ex);
        }
    }

    private static McpProtocolException ScreenshotResourceNotFound()
        => new(
            "Screenshot resource is not retained in this MCP session.",
            McpErrorCode.ResourceNotFound);

    private static McpProtocolException ScreenshotResourceNoLongerAvailable(Exception innerException)
        => new(
            "Screenshot resource is no longer available in this MCP session.",
            innerException,
            McpErrorCode.ResourceNotFound);

    private static McpProtocolException ScreenshotResourceFailedInternalValidation(Exception innerException)
        => new(
            "Screenshot resource failed internal validation.",
            innerException,
            McpErrorCode.InternalError);
}
