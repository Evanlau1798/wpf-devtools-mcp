using System.Security.Cryptography;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ScreenshotResourceIntegrityTests
{
    private const int ProcessId = 12345;
    private const string ScreenshotId = "shot_0123456789abcdef0123456789abcdef";

    [Fact]
    public void RegisterScreenshotResource_WithMissingFile_ShouldReject()
    {
        using var sessionManager = new SessionManager();
        var storageRoot = sessionManager.GetOrCreateScreenshotStorageRoot(ProcessId);
        var filePath = Path.Combine(storageRoot, ScreenshotId + ".png");

        var act = () => sessionManager.RegisterScreenshotResource(
            ProcessId,
            ScreenshotId,
            filePath,
            sha256: ValidSha256For([137, 80, 78, 71]));

        act.Should()
            .Throw<FileNotFoundException>()
            .WithMessage("*Screenshot file*");
    }

    [Fact]
    public void RegisterScreenshotResource_WithMissingSha256_ShouldRejectAndDeleteOwnedOrphan()
    {
        using var sessionManager = new SessionManager();
        var filePath = WriteOwnedScreenshot(sessionManager, [137, 80, 78, 71]);

        var act = () => sessionManager.RegisterScreenshotResource(
            ProcessId,
            ScreenshotId,
            filePath,
            sha256: null);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*SHA-256*digest*required*");
        File.Exists(filePath).Should().BeFalse(
            "a file-mode screenshot without an integrity digest must not remain as an untracked server-owned orphan");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void RegisterScreenshotResource_WithMalformedSha256_ShouldRejectAndDeleteOwnedOrphan(string sha256)
    {
        using var sessionManager = new SessionManager();
        var filePath = WriteOwnedScreenshot(sessionManager, [137, 80, 78, 71]);

        var act = () => sessionManager.RegisterScreenshotResource(
            ProcessId,
            ScreenshotId,
            filePath,
            sha256);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*64-character hex*");
        File.Exists(filePath).Should().BeFalse(
            "malformed screenshot integrity metadata should fail closed before registration");
    }

    [Fact]
    public void RegisterScreenshotResource_WithMismatchedSha256_ShouldRejectAndDeleteOwnedOrphan()
    {
        using var sessionManager = new SessionManager();
        var filePath = WriteOwnedScreenshot(sessionManager, [137, 80, 78, 71]);

        var act = () => sessionManager.RegisterScreenshotResource(
            ProcessId,
            ScreenshotId,
            filePath,
            sha256: new string('0', 64));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*failed SHA-256 verification*");
        File.Exists(filePath).Should().BeFalse(
            "a screenshot with mismatched integrity metadata must not remain as a readable retained resource or orphan");
    }

    [Fact]
    public void GetScreenshotPng_WithFileChangedAfterRegistration_ShouldReject()
    {
        using var sessionManager = new SessionManager();
        var originalBytes = new byte[] { 137, 80, 78, 71 };
        var filePath = WriteOwnedScreenshot(sessionManager, originalBytes);
        sessionManager.RegisterScreenshotResource(
            ProcessId,
            ScreenshotId,
            filePath,
            ValidSha256For(originalBytes));
        File.WriteAllBytes(filePath, [1, 2, 3, 4]);

        var act = () => ScreenshotResources.GetScreenshotPng(sessionManager, ScreenshotId);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*failed SHA-256 verification*");
    }

    private static string WriteOwnedScreenshot(SessionManager sessionManager, byte[] imageBytes)
    {
        var storageRoot = sessionManager.GetOrCreateScreenshotStorageRoot(ProcessId);
        var filePath = Path.Combine(storageRoot, ScreenshotId + ".png");
        File.WriteAllBytes(filePath, imageBytes);
        return filePath;
    }

    private static string ValidSha256For(byte[] imageBytes)
        => Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
}
