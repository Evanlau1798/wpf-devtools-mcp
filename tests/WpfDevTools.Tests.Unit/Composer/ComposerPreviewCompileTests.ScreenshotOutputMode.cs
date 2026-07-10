using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed partial class ComposerPreviewCompileTests
{
    [Fact]
    public async Task PreviewUiBlueprintTool_WhenScreenshotOutputModeIsInvalid_ShouldRejectBeforePreview()
    {
        using var sessionManager = new SessionManager();

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            sessionManager,
            blueprintJson: "not-json",
            restoreEnabled: false,
            screenshotOutputMode: "base64",
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        payload.GetProperty("error").GetString().Should().Contain("screenshotOutputMode");
    }

    [Fact]
    public async Task PreviewBlueprintAsync_WhenFileScreenshotRequested_ShouldKeepResourceReadable()
    {
        using var sensitiveReads = new EnvironmentVariableScope(McpServerConfiguration.AllowSensitiveReadsEnvVar, "true");
        using var screenshots = new EnvironmentVariableScope(McpServerConfiguration.AllowScreenshotsEnvVar, "true");
        using var session = SecurePreviewSession.Create();
        var service = new UiBlueprintPreviewService(CreateRegistry(), session.SessionManager);
        using var timeout = CreateTimeout();

        var result = await service.PreviewAsync(
            new PreviewBlueprintRequest(
                ButtonBlueprint(),
                RestoreEnabled: true,
                StartHost: true,
                IncludeScreenshotDiagnostics: true,
                ScreenshotOutputMode: "file"),
            timeout.Token);

        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        var screenshot = result.PreviewHost.RuntimeDiagnostics.Should()
            .ContainSingle(diagnostic => diagnostic.Tool == "element_screenshot" && diagnostic.Success)
            .Subject.Payload;
        screenshot.GetProperty("outputMode").GetString().Should().Be("file");
        var screenshotId = screenshot.GetProperty("screenshotId").GetString();
        screenshotId.Should().NotBeNullOrWhiteSpace();

        var resource = ScreenshotResources.GetScreenshotPng(session.SessionManager, screenshotId!);

        var blob = resource.Should().BeOfType<BlobResourceContents>().Subject;
        blob.DecodedData.ToArray().Should().NotBeEmpty();
    }
}
