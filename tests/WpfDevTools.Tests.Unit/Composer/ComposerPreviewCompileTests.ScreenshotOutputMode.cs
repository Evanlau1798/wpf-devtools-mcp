using FluentAssertions;
using WpfDevTools.Mcp.Server;
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
    public async Task PreviewUiBlueprintTool_WhenScreenshotBoundsAreInvalid_ShouldRejectBeforePreview()
    {
        using var sessionManager = new SessionManager();

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            sessionManager,
            blueprintJson: "not-json",
            restoreEnabled: false,
            screenshotMaxWidth: 0,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        payload.GetProperty("error").GetString().Should().Contain("screenshotMaxWidth");
    }

    [Fact]
    public async Task PreviewUiBlueprintTool_WhenCorrelationLookupLimitIsInvalid_ShouldRejectBeforePreview()
    {
        using var sessionManager = new SessionManager();

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            sessionManager,
            blueprintJson: "not-json",
            restoreEnabled: false,
            correlationLookupLimit: 65,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        payload.GetProperty("error").GetString().Should().Contain("correlationLookupLimit");
    }
}
