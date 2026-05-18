using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class E2eTestScreenshotAssertionsTests
{
    [Fact]
    public void AssertFileScreenshotMatchesReportedMetadata_ShouldExposeCleanupPathBeforePngAssertions()
    {
        using var tempDirectory = new TemporaryDirectory();
        var screenshotId = "shot_00000000000000000000000000000000";
        var fileName = screenshotId + ".png";
        var filePath = Path.Combine(tempDirectory.Path, fileName);
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });
        using var document = JsonDocument.Parse($$"""
            {
              "success": true,
              "localPathRedacted": true,
              "screenshotId": "{{screenshotId}}",
              "outputMode": "file",
              "resourceUri": "wpf://screenshots/{{screenshotId}}",
              "fileName": "{{fileName}}",
              "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "width": 1,
              "height": 1,
              "format": "png",
              "rendered": true,
              "byteLength": 3
            }
            """);
        string? cleanupPath = null;

        var act = () => E2eTestHelpers.AssertFileScreenshotMatchesReportedMetadata(
            document.RootElement,
            tempDirectory.Path,
            out cleanupPath);

        act.Should().Throw<Exception>("invalid PNG metadata should still fail validation");
        cleanupPath.Should().Be(filePath,
            "caller finally blocks must know which screenshot to delete even when deeper assertions fail");
        File.Exists(cleanupPath).Should().BeTrue();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wpf-devtools-e2e-screenshot-assertions-" + Guid.NewGuid().ToString("N"));
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
