using System.Security.Cryptography;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ScreenshotResourceTests
{
    [Fact]
    public void GetScreenshotPng_ShouldReturnRegisteredImageAsPngBlob()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();
        var screenshotId = "shot_0123456789abcdef0123456789abcdef";
        var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var filePath = Path.Combine(tempDirectory.Path, screenshotId + ".png");
        File.WriteAllBytes(filePath, imageBytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
        sessionManager.RegisterScreenshotResource(processId: 12345, screenshotId, filePath, sha256);

        var result = ScreenshotResources.GetScreenshotPng(sessionManager, screenshotId);

        var blob = result.Should().BeOfType<BlobResourceContents>().Subject;
        blob.Uri.Should().Be("wpf://screenshots/shot_0123456789abcdef0123456789abcdef");
        blob.MimeType.Should().Be("image/png");
        blob.DecodedData.ToArray().Should().Equal(imageBytes);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wpf-devtools-screenshot-resource-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
