using System.ComponentModel;
using System.Security.Cryptography;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WpfDevTools.Mcp.Server.McpResources;

[McpServerResourceType]
public static class ScreenshotResources
{
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
            throw new InvalidOperationException($"Screenshot '{screenshotId}' is not retained in this MCP session.");
        }

        var filePath = sessionManager.ResolveScreenshotResourcePathForRead(screenshot);
        var imageBytes = File.ReadAllBytes(filePath);
        if (!string.IsNullOrWhiteSpace(screenshot.Sha256))
        {
            var actualSha256 = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
            if (!string.Equals(actualSha256, screenshot.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Screenshot '{screenshotId}' failed SHA-256 verification.");
            }
        }

        return BlobResourceContents.FromBytes(imageBytes, screenshot.ResourceUri, "image/png");
    }
}
