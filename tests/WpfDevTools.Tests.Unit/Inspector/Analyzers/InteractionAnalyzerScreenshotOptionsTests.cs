using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class InteractionAnalyzerScreenshotOptionsTests
{
    [StaFact]
    public void TakeScreenshot_WithMetadataOutputMode_ShouldOmitBase64AndSkipRendering()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 160, Height = 80 };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(elementId, "metadata");
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.TryGetProperty("base64Image", out _).Should().BeFalse();
        json.GetProperty("rendered").GetBoolean().Should().BeFalse();
        json.GetProperty("byteLength").GetInt32().Should().Be(0);
        json.GetProperty("width").GetInt32().Should().Be(160);
        json.GetProperty("height").GetInt32().Should().Be(80);
    }

    [StaFact]
    public void TakeScreenshot_WithMaxWidth_ShouldDownscaleProportionally()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 200, Height = 100 };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(elementId, "metadata", maxWidth: 80);
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("width").GetInt32().Should().Be(80);
        json.GetProperty("height").GetInt32().Should().Be(40);
    }

    [StaFact]
    public void TakeScreenshot_WithMetadataOutputMode_ShouldAllowOversizedElementWithoutRendering()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 5000, Height = 5000 };
        button.Measure(new System.Windows.Size(5000, 5000));
        button.Arrange(new System.Windows.Rect(0, 0, 5000, 5000));
        button.UpdateLayout();
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(elementId, "metadata");
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("rendered").GetBoolean().Should().BeFalse();
        json.GetProperty("byteLength").GetInt32().Should().Be(0);
        json.GetProperty("width").GetInt32().Should().Be(5000);
        json.GetProperty("height").GetInt32().Should().Be(5000);
    }

    [StaFact]
    public void TakeScreenshot_WithRenderedOutputAboveBudget_ShouldReturnPayloadTooLarge()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 5000, Height = 5000 };
        button.Measure(new System.Windows.Size(5000, 5000));
        button.Arrange(new System.Windows.Rect(0, 0, 5000, 5000));
        button.UpdateLayout();
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(elementId, "base64");
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be(
            "PayloadTooLarge",
            json.GetProperty("error").GetString());
        json.GetProperty("hint").GetString().Should().Contain("maxWidth");
    }

    [StaFact]
    public void TakeScreenshot_WithLargeBase64Output_ShouldRequireFileResourceMode()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var image = new Image
        {
            Width = 384,
            Height = 384,
            Source = CreateNoisyBitmap(width: 384, height: 384)
        };
        image.Measure(new Size(384, 384));
        image.Arrange(new Rect(0, 0, 384, 384));
        image.UpdateLayout();
        var elementId = finder.GenerateElementId(image);

        var result = analyzer.TakeScreenshot(elementId, "base64");
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("PayloadTooLarge");
        json.GetProperty("hint").GetString().Should().Contain("outputMode 'file'");
        json.GetProperty("errorData").GetProperty("maxInlineByteLength")
            .GetInt32().Should().Be(ScreenshotStorage.MaxInlineEncodedPngBytes);
        json.TryGetProperty("base64Image", out _).Should().BeFalse();
    }

    [StaFact]
    public void TakeScreenshot_WithNonPositiveMaxDimension_ShouldReturnStructuredError()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 120, Height = 60 };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(elementId, "metadata", maxWidth: 0);
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("hint").GetString().Should().Contain("positive");
    }

    [StaFact]
    public void TakeScreenshot_WithFileOutputMode_ShouldWritePngAndOmitBase64()
    {
        using var tempDirectory = TemporaryDirectory.CreateScreenshotLeaseDirectory();
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(
            finder,
            watchEventBuffer: null,
            screenshotDirectoryOverride: tempDirectory.Path);
        var button = new Button { Width = 140, Height = 70 };
        var elementId = finder.GenerateElementId(button);
        string? path = null;

        try
        {
            var result = analyzer.TakeScreenshot(elementId, "file");
            var json = JsonSerializer.SerializeToElement(result);

            json.GetProperty("success").GetBoolean().Should().BeTrue();
            json.TryGetProperty("base64Image", out _).Should().BeFalse();
            path = json.GetProperty("path").GetString();
            path.Should().NotBeNullOrEmpty();
            path.Should().StartWith(
                Path.GetFullPath(tempDirectory.Path) + Path.DirectorySeparatorChar,
                "unit screenshot tests should not write under real LocalApplicationData");
            File.Exists(path).Should().BeTrue();
            json.GetProperty("rendered").GetBoolean().Should().BeTrue();
            json.GetProperty("byteLength").GetInt32().Should().BeGreaterThan(0);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [StaFact]
    public void TakeScreenshot_WithRequestDirectoryOverrideOutsideLeaseRoot_ShouldRejectWithoutWriting()
    {
        using var attackerDirectory = new TemporaryDirectory();
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 140, Height = 70 };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(
            elementId,
            "file",
            screenshotDirectoryOverride: attackerDirectory.Path);
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        Directory.EnumerateFiles(attackerDirectory.Path).Should().BeEmpty();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
            : this(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wpf-devtools-interaction-screenshot-" + Guid.NewGuid().ToString("N")))
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

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private static BitmapSource CreateNoisyBitmap(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var seed = 17u;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            seed = (seed * 1664525u) + 1013904223u;
            pixels[index] = (byte)seed;
            pixels[index + 1] = (byte)(seed >> 8);
            pixels[index + 2] = (byte)(seed >> 16);
            pixels[index + 3] = 255;
        }

        return BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
    }
}
