using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed partial class ComposerPreviewCompileTests
{
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
            correlationLookupLimit: 33,
            cancellationToken: timeout.Token);

        result.IsError.Should().BeFalse();
        var summary = result.StructuredContent!.Value.GetProperty("layoutRiskSummary");
        summary.GetProperty("correlatedTargetCount").GetInt32().Should().Be(34);
        summary.GetProperty("resolvedTargetCount").GetInt32().Should().Be(34);
        summary.GetProperty("inspectedTargetCount").GetInt32().Should().Be(34);
        summary.GetProperty("inspectionTruncated").GetBoolean().Should().BeFalse();
        summary.GetProperty("unresolvedCorrelationCount").GetInt32().Should().Be(0);
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
