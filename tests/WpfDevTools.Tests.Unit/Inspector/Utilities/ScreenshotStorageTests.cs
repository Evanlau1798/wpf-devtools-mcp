using System.IO;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Utilities;

public sealed class ScreenshotStorageTests
{
    [Fact]
    public void WritePng_ShouldStoreScreenshotUnderLocalApplicationData()
    {
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var expectedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WpfDevTools",
            "tmp",
            "screenshots");

        var screenshot = ScreenshotStorage.WritePng(imageBytes);

        try
        {
            screenshot.Path.StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            File.Exists(screenshot.Path).Should().BeTrue();
            Directory.Exists(expectedDirectory).Should().BeTrue();
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
