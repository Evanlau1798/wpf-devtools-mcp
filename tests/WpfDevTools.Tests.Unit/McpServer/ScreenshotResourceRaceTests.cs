using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ScreenshotResourceRaceTests
{
    [Fact]
    public void RegisterScreenshotResource_WithStaleSessionGeneration_ShouldRejectAndDeleteOwnedOrphan()
    {
        using var sessionManager = new SessionManager();
        const int processId = 12345;
        const string screenshotId = "shot_0123456789abcdef0123456789abcdef";
        string? storageRoot = null;

        try
        {
            sessionManager.AddSession(processId);
            sessionManager.TryGetSessionGeneration(processId, out var sessionGeneration)
                .Should().BeTrue();
            storageRoot = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
            var filePath = Path.Combine(storageRoot, screenshotId + ".png");
            sessionManager.RemoveSession(processId);
            Directory.CreateDirectory(storageRoot);
            File.WriteAllBytes(filePath, new byte[] { 137, 80, 78, 71 });

            var act = () => sessionManager.RegisterScreenshotResource(
                processId,
                sessionGeneration,
                screenshotId,
                filePath,
                null);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*active session*");
            sessionManager.TryDeleteUnregisteredScreenshotFile(processId, filePath, storageRoot)
                .Should().BeTrue();
            File.Exists(filePath).Should().BeFalse(
                "stale file-mode responses remain server-owned only through their original lease root");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(storageRoot) && Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }
}
