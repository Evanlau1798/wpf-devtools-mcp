using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed partial class ComposerPreviewCompileTests
{
    [Fact]
    public void PreviewHostPayload_WhenCompact_ShouldKeepScreenshotHandleAndOmitVerbosePayloads()
    {
        var host = new PreviewHostResult(
            "loaded",
            Started: true,
            ViewLoaded: true,
            ProcessId: 42,
            RuntimeDiagnostics:
            [
                new PreviewRuntimeDiagnostic(
                    "get_ui_summary",
                    true,
                    JsonSerializer.SerializeToElement(new
                    {
                        semanticNodeCount = 40,
                        nodes = Enumerable.Repeat(new string('x', 100), 20).ToArray()
                    })),
                new PreviewRuntimeDiagnostic(
                    "find_elements",
                    true,
                    JsonSerializer.SerializeToElement(new
                    {
                        results = new[] { new { elementId = "a" }, new { elementId = "b" } }
                    }))
                {
                    Lookup = new PreviewCorrelationLookup("GeneratedName", "exact")
                },
                new PreviewRuntimeDiagnostic(
                    "find_elements",
                    false,
                    JsonSerializer.SerializeToElement(new { errorCode = "LookupFailed", recovery = "Retry the exact lookup." }))
                {
                    Lookup = new PreviewCorrelationLookup("MissingName", "exact")
                },
                new PreviewRuntimeDiagnostic(
                    "get_clipping_info",
                    false,
                    JsonSerializer.SerializeToElement(new { errorCode = "ProbeFailed", recovery = "Retry the focused probe." }))
                {
                    TargetElementIds = ["a", "b"]
                },
                new PreviewRuntimeDiagnostic(
                    "element_screenshot",
                    true,
                    JsonSerializer.SerializeToElement(new
                    {
                        screenshotId = "shot_01",
                        resourceUri = "wpf://screenshots/shot_01",
                        resourceRead = new { method = "resources/read" }
                    }))
            ]);

        var payload = JsonSerializer.SerializeToElement(
            UiComposerMcpTools.BuildPreviewHostPayload(host, compactRuntimeDiagnostics: true));

        payload.GetProperty("runtimeDiagnosticsCompacted").GetBoolean().Should().BeTrue();
        var diagnostics = payload.GetProperty("runtimeDiagnostics");
        diagnostics[0].GetProperty("tool").GetString().Should().Be("get_ui_summary");
        diagnostics[0].GetProperty("payloadOmitted").GetBoolean().Should().BeTrue();
        diagnostics[0].TryGetProperty("payload", out _).Should().BeFalse();
        diagnostics[1].GetProperty("matchedElementCount").GetInt32().Should().Be(2);
        diagnostics[1].GetProperty("lookup").GetProperty("query").GetString().Should().Be("GeneratedName");
        diagnostics[1].TryGetProperty("targetElementCount", out _).Should().BeFalse();
        diagnostics[2].GetProperty("payload").GetProperty("errorCode").GetString()
            .Should().Be("LookupFailed");
        diagnostics[2].GetProperty("lookup").GetProperty("query").GetString().Should().Be("MissingName");
        diagnostics[3].GetProperty("targetElementCount").GetInt32().Should().Be(2);
        diagnostics[3].GetProperty("payload").GetProperty("errorCode").GetString()
            .Should().Be("ProbeFailed");
        diagnostics[4].GetProperty("payload").GetProperty("resourceUri").GetString()
            .Should().Be("wpf://screenshots/shot_01");

        var fullPayload = JsonSerializer.SerializeToElement(
            UiComposerMcpTools.BuildPreviewHostPayload(host, compactRuntimeDiagnostics: false));
        fullPayload.GetProperty("RuntimeDiagnostics")[0].GetProperty("Payload")
            .GetProperty("nodes").GetArrayLength().Should().Be(20);
        payload.GetRawText().Length.Should().BeLessThan(fullPayload.GetRawText().Length);
        typeof(UiComposerMcpTools).GetMethod(nameof(UiComposerMcpTools.PreviewUiBlueprint))!
            .GetParameters().Single(parameter => parameter.Name == "compactRuntimeDiagnostics")
            .DefaultValue.Should().Be(true);
    }

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
