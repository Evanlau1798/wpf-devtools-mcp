using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewEvidenceContractTests
{
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
