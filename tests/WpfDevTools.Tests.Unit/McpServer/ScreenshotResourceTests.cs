using System.Security.Cryptography;
using System.Reflection;
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
        const int processId = 12345;
        var screenshotId = "shot_0123456789abcdef0123456789abcdef";
        var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var storageRoot = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
        var filePath = Path.Combine(storageRoot, screenshotId + ".png");
        File.WriteAllBytes(filePath, imageBytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
        sessionManager.RegisterScreenshotResource(processId, screenshotId, filePath, sha256);

        var result = ScreenshotResources.GetScreenshotPng(sessionManager, screenshotId);

        var blob = result.Should().BeOfType<BlobResourceContents>().Subject;
        blob.Uri.Should().Be("wpf://screenshots/shot_0123456789abcdef0123456789abcdef");
        blob.MimeType.Should().Be("image/png");
        blob.DecodedData.ToArray().Should().Equal(imageBytes);
    }

    [Fact]
    public void GetScreenshotPng_WithMismatchedSha_ShouldThrowVerificationError()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();
        const int processId = 12345;
        var screenshotId = "shot_0123456789abcdef0123456789abcdef";
        var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var storageRoot = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
        var filePath = Path.Combine(storageRoot, screenshotId + ".png");
        File.WriteAllBytes(filePath, imageBytes);
        sessionManager.RegisterScreenshotResource(
            processId,
            screenshotId,
            filePath,
            sha256: new string('0', 64));

        var act = () => ScreenshotResources.GetScreenshotPng(sessionManager, screenshotId);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*failed SHA-256 verification*");
    }

    [Theory]
    [InlineData("../shot_0123456789abcdef0123456789abcdef")]
    [InlineData("shot_0123456789abcdef0123456789abcdef/x")]
    [InlineData("shot_not_hex")]
    public void RegisterScreenshotResource_WithInvalidScreenshotId_ShouldRejectResource(
        string screenshotId)
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();
        const int processId = 12345;
        var storageRoot = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
        var filePath = Path.Combine(storageRoot, "shot_0123456789abcdef0123456789abcdef.png");
        File.WriteAllBytes(filePath, new byte[] { 137, 80, 78, 71 });

        var act = () => sessionManager.RegisterScreenshotResource(
            processId,
            screenshotId,
            filePath,
            sha256: null);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*shot_<32 hex chars>*");
    }

    [Fact]
    public void RegisterScreenshotResource_PathOutsideServerOwnedRoot_ShouldRejectResource()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();
        const int processId = 12345;
        var screenshotId = "shot_0123456789abcdef0123456789abcdef";
        _ = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
        var outsidePath = Path.Combine(tempDirectory.Path, screenshotId + ".png");
        File.WriteAllBytes(outsidePath, new byte[] { 137, 80, 78, 71 });

        var act = () => sessionManager.RegisterScreenshotResource(
            processId,
            screenshotId,
            outsidePath,
            sha256: null);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*server-owned screenshot storage root*");
        File.Exists(outsidePath).Should().BeTrue(
            "rejected target-supplied paths must remain unowned by the server cleanup path");
    }

    [Fact]
    public void Dispose_AfterRejectedOutsideRootPath_ShouldNotDeleteUnownedFile()
    {
        using var tempDirectory = new TemporaryDirectory();
        var sessionManager = new SessionManager();
        const int processId = 12345;
        var screenshotId = "shot_0123456789abcdef0123456789abcdef";
        _ = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
        var outsidePath = Path.Combine(tempDirectory.Path, screenshotId + ".png");
        File.WriteAllBytes(outsidePath, new byte[] { 137, 80, 78, 71 });

        try
        {
            var act = () => sessionManager.RegisterScreenshotResource(
                processId,
                screenshotId,
                outsidePath,
                sha256: null);

            act.Should().Throw<ArgumentException>();
        }
        finally
        {
            sessionManager.Dispose();
        }

        File.Exists(outsidePath).Should().BeTrue(
            "server disposal must not delete arbitrary files outside the owned screenshot root");
    }

    [Fact]
    public void GetOrCreateScreenshotStorageRoot_WhenExistingRootBecomesReparsePoint_ShouldFailClosed()
    {
        var previousDetector = SessionManager.ScreenshotReparsePointChainDetectorOverrideForTesting;
        using var sessionManager = new SessionManager();
        const int processId = 12345;
        var swappedToReparsePoint = false;

        try
        {
            SessionManager.ScreenshotReparsePointChainDetectorOverrideForTesting = _ => swappedToReparsePoint;
            _ = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
            swappedToReparsePoint = true;

            var act = () => sessionManager.GetOrCreateScreenshotStorageRoot(processId);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*symbolic link*reparse point*");
        }
        finally
        {
            SessionManager.ScreenshotReparsePointChainDetectorOverrideForTesting = previousDetector;
        }
    }

    [Fact]
    public void RegisterScreenshotResource_WithExistingPreparedRoot_ShouldNotPrepareRootAgain()
    {
        var previousDetector = SessionManager.ScreenshotReparsePointChainDetectorOverrideForTesting;
        using var sessionManager = new SessionManager();
        const int processId = 12345;
        var checkedPaths = new List<string>();

        try
        {
            SessionManager.ScreenshotReparsePointChainDetectorOverrideForTesting = path =>
            {
                checkedPaths.Add(Path.GetFullPath(path));
                return false;
            };
            var storageRoot = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
            var screenshotId = "shot_0123456789abcdef0123456789abcdef";
            var filePath = Path.Combine(storageRoot, screenshotId + ".png");
            File.WriteAllBytes(filePath, new byte[] { 137, 80, 78, 71 });
            checkedPaths.Clear();

            sessionManager.RegisterScreenshotResource(processId, screenshotId, filePath, sha256: null);

            checkedPaths.Should().NotContain(Path.GetFullPath(storageRoot),
                "registering a file under an existing prepared root should not harden the root a second time");
            checkedPaths.Should().Contain(Path.GetFullPath(filePath),
                "the screenshot file path itself must still be validated before registration");
        }
        finally
        {
            SessionManager.ScreenshotReparsePointChainDetectorOverrideForTesting = previousDetector;
        }
    }

    [Theory]
    [InlineData("../shot_0123456789abcdef0123456789abcdef")]
    [InlineData("shot_0123456789abcdef0123456789abcdef/x")]
    [InlineData("shot_not_hex")]
    public void GetScreenshotPng_WithUnregisteredInvalidScreenshotId_ShouldNotReadResource(
        string screenshotId)
    {
        using var sessionManager = new SessionManager();

        var act = () => ScreenshotResources.GetScreenshotPng(sessionManager, screenshotId);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*is not retained in this MCP session*");
    }

    [Fact]
    public void GetScreenshotPng_AfterRemoveSession_ShouldRejectFormerResource()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();
        string? filePath = null;
        var screenshotId = RegisterValidScreenshotWithPath(
            sessionManager,
            tempDirectory.Path,
            processId: 12345,
            filePath: out filePath);

        sessionManager.RemoveSession(12345);
        var act = () => ScreenshotResources.GetScreenshotPng(sessionManager, screenshotId);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*is not retained in this MCP session*");
        File.Exists(filePath).Should().BeFalse(
            "disconnecting a target should purge retained screenshot pixels");
    }

    [Fact]
    public void RemoveSession_ShouldPruneRetainedScreenshotResourceOrderEntries()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();

        for (var index = 0; index < 10; index++)
        {
            var processId = 12345 + index;
            var screenshotId = "shot_" + index.ToString("x32");
            RegisterValidScreenshot(sessionManager, tempDirectory.Path, processId, screenshotId);
            sessionManager.RemoveSession(processId);
        }

        var counts = GetRetainedScreenshotResourceCounts(sessionManager);
        counts.Resources.Should().Be(0);
        counts.Order.Should().Be(0);
    }

    [Fact]
    public void GetScreenshotPng_AfterRetentionWindow_ShouldExpireHandleAndDeleteFile()
    {
        using var tempDirectory = new TemporaryDirectory();
        var now = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        using var sessionManager = new SessionManager(
            maxRequestsPerMinute: 60,
            authManager: null,
            certManager: null,
            utcNowProvider: () => now);
        string? filePath = null;
        var screenshotId = RegisterValidScreenshotWithPath(
            sessionManager,
            tempDirectory.Path,
            processId: 12345,
            filePath: out filePath);

        now = now.Add(SessionManager.ScreenshotResourceRetentionWindow).AddTicks(1);

        var act = () => ScreenshotResources.GetScreenshotPng(sessionManager, screenshotId);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*is not retained in this MCP session*");
        File.Exists(filePath).Should().BeFalse(
            "expired screenshot resources should remove their retained PNG file");
        var counts = GetRetainedScreenshotResourceCounts(sessionManager);
        counts.Resources.Should().Be(0);
        counts.Order.Should().Be(0);
    }

    [Fact]
    public void RegisterScreenshotResource_WhenRetentionLimitIsExceeded_ShouldDeleteEvictedFile()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();
        string? firstPath = null;

        for (var index = 0; index <= SessionManager.RetainedScreenshotResourceLimit; index++)
        {
            var screenshotId = "shot_" + index.ToString("x32");
            RegisterValidScreenshotWithPath(
                sessionManager,
                tempDirectory.Path,
                processId: 12345,
                out var filePath,
                screenshotId);
            firstPath ??= filePath;
        }

        File.Exists(firstPath).Should().BeFalse(
            "evicted screenshot resources should not leave pixel files behind");
        var counts = GetRetainedScreenshotResourceCounts(sessionManager);
        counts.Resources.Should().Be(SessionManager.RetainedScreenshotResourceLimit);
        counts.Order.Should().Be(SessionManager.RetainedScreenshotResourceLimit);
    }

    [Fact]
    public void RegisterScreenshotResource_WithSameIdAndPath_ShouldKeepRetainedFileReadable()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();
        var screenshotId = "shot_0123456789abcdef0123456789abcdef";
        RegisterValidScreenshotWithPath(
            sessionManager,
            tempDirectory.Path,
            processId: 12345,
            out var filePath,
            screenshotId);

        sessionManager.RegisterScreenshotResource(
            processId: 12345,
            screenshotId,
            filePath,
            sha256: null);

        File.Exists(filePath).Should().BeTrue(
            "refreshing a retained handle must not delete the same file path");
        var act = () => ScreenshotResources.GetScreenshotPng(sessionManager, screenshotId);
        act.Should().NotThrow();
        var counts = GetRetainedScreenshotResourceCounts(sessionManager);
        counts.Resources.Should().Be(1);
        counts.Order.Should().Be(1);
    }

    [Fact]
    public void Dispose_ShouldClearRetainedScreenshotResources()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var sessionManager = new SessionManager();
        string? filePath = null;
        var screenshotId = RegisterValidScreenshotWithPath(
            sessionManager,
            tempDirectory.Path,
            processId: 12345,
            filePath: out filePath);

        var beforeDispose = GetRetainedScreenshotResourceCounts(sessionManager);
        beforeDispose.Resources.Should().Be(1);
        beforeDispose.Order.Should().Be(1);

        sessionManager.Dispose();
        var afterDispose = GetRetainedScreenshotResourceCounts(sessionManager);
        afterDispose.Resources.Should().Be(0);
        afterDispose.Order.Should().Be(0);
        File.Exists(filePath).Should().BeFalse(
            "disposing the server session manager should purge retained screenshot pixels");

        var act = () => ScreenshotResources.GetScreenshotPng(sessionManager, screenshotId);
        act.Should().Throw<ObjectDisposedException>();
    }

    private static string RegisterValidScreenshot(
        SessionManager sessionManager,
        string directory,
        int processId,
        string screenshotId = "shot_0123456789abcdef0123456789abcdef")
    {
        return RegisterValidScreenshotWithPath(
            sessionManager,
            directory,
            processId,
            out _,
            screenshotId);
    }

    private static string RegisterValidScreenshotWithPath(
        SessionManager sessionManager,
        string directory,
        int processId,
        out string filePath,
        string screenshotId = "shot_0123456789abcdef0123456789abcdef")
    {
        var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var storageRoot = sessionManager.GetOrCreateScreenshotStorageRoot(processId);
        filePath = Path.Combine(storageRoot, screenshotId + ".png");
        File.WriteAllBytes(filePath, imageBytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
        sessionManager.RegisterScreenshotResource(processId, screenshotId, filePath, sha256);
        return screenshotId;
    }

    private static (int Resources, int Order) GetRetainedScreenshotResourceCounts(
        SessionManager sessionManager)
    {
        return (
            GetPrivateCollectionCount(sessionManager, "_screenshotResources"),
            GetPrivateCollectionCount(sessionManager, "_screenshotResourceOrder"));
    }

    private static int GetPrivateCollectionCount(
        SessionManager sessionManager,
        string fieldName)
    {
        var field = typeof(SessionManager).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();

        var value = field!.GetValue(sessionManager);
        value.Should().NotBeNull();

        var countProperty = value!.GetType().GetProperty(
            "Count",
            BindingFlags.Instance | BindingFlags.Public);
        countProperty.Should().NotBeNull();
        return (int)countProperty!.GetValue(value)!;
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
