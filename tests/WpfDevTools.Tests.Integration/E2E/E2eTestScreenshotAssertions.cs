using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public static partial class E2eTestHelpers
{
    public readonly record struct ImageDimensions(int Width, int Height);

    private static readonly byte[] PngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static readonly byte[] PngIhdrChunkType = new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' };

    public static ImageDimensions AssertBase64ScreenshotMatchesReportedMetadata(JsonElement result)
    {
        result.TryGetProperty("base64Image", out var image).Should().BeTrue(
            "screenshot should include base64-encoded PNG data");
        image.ValueKind.Should().Be(JsonValueKind.String, "base64Image should be a string payload");
        var encodedImage = image.GetString();
        encodedImage.Should().NotBeNullOrWhiteSpace("base64Image should contain PNG data");

        var bytes = Array.Empty<byte>();
        var decode = () => bytes = Convert.FromBase64String(encodedImage!);
        decode.Should().NotThrow("base64Image should be valid base64");

        return AssertPngMatchesReportedMetadata(bytes, result);
    }

    public static string AssertFileScreenshotMatchesReportedMetadata(JsonElement result, string expectedDirectory)
    {
        AssertFileScreenshotMatchesReportedMetadata(result, expectedDirectory, out var fullPath);
        return fullPath;
    }

    public static void AssertFileScreenshotMatchesReportedMetadata(
        JsonElement result,
        string expectedDirectory,
        out string fullPath)
    {
        result.TryGetProperty("path", out _).Should().BeFalse(
            "file screenshot output should redact absolute local paths from MCP responses");
        result.GetProperty("localPathRedacted").GetBoolean().Should().BeTrue(
            "file screenshot output should advertise path redaction");
        result.TryGetProperty("fileName", out var fileNameProperty).Should().BeTrue(
            "file screenshot output should include the safe file name");
        fileNameProperty.ValueKind.Should().Be(JsonValueKind.String, "fileName should be a string");
        var fileName = fileNameProperty.GetString();
        fileName.Should().NotBeNullOrWhiteSpace("fileName should identify the written PNG without exposing an absolute path");
        Path.GetFileName(fileName!).Should().Be(fileName, "fileName should not include directory components");

        fullPath = Path.GetFullPath(Path.Combine(expectedDirectory, fileName!));
        IsPathUnderDirectory(fullPath, expectedDirectory).Should().BeTrue(
            $"file screenshot should be written under {expectedDirectory}");
        File.Exists(fullPath).Should().BeTrue("file screenshot path should exist before cleanup");

        AssertPngMatchesReportedMetadata(File.ReadAllBytes(fullPath), result);
    }

    private static ImageDimensions AssertPngMatchesReportedMetadata(byte[] bytes, JsonElement result)
    {
        bytes.Length.Should().BeGreaterThanOrEqualTo(24, "PNG signature and IHDR fields should be present");
        bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature).Should().BeTrue(
            "decoded screenshot bytes should start with the PNG signature");
        BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8, 4)).Should().Be(13,
            "the first PNG chunk should be a 13-byte IHDR chunk");
        bytes.AsSpan(12, PngIhdrChunkType.Length).SequenceEqual(PngIhdrChunkType).Should().BeTrue(
            "the first PNG chunk should be IHDR");

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        width.Should().BeGreaterThan(0, "PNG IHDR width should be positive");
        height.Should().BeGreaterThan(0, "PNG IHDR height should be positive");
        AssertPngDecodes(bytes).Should().Be(new ImageDimensions(width, height),
            "decoded PNG dimensions should match IHDR dimensions");

        if (result.TryGetProperty("width", out var reportedWidth))
        {
            reportedWidth.GetInt32().Should().Be(width, "reported width should match PNG IHDR width");
        }

        if (result.TryGetProperty("height", out var reportedHeight))
        {
            reportedHeight.GetInt32().Should().Be(height, "reported height should match PNG IHDR height");
        }

        return new ImageDimensions(width, height);
    }

    private static ImageDimensions AssertPngDecodes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        decoder.Frames.Should().NotBeEmpty("valid PNG data should decode into at least one frame");
        var frame = decoder.Frames[0];
        var bitsPerPixel = Math.Max(1, frame.Format.BitsPerPixel);
        var stride = ((frame.PixelWidth * bitsPerPixel + 31) / 32) * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);
        return new ImageDimensions(frame.PixelWidth, frame.PixelHeight);
    }

    private static bool IsPathUnderDirectory(string path, string expectedDirectory)
    {
        var fullExpectedDirectory = Path.GetFullPath(expectedDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.StartsWith(
            fullExpectedDirectory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

}
