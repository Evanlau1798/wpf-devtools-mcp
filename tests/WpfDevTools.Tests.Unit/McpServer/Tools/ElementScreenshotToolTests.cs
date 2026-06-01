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
public class ElementScreenshotToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        // Arrange
        var tool = new ElementScreenshotTool(new SessionManager());
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new ElementScreenshotTool(new SessionManager());
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task Execute_WithInvalidOutputMode_ShouldReturnInvalidArgumentBeforePipeRequest()
    {
        var tool = new ElementScreenshotTool(new SessionManager());

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 12345,
            outputMode = "inline"
        }), CancellationToken.None));

        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("outputMode");
    }

    [Fact]
    public async Task Execute_WithNonStringOutputMode_ShouldReturnInvalidArgumentBeforePipeRequest()
    {
        var tool = new ElementScreenshotTool(new SessionManager());

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 12345,
            outputMode = 1
        }), CancellationToken.None));

        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("outputMode");
    }

    [Theory]
    [InlineData("maxWidth", 0)]
    [InlineData("maxHeight", -1)]
    public async Task Execute_WithInvalidScreenshotDimensionLimit_ShouldReturnInvalidArgumentBeforePipeRequest(
        string parameterName,
        int value)
    {
        var tool = new ElementScreenshotTool(new SessionManager());
        var arguments = parameterName == "maxWidth"
            ? ToJsonElement(new { processId = 12345, maxWidth = value })
            : ToJsonElement(new { processId = 12345, maxHeight = value });

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(arguments, CancellationToken.None));

        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain(parameterName);
    }

    [Fact]
    public async Task Execute_WithNonIntegerScreenshotDimensionLimit_ShouldReturnInvalidArgumentBeforePipeRequest()
    {
        var tool = new ElementScreenshotTool(new SessionManager());

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 12345,
            maxWidth = true
        }), CancellationToken.None));

        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("maxWidth");
    }

    [Fact]
    public async Task Execute_WithOutputModeDifferentCasing_ShouldNormalizeBeforeForwardingPipeRequest()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_ElementScreenshotMode_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var requestCompletion = new TaskCompletionSource<InspectorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string screenshotId = "shot_0123456789abcdef0123456789abcdef";

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                requestCompletion.TrySetResult(request!);

                var screenshotDirectory = request!.Params!.Value.TryGetProperty("screenshotDirectory", out var directoryProperty)
                    ? directoryProperty.GetString()
                    : null;
                var screenshotPath = Path.Combine(
                    screenshotDirectory ?? Path.GetTempPath(),
                    screenshotId + ".png");
                var screenshotBytes = new byte[] { 137, 80, 78, 71 };
                await File.WriteAllBytesAsync(screenshotPath, screenshotBytes);
                var response = new InspectorResponse
                {
                    Id = request.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.SerializeToElement(new
                    {
                        success = true,
                        screenshotId,
                        width = 160,
                        height = 80,
                        format = "png",
                        byteLength = screenshotBytes.Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(screenshotBytes)).ToLowerInvariant(),
                        path = screenshotPath
                    })
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

        var now = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        var sessionManager = new SessionManager(
            maxRequestsPerMinute: 60,
            authManager: null,
            certManager: null,
            utcNowProvider: () => now);
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
        ReplacePipeClient(sessionManager, processId, client);
        var tool = new ElementScreenshotTool(sessionManager);

        try
        {
            var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
            {
                processId,
                outputMode = " FiLe "
            }), CancellationToken.None));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            result.TryGetProperty("path", out _).Should().BeFalse();
            result.GetProperty("fileName").GetString().Should().Be("shot_0123456789abcdef0123456789abcdef.png");
            result.GetProperty("resourceUri").GetString().Should().Be("wpf://screenshots/shot_0123456789abcdef0123456789abcdef");
            result.GetProperty("expiresAtUtc").GetDateTimeOffset().Should().Be(
                now.Add(SessionManager.ScreenshotResourceRetentionWindow));
            result.GetProperty("localPathRedacted").GetBoolean().Should().BeTrue();
            var request = await requestCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            request.Params.Should().NotBeNull();
            request.Params!.Value.TryGetProperty("outputMode", out var outputMode).Should().BeTrue();
            outputMode.GetString().Should().Be("file");
            request.Params.Value.TryGetProperty("screenshotDirectory", out var screenshotDirectory).Should().BeTrue(
                "file-mode screenshots must write into a server-owned root before the server exposes a retained resource URI");
            screenshotDirectory.GetString().Should().NotBeNullOrWhiteSpace();
            Directory.Exists(screenshotDirectory.GetString()!).Should().BeTrue();
        }
        finally
        {
            sessionManager.Dispose();
            server.Dispose();
            await serverTask;
        }
    }

    [Fact]
    public async Task Execute_WithInvalidFileModeScreenshotId_ShouldDeleteOwnedPngAndReturnSecurityError()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_ElementScreenshotInvalidId_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var pathCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string validFileId = "shot_0123456789abcdef0123456789abcdef";

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
                var screenshotDirectory = request.Params!.Value.GetProperty("screenshotDirectory").GetString()!;
                var screenshotPath = Path.Combine(screenshotDirectory, validFileId + ".png");
                File.WriteAllBytes(screenshotPath, new byte[] { 137, 80, 78, 71 });
                pathCompletion.TrySetResult(screenshotPath);

                var response = new InspectorResponse
                {
                    Id = request.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.SerializeToElement(new
                    {
                        success = true,
                        screenshotId = "invalid-id",
                        width = 160,
                        height = 80,
                        format = "png",
                        byteLength = 4,
                        path = screenshotPath
                    })
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
        ReplacePipeClient(sessionManager, processId, client);
        var tool = new ElementScreenshotTool(sessionManager);

        try
        {
            var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
            {
                processId,
                outputMode = "file"
            }), CancellationToken.None));
            var screenshotPath = await pathCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));

            result.GetProperty("success").GetBoolean().Should().BeFalse();
            result.GetProperty("errorCode").GetString().Should().Be("SecurityError");
            File.Exists(screenshotPath).Should().BeFalse(
                "unregistered server-owned screenshot bytes must not survive a registration failure");
        }
        finally
        {
            server.Dispose();
            await serverTask;
        }
    }

    [Fact]
    public async Task Execute_WithoutOutputMode_ShouldDefaultToMetadata()
    {
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_ElementScreenshotDefault_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var requestCompletion = new TaskCompletionSource<InspectorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                requestCompletion.TrySetResult(request!);

                var response = new InspectorResponse
                {
                    Id = request!.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.Deserialize<JsonElement>("""{"success":true,"width":160,"height":80,"format":"png","byteLength":256}""")
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

        var sessionManager = new SessionManager();
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
        ReplacePipeClient(sessionManager, processId, client);
        var tool = new ElementScreenshotTool(sessionManager);

        try
        {
            var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new { processId, elementId = "myControl" }), CancellationToken.None));

            result.GetProperty("success").GetBoolean().Should().BeTrue();
            var request = await requestCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            request.Params.Should().NotBeNull();
            request.Params!.Value.TryGetProperty("outputMode", out var outputMode).Should().BeTrue();
            outputMode.GetString().Should().Be("metadata");
        }
        finally
        {
            sessionManager.Dispose();
            server.Dispose();
            await serverTask;
        }
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        ReplaceSessionManagerPipeClient(sessionManager, processId, replacement);
    }
}
