using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class InteractionAnalyzerScreenshotOptionsTests
{
    [StaFact]
    public void TakeScreenshot_WithMetadataOutputMode_ShouldOmitBase64AndIncludeByteLength()
    {
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 160, Height = 80 };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(elementId, "metadata");
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.TryGetProperty("base64Image", out _).Should().BeFalse();
        json.GetProperty("byteLength").GetInt32().Should().BeGreaterThan(0);
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
        var finder = new ElementFinder();
        var analyzer = new InteractionAnalyzer(finder);
        var button = new Button { Width = 140, Height = 70 };
        var elementId = finder.GenerateElementId(button);

        var result = analyzer.TakeScreenshot(elementId, "file");
        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.TryGetProperty("base64Image", out _).Should().BeFalse();
        var path = json.GetProperty("path").GetString();
        path.Should().NotBeNullOrEmpty();
        File.Exists(path).Should().BeTrue();
        json.GetProperty("byteLength").GetInt32().Should().BeGreaterThan(0);

        File.Delete(path!);
    }
}
