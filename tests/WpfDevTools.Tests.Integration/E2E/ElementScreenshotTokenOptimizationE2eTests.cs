using System.IO;
using FluentAssertions;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class ElementScreenshotTokenOptimizationE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ElementScreenshotTokenOptimizationE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ElementScreenshot_WithMetadataOutputMode_ShouldOmitBase64Payload()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "element_screenshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                outputMode = "metadata"
            });

        _output.WriteLine($"Screenshot metadata result: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("base64Image", out _).Should().BeFalse();
        result.GetProperty("rendered").GetBoolean().Should().BeFalse();
        result.GetProperty("byteLength").GetInt32().Should().Be(0);
        result.GetProperty("width").GetInt32().Should().BeGreaterThan(0);
        result.GetProperty("height").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ElementScreenshot_WithMaxWidth_ShouldReturnDownscaledImage()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "element_screenshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                outputMode = "base64",
                maxWidth = 200
            });

        _output.WriteLine($"Downscaled screenshot keys: {string.Join(", ", E2eTestHelpers.EnumeratePropertyNames(result))}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("rendered").GetBoolean().Should().BeTrue();
        result.GetProperty("width").GetInt32().Should().BeLessOrEqualTo(200);
        E2eTestHelpers.AssertBase64ScreenshotMatchesReportedMetadata(result);
    }

    [Fact]
    public async Task ElementScreenshot_WithFileOutputMode_ShouldWriteScreenshotToDisk()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "element_screenshot",
            new
            {
                processId = _fixture.TestAppProcessId,
                outputMode = "file",
                maxWidth = 256
            });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.TryGetProperty("base64Image", out _).Should().BeFalse();
        var expectedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WpfDevTools",
            "tmp",
            "screenshots");
        var path = E2eTestHelpers.AssertFileScreenshotMatchesReportedMetadata(result, expectedDirectory);
        _output.WriteLine($"File screenshot metadata: rendered={result.GetProperty("rendered").GetBoolean()}, width={result.GetProperty("width").GetInt32()}, height={result.GetProperty("height").GetInt32()}, underExpectedDirectory=True");
        result.GetProperty("width").GetInt32().Should().BeLessOrEqualTo(256);

        File.Delete(path);
    }
}
