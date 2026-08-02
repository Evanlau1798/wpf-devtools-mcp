using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewEvidenceContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreviewToolPayload_ShouldExposeReusableScreenshotBeforeVerboseHostDiagnostics(bool compact)
    {
        var screenshot = JsonSerializer.SerializeToElement(new
        {
            success = true,
            screenshotId = "shot_01",
            resourceUri = "wpf://screenshots/shot_01",
            resourceRead = new { method = "resources/read" }
        });
        var result = new PreviewBlueprintResult(
            true,
            true,
            true,
            true,
            new string('b', 16_000),
            new string('x', 16_000),
            [],
            new PreviewHostResult(
                "loaded",
                Started: true,
                RuntimeDiagnostics:
                [
                    new PreviewRuntimeDiagnostic("get_ui_summary", true, JsonSerializer.SerializeToElement(new { nodes = new string('n', 16_000) })),
                    new PreviewRuntimeDiagnostic("element_screenshot", true, screenshot)
                ]));

        var payload = JsonSerializer.SerializeToElement(
            UiComposerMcpTools.BuildPreviewToolPayload(result, "draft://sample", compact));

        payload.EnumerateObject().Select(property => property.Name).Should().ContainInOrder(
            "blueprintDraftRef",
            "previewScreenshot",
            "BuildOutput",
            "previewHost");
        payload.GetProperty("previewScreenshot").GetProperty("resourceUri").GetString()
            .Should().Be("wpf://screenshots/shot_01");
    }

    [Fact]
    public void PreviewToolPayload_ShouldPreserveFullXamlWithoutRebuildingPreview()
    {
        var result = new PreviewBlueprintResult(
            true,
            true,
            true,
            true,
            string.Empty,
            "<Button x:Name=\"GeneratedButton\" />",
            [],
            new PreviewHostResult("compiled", Started: false))
        {
            UsesRuntimeDependencies = true,
            ElementCorrelations =
            [
                new RenderElementCorrelation("GeneratedButton", "$.layout", "sample.button")
            ]
        };

        var compact = System.Text.Json.JsonSerializer.SerializeToElement(
            UiComposerMcpTools.BuildPreviewToolPayload(result, "draft://sample", compactRuntimeDiagnostics: true));
        var full = System.Text.Json.JsonSerializer.SerializeToElement(
            UiComposerMcpTools.BuildPreviewToolPayload(result, "draft://sample", compactRuntimeDiagnostics: false));

        compact.GetProperty("xaml").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        compact.GetProperty("generatedXamlOmitted").GetBoolean().Should().BeTrue();
        compact.GetProperty("elementCorrelations").GetArrayLength().Should().Be(0);
        full.GetProperty("xaml").GetString().Should().Contain("GeneratedButton");
        full.GetProperty("generatedXamlOmitted").GetBoolean().Should().BeFalse();
        full.GetProperty("elementCorrelations")[0].GetProperty("ElementName").GetString()
            .Should().Be("GeneratedButton");
    }

    [Fact]
    public void CompiledPreview_ShouldDescribeConfigurationWithoutClaimingHostVisualEvidence()
    {
        var result = new PreviewBlueprintResult(
            true,
            true,
            true,
            true,
            string.Empty,
            "<Window />",
            [],
            new PreviewHostResult("compiled", Started: false))
        {
            UsesRuntimeDependencies = true
        };

        result.VisualFidelity.Should().Be("resource-backed");
        result.VisualValidationGuidance.Should().Contain("host was not started");
        result.VisualComparisonChecklist.Should().OnlyContain(item =>
            !item.Preview.Contains("loads", StringComparison.OrdinalIgnoreCase)
            && !item.Preview.Contains("measures", StringComparison.OrdinalIgnoreCase));
    }
}
