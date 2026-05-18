using System.IO;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;

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
        using var tempDirectory = new TemporaryDirectory();
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
        var previousValue = Environment.GetEnvironmentVariable(ScreenshotDirectoryEnvironmentVariable);
        ScreenshotStorage.ScreenshotFile? screenshot = null;

        try
        {
            Environment.SetEnvironmentVariable(ScreenshotDirectoryEnvironmentVariable, tempDirectory.Path);

            screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 });

            screenshot.Path.Should().StartWith(
                Path.GetFullPath(tempDirectory.Path) + Path.DirectorySeparatorChar,
                "test-configured screenshot output should not write under the real LocalApplicationData directory");
            File.Exists(screenshot.Path).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScreenshotDirectoryEnvironmentVariable, previousValue);
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
        var expiredPath = Path.Combine(tempDirectory.Path, "shot_expired.png");
        File.WriteAllBytes(expiredPath, new byte[] { 9, 9, 9 });
        File.SetLastWriteTimeUtc(
            expiredPath,
            DateTimeOffset.UtcNow.Subtract(ScreenshotStorage.RetentionMaxAge).AddMinutes(-1).UtcDateTime);

        var screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 }, tempDirectory.Path);

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
    public void WritePng_WhenRetentionCountIsFull_ShouldStayWithinRetentionCapAfterWrite()
    {
        using var tempDirectory = new TemporaryDirectory();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < ScreenshotStorage.MaxStoredScreenshots; index++)
        {
            var path = Path.Combine(tempDirectory.Path, $"shot_{index:x32}.png");
            File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
            File.SetLastWriteTimeUtc(path, now.AddMinutes(-index - 1).UtcDateTime);
        }

        var screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 }, tempDirectory.Path);

        File.Exists(screenshot.Path).Should().BeTrue("the newly written screenshot must not be evicted immediately");
        Directory.EnumerateFiles(tempDirectory.Path, "shot_*.png")
            .Should().HaveCount(ScreenshotStorage.MaxStoredScreenshots,
                "on-disk screenshot retention should enforce the cap after every write");
    }

    [Fact]
    public void WritePng_WhenExistingScreenshotsHaveFutureTimestamps_ShouldRetainNewScreenshot()
    {
        using var tempDirectory = new TemporaryDirectory();
        var future = DateTimeOffset.UtcNow.AddDays(1);
        for (var index = 0; index < ScreenshotStorage.MaxStoredScreenshots; index++)
        {
            var path = Path.Combine(tempDirectory.Path, $"shot_{index:x32}.png");
            File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
            File.SetLastWriteTimeUtc(path, future.AddMinutes(index).UtcDateTime);
        }

        var screenshot = ScreenshotStorage.WritePng(new byte[] { 1, 2, 3 }, tempDirectory.Path);

        File.Exists(screenshot.Path).Should().BeTrue("the retention pass must explicitly protect the file it just wrote");
        Directory.EnumerateFiles(tempDirectory.Path, "shot_*.png")
            .Should().HaveCount(ScreenshotStorage.MaxStoredScreenshots);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wpf-devtools-screenshot-storage-" + Guid.NewGuid().ToString("N"));
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
