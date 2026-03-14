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
}
