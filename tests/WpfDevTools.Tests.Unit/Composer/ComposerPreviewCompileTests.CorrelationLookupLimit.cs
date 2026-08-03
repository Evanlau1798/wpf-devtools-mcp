using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed partial class ComposerPreviewCompileTests
{
    [Fact]
    public void BuildCorrelationLookupPlan_ShouldKeepContractNamesOutsideCorrelationLimit()
    {
        var correlations = new[]
        {
            new RenderElementCorrelation("Existing01", "$.layout.slots.children[0]", "core.text"),
            new RenderElementCorrelation("Existing02", "$.layout.slots.children[1]", "core.text")
        };

        var plan = UiBlueprintPreviewDiagnosticsBridge.BuildCorrelationLookupPlan(
            correlations,
            exactNameLookupLimit: 1,
            prioritizedElementNames: ["Contract01", "Contract02"]);

        plan.Should().Contain(item => item.Query == "Contract01" && item.MatchMode == "exact");
        plan.Should().Contain(item => item.Query == "Contract02" && item.MatchMode == "exact");
        plan.Should().Contain(item => item.Query == "Existing01" && item.MatchMode == "exact");
        plan.Should().NotContain(item => item.Query == "Existing02" && item.MatchMode == "exact");
    }

    [Fact]
    [Trait("Category", "ComposerRuntime")]
    public async Task PreviewUiBlueprintTool_WithNonDefaultCorrelationLookupLimit_ShouldResolveThirtyThirdExactName()
    {
        using var sensitiveReads = new EnvironmentVariableScope(
            McpServerConfiguration.AllowSensitiveReadsEnvVar,
            "true");
        using var session = SecurePreviewSession.Create();
        using var timeout = CreateTimeout();

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            session.SessionManager,
            CorrelationLookupBlueprint(),
            startHost: true,
            includeRuntimeDiagnostics: true,
            visualLayoutContractJson: """
                {
                  "regions": [{
                    "elementName": "Target33",
                    "bounds": { "x": 0, "y": 0, "width": 1, "height": 1 }
                  }]
                }
                """,
            correlationLookupLimit: 33,
            cancellationToken: timeout.Token);

        result.IsError.Should().BeFalse();
        var layoutRiskSummary = result.StructuredContent!.Value.GetProperty("layoutRiskSummary");
        layoutRiskSummary.GetProperty("correlatedTargetCount").GetInt32().Should().Be(34);
        layoutRiskSummary.GetProperty("resolvedTargetCount").GetInt32().Should().Be(34);
        layoutRiskSummary.GetProperty("inspectedTargetCount").GetInt32().Should().Be(34);
        layoutRiskSummary.GetProperty("inspectionTruncated").GetBoolean().Should().BeFalse();
        layoutRiskSummary.GetProperty("unresolvedCorrelationCount").GetInt32().Should().Be(0);
        layoutRiskSummary.GetProperty("namescopeOnlyCorrelationCount").GetInt32().Should().Be(0);
        result.StructuredContent.Value.GetProperty("previewHost")
            .GetProperty("runtimeDiagnostics")
            .EnumerateArray()
            .Should()
            .Contain(diagnostic => diagnostic.GetProperty("tool").GetString() == "get_namescope"
                                   && diagnostic.GetProperty("success").GetBoolean());
        var payload = result.StructuredContent!.Value;
        var visualContractSummary = payload.GetProperty("visualLayoutContractSummary");
        visualContractSummary.GetProperty("provided").GetBoolean().Should().BeTrue();
        visualContractSummary.GetProperty("evaluatedRegionCount").GetInt32().Should().Be(1);
        visualContractSummary.GetProperty("regions")[0].GetProperty("elementName").GetString().Should().Be("Target33");
        visualContractSummary.GetProperty("regions")[0].GetProperty("actualBounds").ValueKind.Should().Be(JsonValueKind.Object);
        payload.GetProperty("previewHost").GetProperty("runtimeDiagnostics")
            .EnumerateArray()
            .Should().Contain(diagnostic => diagnostic.GetProperty("tool").GetString() == "get_element_snapshot");
    }

    private static string CorrelationLookupBlueprint()
    {
        var children = Enumerable.Range(1, 33)
            .Select(index => new
            {
                kind = "core.text",
                elementName = $"Target{index:00}",
                properties = new { text = $"Target {index}" }
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            schemaVersion = "wpfdevtools.ui-blueprint.v1",
            name = "CorrelationLookupPreview",
            packs = new[] { new { id = "core", version = "0.1.0", required = true, role = "primary" } },
            primaryPack = "core",
            layout = new
            {
                kind = "core.stack",
                slots = new { children }
            }
        });
    }
}
