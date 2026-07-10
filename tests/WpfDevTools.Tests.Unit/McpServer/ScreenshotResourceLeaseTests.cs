using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed partial class ScreenshotResourceTests
{
    [Fact]
    public void DetachedScreenshotResource_WhenProcessIdIsReused_ShouldRemainReadable()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();
        const int processId = 12345;
        var screenshotId = RegisterValidScreenshotWithPath(
            sessionManager,
            tempDirectory.Path,
            processId,
            out var filePath);
        var detachedRoot = Path.GetDirectoryName(filePath)!;

        sessionManager.DetachScreenshotResource(processId, screenshotId).Should().BeTrue();
        sessionManager.RemoveSession(processId);
        var reusedProcessRoot = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
        sessionManager.RemoveSession(processId);

        reusedProcessRoot.Should().NotBe(detachedRoot);
        ScreenshotResources.GetScreenshotPng(sessionManager, screenshotId)
            .Should().BeOfType<ModelContextProtocol.Protocol.BlobResourceContents>();

        sessionManager.Dispose();
        Directory.Exists(detachedRoot).Should().BeFalse(
            "disposing the MCP session should remove detached preview lease roots");
    }
}
