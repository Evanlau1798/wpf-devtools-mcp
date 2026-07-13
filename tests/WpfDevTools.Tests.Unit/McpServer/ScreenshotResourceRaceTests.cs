using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ScreenshotResourceRaceTests
{
    [Fact]
    public async Task RegisterScreenshotResource_WhenDisposeWinsAfterReaderOpen_ShouldDeleteOwnedOrphan()
    {
        var sessionManager = new SessionManager();
        const int processId = 12345;
        const string screenshotId = "shot_0123456789abcdef0123456789abcdef";
        var opened = new ManualResetEventSlim();
        var resume = new ManualResetEventSlim();
        var previousHook = ScreenshotResourceReader.OpenedForTesting;
        Task<Exception>? registration = null;
        var storageRoot = string.Empty;

        try
        {
            sessionManager.AddSession(processId);
            sessionManager.TryGetSessionGeneration(processId, out var sessionGeneration).Should().BeTrue();
            storageRoot = sessionManager.GetOrCreateScreenshotStorageRoot(processId, sessionGeneration);
            var filePath = Path.Combine(storageRoot, screenshotId + ".png");
            var imageBytes = new byte[] { 137, 80, 78, 71 };
            File.WriteAllBytes(filePath, imageBytes);
            var sha256 = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
            ScreenshotResourceReader.OpenedForTesting = () =>
            {
                opened.Set();
                resume.Wait(TimeSpan.FromSeconds(10));
            };

            registration = Task.Run(() => Record.Exception(() => sessionManager.RegisterScreenshotResource(
                processId,
                sessionGeneration,
                screenshotId,
                filePath,
                sha256)));
            opened.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            sessionManager.Dispose();
            resume.Set();
            var exception = await registration.WaitAsync(TimeSpan.FromSeconds(5));

            exception.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Contain("original active session");
            File.Exists(filePath).Should().BeFalse();
            Directory.Exists(storageRoot).Should().BeFalse();
        }
        finally
        {
            resume.Set();
            ScreenshotResourceReader.OpenedForTesting = previousHook;
            if (registration is not null)
            {
                await registration.WaitAsync(TimeSpan.FromSeconds(5));
            }

            opened.Dispose();
            resume.Dispose();
            sessionManager.Dispose();
            if (!string.IsNullOrWhiteSpace(storageRoot) && Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

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
