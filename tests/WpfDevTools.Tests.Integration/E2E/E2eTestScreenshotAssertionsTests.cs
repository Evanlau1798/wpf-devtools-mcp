using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class E2eTestScreenshotAssertionsTests
{
    [Fact]
    public void AssertFileScreenshotMetadata_ShouldReturnResourceUriWithoutLocalPath()
    {
        var screenshotId = "shot_00000000000000000000000000000000";
        var fileName = screenshotId + ".png";
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

        var resourceUri = E2eTestHelpers.AssertFileScreenshotMetadata(document.RootElement);

        resourceUri.Should().Be("wpf://screenshots/shot_00000000000000000000000000000000");
    }

    [Fact]
    public void AssertScreenshotResourceMatchesReportedMetadata_ShouldValidateMcpBlob()
    {
        var imageBytes = CreateOnePixelPng();
        var blob = Convert.ToBase64String(imageBytes);
        using var resultDocument = JsonDocument.Parse("""
            {
              "success": true,
              "localPathRedacted": true,
              "screenshotId": "shot_00000000000000000000000000000000",
              "outputMode": "file",
              "resourceUri": "wpf://screenshots/shot_00000000000000000000000000000000",
              "fileName": "shot_00000000000000000000000000000000.png",
              "width": 1,
              "height": 1,
              "format": "png",
              "rendered": true
            }
            """);
        using var resourceDocument = JsonDocument.Parse($$"""
            {
              "result": {
                "contents": [
                  {
                    "uri": "wpf://screenshots/shot_00000000000000000000000000000000",
                    "mimeType": "image/png",
                    "blob": "{{blob}}"
                  }
                ]
              }
            }
            """);

        var dimensions = E2eTestHelpers.AssertScreenshotResourceMatchesReportedMetadata(
            resourceDocument.RootElement,
            resultDocument.RootElement);

        dimensions.Should().Be(new E2eTestHelpers.ImageDimensions(1, 1));
    }

    private static byte[] CreateOnePixelPng()
    {
        var pixels = new byte[] { 0, 0, 0, 255 };
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
