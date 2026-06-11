using System.IO;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

[Collection("ProcessEnvironment")]
public sealed class ScreenshotStorageTests
{
    private const string ScreenshotDirectoryEnvironmentVariable = ScreenshotStorage.DirectoryEnvironmentVariable;

    [Fact]
    public void GetDefaultScreenshotDirectory_ShouldResolveUnderLocalApplicationData()
    {
        var expectedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WpfDevTools",
            "tmp",
            "screenshots");
        var directoryExistedBefore = Directory.Exists(expectedDirectory);

        var directory = ScreenshotStorage.GetDefaultScreenshotDirectory();

        directory.Should().Be(expectedDirectory);
        Directory.Exists(directory).Should().Be(directoryExistedBefore,
            "default directory resolution should not write to the real LocalApplicationData profile during unit tests");
    }

    [Fact]
    public void WritePng_WithOversizedPayload_ShouldRejectBeforeWritingFile()
    {
        using var tempDirectory = TemporaryDirectory.CreateScreenshotLeaseDirectory();
        var imageBytes = new byte[ScreenshotStorage.MaxEncodedPngBytes + 1];

        Action act = () => ScreenshotStorage.WritePng(imageBytes, tempDirectory.Path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*exceeds*");
        Directory.EnumerateFiles(tempDirectory.Path).Should().BeEmpty();
    }

    [Fact]
    public void WritePng_ShouldUseConfiguredScreenshotDirectory()
    {
        using var tempDirectory = new TemporaryDirectory();
        ScreenshotStorage.ScreenshotFile? screenshot = null;

        try
        {
            using var _ = new ScreenshotDirectoryEnvironmentScope(tempDirectory.Path);

            screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 });

            screenshot.Path.Should().StartWith(
                Path.GetFullPath(tempDirectory.Path) + Path.DirectorySeparatorChar,
                "test-configured screenshot output should not write under the real LocalApplicationData directory");
            File.Exists(screenshot.Path).Should().BeTrue();
        }
        finally
        {
            if (screenshot != null && File.Exists(screenshot.Path))
            {
                File.Delete(screenshot.Path);
            }
        }
    }

    [Theory]
    [InlineData(@"relative\screenshots")]
    [InlineData(@"C:relative\screenshots")]
    [InlineData(@"\rooted\screenshots")]
    [InlineData(@"\\server\share\screenshots")]
    public void WritePng_WithInvalidConfiguredScreenshotDirectory_ShouldRejectBeforeWriting(string configuredDirectory)
    {
        var previousValue = Environment.GetEnvironmentVariable(ScreenshotDirectoryEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(ScreenshotDirectoryEnvironmentVariable, configuredDirectory);

            var act = () => ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 });

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*WPFDEVTOOLS_SCREENSHOT_DIR*absolute local directory path*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScreenshotDirectoryEnvironmentVariable, previousValue);
        }
    }

    [Fact]
    public void WritePng_ShouldRemoveExpiredScreenshotsInTargetDirectory()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var _ = new ScreenshotDirectoryEnvironmentScope(tempDirectory.Path);
        var expiredPath = Path.Combine(tempDirectory.Path, "shot_expired.png");
        File.WriteAllBytes(expiredPath, new byte[] { 9, 9, 9 });
        File.SetLastWriteTimeUtc(
            expiredPath,
            DateTimeOffset.UtcNow.Subtract(ScreenshotStorage.RetentionMaxAge).AddMinutes(-1).UtcDateTime);

        var screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 });

        try
        {
            File.Exists(screenshot.Path).Should().BeTrue();
            File.Exists(expiredPath).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(screenshot.Path))
            {
                File.Delete(screenshot.Path);
            }
        }
    }

    [Fact]
    public void WritePng_WithServerLeaseDirectoryOverride_ShouldNotDeleteExistingScreenshots()
    {
        using var tempDirectory = TemporaryDirectory.CreateScreenshotLeaseDirectory();
        var retainedPath = Path.Combine(tempDirectory.Path, "shot_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png");
        File.WriteAllBytes(retainedPath, new byte[] { 9, 9, 9 });
        File.SetLastWriteTimeUtc(
            retainedPath,
            DateTimeOffset.UtcNow.Subtract(ScreenshotStorage.RetentionMaxAge).AddMinutes(-1).UtcDateTime);

        var screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 }, tempDirectory.Path);

        File.Exists(screenshot.Path).Should().BeTrue();
        File.Exists(retainedPath).Should().BeTrue(
            "server-owned retained screenshot resources must be deleted only by the MCP server resource registry");
    }

    [Fact]
    public void WritePng_WithServerLeaseDirectoryOutsideTargetTemp_ShouldAllowValidLocalLeaseShape()
    {
        using var serverTempRoot = new TemporaryDirectory();
        using var leaseDirectory = TemporaryDirectory.CreateScreenshotLeaseDirectoryUnder(serverTempRoot.Path);

        var screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 }, leaseDirectory.Path);

        File.Exists(screenshot.Path).Should().BeTrue();
        screenshot.Path.Should().StartWith(
            Path.GetFullPath(leaseDirectory.Path) + Path.DirectorySeparatorChar,
            "server-issued lease roots can come from the server profile, not the target process temp root");
    }

    [Fact]
    public void WritePng_WhenRetentionCountIsFull_ShouldStayWithinRetentionCapAfterWrite()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var _ = new ScreenshotDirectoryEnvironmentScope(tempDirectory.Path);
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < ScreenshotStorage.MaxStoredScreenshots; index++)
        {
            var path = Path.Combine(tempDirectory.Path, $"shot_{index:x32}.png");
            File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
            File.SetLastWriteTimeUtc(path, now.AddMinutes(-index - 1).UtcDateTime);
        }

        var screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 });

        File.Exists(screenshot.Path).Should().BeTrue("the newly written screenshot must not be evicted immediately");
        Directory.EnumerateFiles(tempDirectory.Path, "shot_*.png")
            .Should().HaveCount(ScreenshotStorage.MaxStoredScreenshots,
                "on-disk screenshot retention should enforce the cap after every write");
    }

    [Fact]
    public void WritePng_WhenExistingScreenshotsHaveFutureTimestamps_ShouldRetainNewScreenshot()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var _ = new ScreenshotDirectoryEnvironmentScope(tempDirectory.Path);
        var future = DateTimeOffset.UtcNow.AddDays(1);
        for (var index = 0; index < ScreenshotStorage.MaxStoredScreenshots; index++)
        {
            var path = Path.Combine(tempDirectory.Path, $"shot_{index:x32}.png");
            File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
            File.SetLastWriteTimeUtc(path, future.AddMinutes(index).UtcDateTime);
        }

        var screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 });

        File.Exists(screenshot.Path).Should().BeTrue("the retention pass must explicitly protect the file it just wrote");
        Directory.EnumerateFiles(tempDirectory.Path, "shot_*.png")
            .Should().HaveCount(ScreenshotStorage.MaxStoredScreenshots);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
            : this(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wpf-devtools-screenshot-storage-" + Guid.NewGuid().ToString("N")))
        {
        }

        private TemporaryDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public static TemporaryDirectory CreateScreenshotLeaseDirectory()
            => new(ScreenshotLeasePaths.CreateStorageRootPath(
                System.IO.Path.GetTempPath(),
                Environment.ProcessId,
                Guid.NewGuid().ToString("N")));

        public static TemporaryDirectory CreateScreenshotLeaseDirectoryUnder(string tempRoot)
            => new(ScreenshotLeasePaths.CreateStorageRootPath(
                tempRoot,
                Environment.ProcessId,
                Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class ScreenshotDirectoryEnvironmentScope : IDisposable
    {
        private readonly string? _previousValue;

        public ScreenshotDirectoryEnvironmentScope(string directoryPath)
        {
            _previousValue = Environment.GetEnvironmentVariable(ScreenshotDirectoryEnvironmentVariable);
            Environment.SetEnvironmentVariable(ScreenshotDirectoryEnvironmentVariable, directoryPath);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(ScreenshotDirectoryEnvironmentVariable, _previousValue);
        }
    }
}
