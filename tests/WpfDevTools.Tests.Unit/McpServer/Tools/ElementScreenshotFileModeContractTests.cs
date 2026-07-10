using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed class ElementScreenshotFileModeContractTests
{
    private const string ScreenshotId = "shot_0123456789abcdef0123456789abcdef";
    private static readonly byte[] ScreenshotBytes = [137, 80, 78, 71];

    [Fact]
    public async Task Execute_FileModeSuccessWithoutPath_ShouldFailClosed()
    {
        var result = await ExecuteFileModeAsync(request => JsonSerializer.SerializeToElement(new
        {
            success = true,
            screenshotId = ScreenshotId,
            width = 160,
            height = 80,
            format = "png",
            byteLength = ScreenshotBytes.Length,
            sha256 = Sha256For(ScreenshotBytes)
        }));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        result.ToString().Should().NotContain("resourceUri");
    }

    [Fact]
    public async Task Execute_FileModeSuccessWithoutScreenshotId_ShouldFailClosed()
    {
        var result = await ExecuteFileModeAsync(request =>
        {
            var path = WriteOwnedScreenshot(request);
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                width = 160,
                height = 80,
                format = "png",
                byteLength = ScreenshotBytes.Length,
                sha256 = Sha256For(ScreenshotBytes),
                path
            });
        });

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("SecurityError");
    }

    [Fact]
    public async Task Execute_FileModeSuccessWithoutSha256_ShouldFailClosed()
    {
        var result = await ExecuteFileModeAsync(request =>
        {
            var path = WriteOwnedScreenshot(request);
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                screenshotId = ScreenshotId,
                width = 160,
                height = 80,
                format = "png",
                byteLength = ScreenshotBytes.Length,
                path
            });
        });

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("SecurityError");
    }

    [Fact]
    public async Task Execute_FileModeSuccessWithWrongSha256_ShouldFailClosed()
    {
        var result = await ExecuteFileModeAsync(request =>
        {
            var path = WriteOwnedScreenshot(request);
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                screenshotId = ScreenshotId,
                width = 160,
                height = 80,
                format = "png",
                byteLength = ScreenshotBytes.Length,
                sha256 = new string('0', 64),
                path
            });
        });

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("SecurityError");
    }

    [Fact]
    public async Task Execute_FileModeSuccessWithPathOutsideScreenshotRoot_ShouldFailClosed()
    {
        var outsideDirectory = Directory.CreateTempSubdirectory("wpf-devtools-outside-shot-");
        try
        {
            var outsidePath = Path.Combine(outsideDirectory.FullName, ScreenshotId + ".png");
            await File.WriteAllBytesAsync(outsidePath, ScreenshotBytes);

            var result = await ExecuteFileModeAsync(_ => JsonSerializer.SerializeToElement(new
            {
                success = true,
                screenshotId = ScreenshotId,
                width = 160,
                height = 80,
                format = "png",
                byteLength = ScreenshotBytes.Length,
                sha256 = Sha256For(ScreenshotBytes),
                path = outsidePath
            }));

            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("errorCode").GetString().Should().Be("SecurityError");
            File.Exists(outsidePath).Should().BeTrue();
        }
        finally
        {
            outsideDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_FileModeSuccessWithValidResource_ShouldReturnOnlyResourceMetadata()
    {
        string? localPath = null;
        var result = await ExecuteFileModeAsync(request =>
        {
            localPath = WriteOwnedScreenshot(request);
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                screenshotId = ScreenshotId,
                width = 160,
                height = 80,
                format = "png",
                byteLength = ScreenshotBytes.Length,
                sha256 = Sha256For(ScreenshotBytes),
                path = localPath
            });
        });

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("outputMode").GetString().Should().Be("file");
        result.GetProperty("resourceUri").GetString().Should().Be($"wpf://screenshots/{ScreenshotId}");
        var resourceRead = result.GetProperty("resourceRead");
        resourceRead.GetProperty("method").GetString().Should().Be("resources/read");
        resourceRead.GetProperty("uri").GetString().Should().Be($"wpf://screenshots/{ScreenshotId}");
        resourceRead.GetProperty("sameSessionRequired").GetBoolean().Should().BeTrue();
        result.GetProperty("screenshotId").GetString().Should().Be(ScreenshotId);
        result.GetProperty("sha256").GetString().Should().MatchRegex("^[0-9a-fA-F]{64}$");
        result.GetProperty("localPathRedacted").GetBoolean().Should().BeTrue();
        result.TryGetProperty("path", out _).Should().BeFalse();
        result.TryGetProperty("filePath", out _).Should().BeFalse();
        result.TryGetProperty("absolutePath", out _).Should().BeFalse();
        result.TryGetProperty("screenshotPath", out _).Should().BeFalse();
        JsonSerializer.Serialize(result).Should().NotContain(localPath);
    }

    [Fact]
    public async Task Execute_MetadataModeSuccess_ShouldSuggestFileModeForPixelEvidence()
    {
        var result = await ExecuteScreenshotAsync(
            _ => JsonSerializer.SerializeToElement(new
            {
                success = true,
                outputMode = "metadata",
                rendered = false,
                width = 160,
                height = 80,
                byteLength = 0
            }),
            "metadata");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("outputMode").GetString().Should().Be("metadata");
        var nextStep = result.GetProperty("nextSteps").EnumerateArray().Should().ContainSingle().Subject;
        nextStep.GetProperty("tool").GetString().Should().Be("element_screenshot");
        nextStep.GetProperty("params").GetProperty("outputMode").GetString().Should().Be("file");
        nextStep.GetProperty("reason").GetString().Should().Contain("pixel evidence");
    }

    private static Task<JsonElement> ExecuteFileModeAsync(Func<InspectorRequest, JsonElement> responseFactory) =>
        ExecuteScreenshotAsync(responseFactory, "file");

    private static async Task<JsonElement> ExecuteScreenshotAsync(
        Func<InspectorRequest, JsonElement> responseFactory,
        string outputMode)
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_ElementScreenshotContract_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
                var response = new InspectorResponse
                {
                    Id = request.Id,
                    CorrelationId = request.CorrelationId,
                    Result = responseFactory(request)
                };

                await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
            }
            catch (EndOfStreamException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);
        var client = new NamedPipeClient(
            processId,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: false,
            requestTimeout: TimeSpan.FromSeconds(5));
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplaceSessionManagerPipeClient(sessionManager, processId, client);
        var tool = new ElementScreenshotTool(sessionManager);

        try
        {
            return JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { processId, outputMode }),
                CancellationToken.None));
        }
        finally
        {
            server.Dispose();
            await serverTask;
        }
    }

    private static string WriteOwnedScreenshot(InspectorRequest request)
    {
        var screenshotDirectory = request.Params!.Value.GetProperty("screenshotDirectory").GetString()!;
        var path = Path.Combine(screenshotDirectory, ScreenshotId + ".png");
        File.WriteAllBytes(path, ScreenshotBytes);
        return path;
    }

    private static string Sha256For(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
